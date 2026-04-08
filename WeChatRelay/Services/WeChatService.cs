using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using WeChatRelay.Models;

namespace WeChatRelay.Services;

public interface IWeChatService
{
    bool IsLoggedIn { get; }
    Task<QrStartResponse> StartQrLoginAsync(CancellationToken ct = default);
    Task<(bool Connected, string? BotToken, string? AccountId, string? BaseUrl, string? UserId, string Message)> WaitForQrConfirmAsync(string sessionKey, CancellationToken ct = default);
    Task<SendMessageResponse> SendTextAsync(string toUserId, string content, CancellationToken ct = default, string? contextToken = null);
    Task StartReceivingAsync(Func<InboundMessage, Task> onMessage, CancellationToken ct = default);
    List<SendToCandidate> GetSendToCandidates();
}

public class WeChatService(HttpClient http, WeChatConfig cfg, ILogger<WeChatService> log) : IWeChatService
{
    private const string DefaultBaseUrl = "https://ilinkai.weixin.qq.com/";
    private const int SessionExpiredErrCode = -14;
    private static readonly TimeSpan QrPollTimeout = TimeSpan.FromSeconds(35);
    private static readonly TimeSpan QrLoginTimeout = TimeSpan.FromMinutes(8);
    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOpts = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public bool IsLoggedIn => !string.IsNullOrEmpty(cfg.BotToken) && !string.IsNullOrEmpty(cfg.BaseUrl);

    public async Task<QrStartResponse> StartQrLoginAsync(CancellationToken ct = default)
    {
        var url = new Uri(new Uri(NormalizeBaseUrl(cfg.BaseUrl ?? DefaultBaseUrl)),
            $"ilink/bot/get_bot_qrcode?bot_type={Uri.EscapeDataString(cfg.BotType ?? "3")}");

        log.LogInformation("Fetching QR code...");
        var resp = await http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<QrStartResponse>(json, ReadOpts)
            ?? new QrStartResponse { Qrcode = "Failed to parse response" };

        if (!string.IsNullOrEmpty(result.QrcodeUrl))
            log.LogInformation("QR code obtained. Session: {Session}", result.Qrcode);

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
                    if (!scannedLogged) { log.LogInformation("QR scanned. Confirm on your phone..."); scannedLogged = true; }
                    break;
                case "expired": return (false, null, null, null, null, "QR expired. Generate a new one.");
                case "confirmed":
                    if (string.IsNullOrEmpty(status.AccountId))
                        return (false, null, null, null, null, "Missing ilink_bot_id from server.");
                    log.LogInformation("Login confirmed: {Id}", status.AccountId);
                    return (true, status.BotToken, status.AccountId,
                        string.IsNullOrWhiteSpace(status.BaseUrl) ? baseUrl : NormalizeBaseUrl(status.BaseUrl),
                        status.UserId, "OK");
            }
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
        return (false, null, null, null, null, "Login timed out.");
    }

    public async Task<SendMessageResponse> SendTextAsync(string toUserId, string content, CancellationToken ct = default, string? contextToken = null)
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
                ItemList = [new OutboundItem { Type = 1, TextItem = new TextItemOut { Text = content } }]
            }
        };

        var json = JsonSerializer.Serialize(req, WriteOpts);
        var url = new Uri(new Uri(NormalizeBaseUrl(cfg.BaseUrl!)), "ilink/bot/sendmessage");

        ApplyHeaders();
        var resp = await http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"), ct);
        resp.EnsureSuccessStatusCode();

        var respJson = await resp.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<SendMessageResponse>(respJson, ReadOpts)
            ?? new SendMessageResponse { Ret = -1, ErrMsg = "Failed to parse response" };

        if (result.Ret == 0) log.LogInformation("Message sent to {User}", toUserId);
        else log.LogWarning("Send failed: {Ret} {Err}", result.Ret, result.ErrMsg);

        return result;
    }

    public async Task StartReceivingAsync(Func<InboundMessage, Task> onMessage, CancellationToken ct = default)
    {
        if (!IsLoggedIn) { log.LogWarning("Not logged in"); return; }

        var buf = string.Empty;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var reqJson = JsonSerializer.Serialize(new { get_updates_buf = buf });
                var url = new Uri(new Uri(NormalizeBaseUrl(cfg.BaseUrl!)), "ilink/bot/getupdates");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(40));

                ApplyHeaders();
                var resp = await http.PostAsync(url, new StringContent(reqJson, Encoding.UTF8, "application/json"), cts.Token);
                resp.EnsureSuccessStatusCode();

                var respJson = await resp.Content.ReadAsStringAsync(ct);
                var result = JsonSerializer.Deserialize<GetUpdatesResponse>(respJson, ReadOpts);

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
                        log.LogError("Session expired. Re-login required.");
                        await Task.Delay(TimeSpan.FromMinutes(2), ct);
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
            return JsonSerializer.Deserialize<QrStatusResponse>(text, ReadOpts) ?? new QrStatusResponse { Status = "wait" };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new QrStatusResponse { Status = "wait" };
        }
    }

    private static string NormalizeBaseUrl(string u) => u.EndsWith('/') ? u : $"{u}/";
    private static string GenerateRandomUin() => Convert.ToBase64String(BitConverter.GetBytes(Random.Shared.Next(int.MinValue, int.MaxValue)));
}
