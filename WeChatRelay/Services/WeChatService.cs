using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WeChatRelay.Models;
using WeChatRelay.Serialization;

namespace WeChatRelay.Services;

public interface IWeChatService
{
    bool IsLoggedIn { get; }
    Task<QrStartResponse> StartQrLoginAsync(CancellationToken ct = default);
    Task<(bool Connected, string? BotToken, string? AccountId, string? BaseUrl, string? UserId, string Message)> WaitForQrConfirmAsync(string sessionKey, CancellationToken ct = default);
    Task<SendMessageResponse> SendTextAsync(string toUserId, string content, CancellationToken ct = default, string? contextToken = null);
    Task<SendMessageResponse> SendImageAsync(string toUserId, string filePath, CancellationToken ct = default, string? contextToken = null);
    Task<SendMessageResponse> SendAudioAsync(string toUserId, string filePath, AudioSendOptions options, CancellationToken ct = default, string? contextToken = null);
    Task StartReceivingAsync(Func<InboundMessage, Task> onMessage, CancellationToken ct = default);
    List<SendToCandidate> GetSendToCandidates();
}

public class WeChatService(
    HttpClient http,
    WeChatConfig cfg,
    ILoginSessionStore loginSessionStore,
    IContextTokenStore contextTokenStore,
    ILogger<WeChatService> log) : IWeChatService
{
    private const string DefaultBaseUrl = "https://ilinkai.weixin.qq.com/";
    private const string CdnBaseUrl = "https://novac2c.cdn.weixin.qq.com/c2c/";
    private const int SessionExpiredErrCode = -14;
    private static readonly TimeSpan QrPollTimeout = TimeSpan.FromSeconds(35);
    private static readonly TimeSpan QrLoginTimeout = TimeSpan.FromMinutes(8);

    public bool IsLoggedIn => !string.IsNullOrEmpty(cfg.BotToken) && !string.IsNullOrEmpty(cfg.BaseUrl);

    public async Task<QrStartResponse> StartQrLoginAsync(CancellationToken ct = default)
    {
        var url = new Uri(new Uri(NormalizeBaseUrl(cfg.BaseUrl ?? DefaultBaseUrl)),
            $"ilink/bot/get_bot_qrcode?bot_type={Uri.EscapeDataString(cfg.BotType ?? "3")}");

        var resp = await http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize(json, WeChatJsonContext.Default.QrStartResponse)
            ?? new QrStartResponse { Qrcode = "Failed to parse response" };

        return result;
    }

    public async Task<(bool Connected, string? BotToken, string? AccountId, string? BaseUrl, string? UserId, string Message)> WaitForQrConfirmAsync(string sessionKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionKey))
            return (false, null, null, null, null, "Missing session key");

        var baseUrl = NormalizeBaseUrl(cfg.BaseUrl ?? DefaultBaseUrl);
        var deadline = DateTimeOffset.UtcNow + QrLoginTimeout;
        var scannedLogged = false;

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var status = await PollQrStatusAsync(baseUrl, sessionKey, ct);

            switch (status.Status)
            {
                case "wait": break;
                case "scaned":
                    if (!scannedLogged) { scannedLogged = true; }
                    break;
                case "expired": return (false, null, null, null, null, "QR expired. Generate a new one.");
                case "confirmed":
                    if (string.IsNullOrEmpty(status.AccountId))
                        return (false, null, null, null, null, "Missing ilink_bot_id from server.");
                    return (true, status.BotToken, status.AccountId,
                        string.IsNullOrWhiteSpace(status.BaseUrl) ? baseUrl : NormalizeBaseUrl(status.BaseUrl),
                        status.UserId, "OK");
            }
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
        return (false, null, null, null, null, "Login timed out.");
    }

    public async Task<SendMessageResponse> SendTextAsync(string toUserId, string content, CancellationToken ct = default, string? contextToken = null)
        => await SendItemsAsync(toUserId,
        [
            new OutboundItem
            {
                Type = 1,
                TextItem = new TextItemOut { Text = content }
            }
        ], ct, contextToken);

    public async Task<SendMessageResponse> SendImageAsync(string toUserId, string filePath, CancellationToken ct = default, string? contextToken = null)
    {
        var payload = await File.ReadAllBytesAsync(filePath, ct);
        var uploaded = await UploadMediaAsync(toUserId, payload, mediaType: 1, ct);

        return await SendItemsAsync(toUserId,
        [
            new OutboundItem
            {
                Type = 2,
                ImageItem = new ImageItemOut
                {
                    Media = uploaded.Media,
                    MidSize = uploaded.CiphertextSize,
                    HdSize = uploaded.CiphertextSize
                }
            }
        ], ct, contextToken);
    }

    public async Task<SendMessageResponse> SendAudioAsync(string toUserId, string filePath, AudioSendOptions options, CancellationToken ct = default, string? contextToken = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var payload = await File.ReadAllBytesAsync(filePath, ct);
        var uploaded = await UploadMediaAsync(toUserId, payload, mediaType: 4, ct);

        return await SendItemsAsync(toUserId,
        [
            new OutboundItem
            {
                Type = 3,
                VoiceItem = new VoiceItemOut
                {
                    Media = uploaded.Media,
                    EncodeType = options.EncodeType,
                    BitsPerSample = options.BitsPerSample,
                    SampleRate = options.SampleRate,
                    Playtime = options.PlaytimeMs
                }
            }
        ], ct, contextToken);
    }

    private async Task<SendMessageResponse> SendItemsAsync(string toUserId, IEnumerable<OutboundItem> itemList, CancellationToken ct, string? contextToken)
    {
        if (!IsLoggedIn)
            return new SendMessageResponse { Ret = -1, ErrMsg = "Not logged in" };

        var req = new SendMessageRequest
        {
            Msg = new OutboundMessage
            {
                FromUserId = cfg.BotId,
                ToUserId = toUserId,
                ClientId = $"wechat-relay-{Guid.NewGuid():N}",
                MessageType = 2,
                MessageState = 2,
                ContextToken = contextToken,
                ItemList = itemList.ToList()
            }
        };

        var result = await PostJsonAsync(
            new Uri(new Uri(NormalizeBaseUrl(cfg.BaseUrl!)), "ilink/bot/sendmessage"),
            req,
            WeChatJsonContext.Default.SendMessageRequest,
            WeChatJsonContext.Default.SendMessageResponse,
            ct) ?? new SendMessageResponse { Ret = -1, ErrMsg = "Failed to parse response" };

        if (result.Ret == SessionExpiredErrCode)
        {
            InvalidateSession("Session expired while sending a message. Re-login required.");
        }

        if (result.Ret != 0) log.LogWarning("Send failed: {Ret} {Err}", result.Ret, result.ErrMsg);

        return result;
    }

    private async Task<UploadedMedia> UploadMediaAsync(string toUserId, byte[] payload, int mediaType, CancellationToken ct)
    {
        var aesKey = RandomNumberGenerator.GetBytes(16);
        var aesKeyHex = Convert.ToHexString(aesKey).ToLowerInvariant();
        var fileKey = Guid.NewGuid().ToString("N");
        var ciphertext = EncryptAesEcb(payload, aesKey);

        var uploadParams = await PostJsonAsync(
            new Uri(new Uri(NormalizeBaseUrl(cfg.BaseUrl!)), "ilink/bot/getuploadurl"),
            new GetUploadUrlRequest
            {
                AesKey = aesKeyHex,
                FileKey = fileKey,
                FileSize = ciphertext.Length,
                MediaType = mediaType,
                RawFileMd5 = ComputeMd5Hex(payload),
                RawSize = payload.Length,
                ToUserId = toUserId
            },
            WeChatJsonContext.Default.GetUploadUrlRequest,
            WeChatJsonContext.Default.GetUploadUrlResponse,
            ct);

        var uploadParam = uploadParams?.UploadParam;
        if (string.IsNullOrWhiteSpace(uploadParam))
            throw new InvalidOperationException("Upload endpoint did not return upload parameters.");

        var encryptedParam = await UploadToCdnAsync(uploadParam, fileKey, ciphertext, ct);
        var encodedAesKey = Convert.ToBase64String(Encoding.ASCII.GetBytes(aesKeyHex));

        return new UploadedMedia(
            new CdnMedia
            {
                EncryptQueryParam = encryptedParam,
                AesKey = encodedAesKey,
                EncryptType = 1
            },
            ciphertext.Length);
    }

    public async Task StartReceivingAsync(Func<InboundMessage, Task> onMessage, CancellationToken ct = default)
    {
        if (!IsLoggedIn) { log.LogWarning("Not logged in"); return; }

        var buf = string.Empty;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var reqJson = JsonSerializer.Serialize(
                    new GetUpdatesRequest { GetUpdatesBuf = buf },
                    WeChatJsonContext.Default.GetUpdatesRequest);
                var url = new Uri(new Uri(NormalizeBaseUrl(cfg.BaseUrl!)), "ilink/bot/getupdates");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(40));

                ApplyHeaders();
                var resp = await http.PostAsync(url, new StringContent(reqJson, Encoding.UTF8, "application/json"), cts.Token);
                resp.EnsureSuccessStatusCode();

                var respJson = await resp.Content.ReadAsStringAsync(ct);
                var result = JsonSerializer.Deserialize(respJson, WeChatJsonContext.Default.GetUpdatesResponse);

                if (result?.Ret == 0)
                {
                    buf = result.GetUpdatesBuf;
                    foreach (var msg in result.Msgs)
                        await onMessage(msg);
                }
                else
                {
                    var code = result?.ErrCode ?? result?.Ret ?? 0;
                    if (code == SessionExpiredErrCode)
                    {
                        InvalidateSession("Session expired. Re-login required.");
                        break;
                    }
                    else
                    {
                        log.LogWarning("getupdates failed: {Ret} {Err}", result?.Ret, result?.ErrMsg);
                        await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "Receive loop error");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    public List<SendToCandidate> GetSendToCandidates()
    {
        var candidates = new List<SendToCandidate>();

        if (!string.IsNullOrEmpty(cfg.UserId))
            candidates.Add(new SendToCandidate(cfg.UserId, $"{cfg.UserId} (self)", "wechat"));

        if (!string.IsNullOrEmpty(cfg.ToUsers))
        {
            foreach (var u in cfg.ToUsers.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var uid = u.Trim();
                if (!candidates.Any(c => c.Id == uid))
                    candidates.Add(new SendToCandidate(uid, uid, "wechat"));
            }
        }

        return candidates;
    }

    private void ApplyHeaders()
    {
        http.DefaultRequestHeaders.Clear();
        http.DefaultRequestHeaders.Add("AuthorizationType", "ilink_bot_token");
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {cfg.BotToken}");
        http.DefaultRequestHeaders.Add("X-WECHAT-UIN", GenerateRandomUin());
    }

    private async Task<TResponse?> PostJsonAsync<TRequest, TResponse>(
        Uri url,
        TRequest payload,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, requestTypeInfo);
        ApplyHeaders();

        var resp = await http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"), ct);
        resp.EnsureSuccessStatusCode();

        var respJson = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize(respJson, responseTypeInfo);
    }

    private async Task<string> UploadToCdnAsync(string uploadParam, string fileKey, byte[] ciphertext, CancellationToken ct)
    {
        http.DefaultRequestHeaders.Clear();

        var uploadUrl = new Uri(new Uri(CdnBaseUrl),
            $"upload?encrypted_query_param={Uri.EscapeDataString(uploadParam)}&filekey={Uri.EscapeDataString(fileKey)}");
        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl)
        {
            Content = new ByteArrayContent(ciphertext)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"CDN upload failed: {(int)response.StatusCode} {detail}");
        }

        if (response.Headers.TryGetValues("x-encrypted-param", out var values))
        {
            var headerValue = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                return headerValue;
            }
        }

        return uploadParam;
    }

    private static byte[] EncryptAesEcb(byte[] payload, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;

        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(payload, 0, payload.Length);
    }

    private static string ComputeMd5Hex(byte[] payload)
    {
        var hash = MD5.HashData(payload);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<QrStatusResponse> PollQrStatusAsync(string baseUrl, string qrcode, CancellationToken ct)
    {
        var url = new Uri(new Uri(baseUrl), $"ilink/bot/get_qrcode_status?qrcode={Uri.EscapeDataString(qrcode)}");
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("iLink-App-ClientVersion", "1");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(QrPollTimeout);

        try
        {
            var resp = await http.SendAsync(req, cts.Token);
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"QR poll failed: {(int)resp.StatusCode}");
            return JsonSerializer.Deserialize(text, WeChatJsonContext.Default.QrStatusResponse) ?? new QrStatusResponse { Status = "wait" };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new QrStatusResponse { Status = "wait" };
        }
    }

    private static string NormalizeBaseUrl(string u) => u.EndsWith('/') ? u : $"{u}/";
    private static string GenerateRandomUin() => Convert.ToBase64String(BitConverter.GetBytes(Random.Shared.Next(int.MinValue, int.MaxValue)));

    private void InvalidateSession(string message)
    {
        loginSessionStore.Clear();
        contextTokenStore.Clear();
        log.LogError(message);
    }

    private sealed record UploadedMedia(CdnMedia Media, long CiphertextSize);
}
