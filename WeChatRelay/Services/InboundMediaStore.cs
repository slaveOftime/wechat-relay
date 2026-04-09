using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using WeChatRelay.Models;

namespace WeChatRelay.Services;

public interface IInboundMediaStore
{
    Task<List<HookPayloadItem>> BuildHookItemsAsync(InboundMessage msg, CancellationToken ct = default);
}

public sealed class InboundMediaStore(IHttpClientFactory httpClientFactory, ILogger<InboundMediaStore> log) : IInboundMediaStore
{
    private const string CdnDownloadBaseUrl = "https://novac2c.cdn.weixin.qq.com/c2c/download?encrypted_query_param=";
    private readonly string _mediaRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "wechat-relay",
        "inbound-media");

    public async Task<List<HookPayloadItem>> BuildHookItemsAsync(InboundMessage msg, CancellationToken ct = default)
    {
        var items = new List<HookPayloadItem>();

        for (var index = 0; index < msg.ItemList.Count; index++)
        {
            var item = msg.ItemList[index];

            switch (item.Type)
            {
                case 1 when item.TextItem is not null:
                    items.Add(new HookPayloadItem
                    {
                        ItemType = item.Type,
                        Kind = "text",
                        Text = item.TextItem.Text
                    });
                    break;
                case 2 when item.ImageItem is not null:
                    items.Add(new HookPayloadItem
                    {
                        ItemType = item.Type,
                        Kind = "image",
                        LocalPath = await TrySaveImageAsync(msg, item.ImageItem, index, ct)
                    });
                    break;
                case 3 when item.VoiceItem is not null:
                    items.Add(new HookPayloadItem
                    {
                        ItemType = item.Type,
                        Kind = "audio",
                        Text = item.VoiceItem.Text,
                        LocalPath = await TrySaveVoiceAsync(msg, item.VoiceItem, index, ct),
                        EncodeType = item.VoiceItem.EncodeType,
                        SampleRate = item.VoiceItem.SampleRate,
                        BitsPerSample = item.VoiceItem.BitsPerSample,
                        PlaytimeMs = item.VoiceItem.Playtime
                    });
                    break;
                case 4 when item.FileItem is not null:
                    items.Add(new HookPayloadItem
                    {
                        ItemType = item.Type,
                        Kind = "file",
                        LocalPath = await TrySaveFileAsync(msg, item.FileItem, index, ct)
                    });
                    break;
                case 5 when item.VideoItem is not null:
                    items.Add(new HookPayloadItem
                    {
                        ItemType = item.Type,
                        Kind = "video",
                        LocalPath = await TrySaveVideoAsync(msg, item.VideoItem, index, ct)
                    });
                    break;
            }
        }

        return items;
    }

    private async Task<string?> TrySaveImageAsync(InboundMessage msg, ImageItem item, int index, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.Media?.EncryptQueryParam))
        {
            return null;
        }

        try
        {
            byte[] payload;
            if (!string.IsNullOrWhiteSpace(item.AesKey))
            {
                payload = await DownloadAndDecryptAsync(item.Media.EncryptQueryParam, Convert.FromHexString(item.AesKey), ct);
            }
            else if (!string.IsNullOrWhiteSpace(item.Media.AesKey))
            {
                payload = await DownloadAndDecryptAsync(item.Media.EncryptQueryParam, ParseAesKey(item.Media.AesKey), ct);
            }
            else
            {
                payload = await DownloadPlainAsync(item.Media.EncryptQueryParam, ct);
            }

            var extension = DetectImageExtension(payload);
            return await SaveBufferAsync(msg, index, "image", $"image{extension}", payload, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to save inbound image for seq={Seq} item={ItemIndex}", msg.Seq, index);
            return null;
        }
    }

    private async Task<string?> TrySaveVoiceAsync(InboundMessage msg, VoiceItem item, int index, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.Media?.EncryptQueryParam) || string.IsNullOrWhiteSpace(item.Media.AesKey))
        {
            return null;
        }

        try
        {
            var payload = await DownloadAndDecryptAsync(item.Media.EncryptQueryParam, ParseAesKey(item.Media.AesKey), ct);
            var extension = ResolveAudioExtension(item.EncodeType);
            return await SaveBufferAsync(msg, index, "audio", $"audio{extension}", payload, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to save inbound audio for seq={Seq} item={ItemIndex}", msg.Seq, index);
            return null;
        }
    }

    private async Task<string?> TrySaveFileAsync(InboundMessage msg, FileItem item, int index, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.Media?.EncryptQueryParam) || string.IsNullOrWhiteSpace(item.Media.AesKey))
        {
            return null;
        }

        try
        {
            var payload = await DownloadAndDecryptAsync(item.Media.EncryptQueryParam, ParseAesKey(item.Media.AesKey), ct);
            var fileName = SanitizeFileName(item.FileName, "file.bin");
            return await SaveBufferAsync(msg, index, "file", fileName, payload, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to save inbound file for seq={Seq} item={ItemIndex}", msg.Seq, index);
            return null;
        }
    }

    private async Task<string?> TrySaveVideoAsync(InboundMessage msg, VideoItem item, int index, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.Media?.EncryptQueryParam) || string.IsNullOrWhiteSpace(item.Media.AesKey))
        {
            return null;
        }

        try
        {
            var payload = await DownloadAndDecryptAsync(item.Media.EncryptQueryParam, ParseAesKey(item.Media.AesKey), ct);
            return await SaveBufferAsync(msg, index, "video", "video.mp4", payload, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to save inbound video for seq={Seq} item={ItemIndex}", msg.Seq, index);
            return null;
        }
    }

    private async Task<byte[]> DownloadPlainAsync(string encryptQueryParam, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient();
        var url = BuildDownloadUrl(encryptQueryParam);
        return await client.GetByteArrayAsync(url, ct);
    }

    private async Task<byte[]> DownloadAndDecryptAsync(string encryptQueryParam, byte[] key, CancellationToken ct)
    {
        var encrypted = await DownloadPlainAsync(encryptQueryParam, ct);
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
    }

    private async Task<string> SaveBufferAsync(InboundMessage msg, int index, string kind, string fileName, byte[] payload, CancellationToken ct)
    {
        var targetPath = BuildTargetPath(msg, index, kind, fileName);
        if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
        {
            return targetPath;
        }

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(tempPath, payload, ct);

        try
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            File.Move(tempPath, targetPath);
            return targetPath;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private string BuildTargetPath(InboundMessage msg, int index, string kind, string fileName)
    {
        var timestamp = msg.CreateTimeMs.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(msg.CreateTimeMs.Value)
            : DateTimeOffset.UtcNow;
        var messageKey = $"msg-{msg.MessageId?.ToString() ?? "unknown"}-{msg.Seq?.ToString() ?? "unknown"}";
        var directory = Path.Combine(_mediaRoot, timestamp.ToString("yyyyMMdd"), SanitizeFileName(messageKey, "message"));
        var indexedFileName = $"{index:D2}-{kind}-{SanitizeFileName(fileName, $"{kind}.bin")}";
        return Path.GetFullPath(Path.Combine(directory, indexedFileName));
    }

    private static string BuildDownloadUrl(string encryptQueryParam)
        => $"{CdnDownloadBaseUrl}{Uri.EscapeDataString(encryptQueryParam)}";

    private static byte[] ParseAesKey(string aesKeyBase64)
    {
        var decoded = Convert.FromBase64String(aesKeyBase64);
        if (decoded.Length == 16)
        {
            return decoded;
        }

        if (decoded.Length == 32)
        {
            var ascii = Encoding.ASCII.GetString(decoded);
            if (ascii.All(Uri.IsHexDigit))
            {
                return Convert.FromHexString(ascii);
            }
        }

        throw new InvalidOperationException($"Unsupported aes_key format. Expected 16 raw bytes or 32 ASCII hex bytes, got {decoded.Length} bytes.");
    }

    private static string ResolveAudioExtension(int? encodeType) => encodeType switch
    {
        1 => ".wav",
        2 => ".adpcm",
        3 => ".feature",
        4 => ".spx",
        5 => ".amr",
        6 => ".silk",
        7 => ".mp3",
        8 => ".ogg",
        _ => ".bin"
    };

    private static string DetectImageExtension(byte[] payload)
    {
        if (payload.Length >= 3 && payload[0] == 0xFF && payload[1] == 0xD8 && payload[2] == 0xFF)
        {
            return ".jpg";
        }

        if (payload.Length >= 8 && payload[0] == 0x89 && payload[1] == 0x50 && payload[2] == 0x4E && payload[3] == 0x47)
        {
            return ".png";
        }

        if (payload.Length >= 6 && payload[0] == 0x47 && payload[1] == 0x49 && payload[2] == 0x46)
        {
            return ".gif";
        }

        if (payload.Length >= 12 && payload[0] == 0x52 && payload[1] == 0x49 && payload[2] == 0x46 && payload[3] == 0x46 && payload[8] == 0x57 && payload[9] == 0x45 && payload[10] == 0x42 && payload[11] == 0x50)
        {
            return ".webp";
        }

        if (payload.Length >= 2 && payload[0] == 0x42 && payload[1] == 0x4D)
        {
            return ".bmp";
        }

        return ".bin";
    }

    private static string SanitizeFileName(string? value, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(candidate.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }
}