using WeChatRelay.Models;

namespace WeChatRelay.Services;

internal static class MessageInspector
{
    private const string CdnDownloadBaseUrl = "https://novac2c.cdn.weixin.qq.com/c2c/download?encrypted_query_param=";

    public static string ExtractText(InboundMessage msg)
    {
        var fragments = new List<string>();

        foreach (var item in msg.ItemList)
        {
            if (item.Type == 1 && !string.IsNullOrWhiteSpace(item.TextItem?.Text))
            {
                fragments.Add(item.TextItem.Text);
            }
            else if (item.Type == 3 && !string.IsNullOrWhiteSpace(item.VoiceItem?.Text))
            {
                fragments.Add(item.VoiceItem.Text);
            }
        }

        return string.Join(" ", fragments);
    }

    public static List<HookPayloadItem> BuildHookItems(InboundMessage msg)
    {
        var items = new List<HookPayloadItem>();

        foreach (var item in msg.ItemList)
        {
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
                        PreviewUrl = item.ImageItem.Url,
                        DownloadUrl = BuildDownloadUrl(item.ImageItem.Media),
                        EncryptQueryParam = item.ImageItem.Media?.EncryptQueryParam,
                        AesKey = item.ImageItem.Media?.AesKey ?? item.ImageItem.AesKey
                    });
                    break;
                case 3 when item.VoiceItem is not null:
                    items.Add(new HookPayloadItem
                    {
                        ItemType = item.Type,
                        Kind = "audio",
                        Text = item.VoiceItem.Text,
                        DownloadUrl = BuildDownloadUrl(item.VoiceItem.Media),
                        EncryptQueryParam = item.VoiceItem.Media?.EncryptQueryParam,
                        AesKey = item.VoiceItem.Media?.AesKey,
                        EncodeType = item.VoiceItem.EncodeType,
                        SampleRate = item.VoiceItem.SampleRate,
                        BitsPerSample = item.VoiceItem.BitsPerSample,
                        PlaytimeMs = item.VoiceItem.Playtime
                    });
                    break;
            }
        }

        return items;
    }

    public static string Describe(InboundMessage msg)
    {
        var parts = new List<string>();
        var text = ExtractText(msg);

        if (!string.IsNullOrWhiteSpace(text))
        {
            parts.Add($"text=\"{text}\"");
        }

        var imageCount = msg.ItemList.Count(i => i.Type == 2 && i.ImageItem is not null);
        if (imageCount == 1)
        {
            parts.Add("[image]");
        }
        else if (imageCount > 1)
        {
            parts.Add($"[{imageCount} images]");
        }

        foreach (var voiceItem in msg.ItemList.Where(i => i.Type == 3).Select(i => i.VoiceItem).Where(i => i is not null))
        {
            var duration = voiceItem!.Playtime.HasValue ? $" {voiceItem.Playtime.Value}ms" : string.Empty;
            parts.Add($"[audio{duration}]");
        }

        return string.Join(" ", parts);
    }

    private static string? BuildDownloadUrl(CdnMedia? media)
    {
        if (string.IsNullOrWhiteSpace(media?.EncryptQueryParam))
        {
            return null;
        }

        return $"{CdnDownloadBaseUrl}{Uri.EscapeDataString(media.EncryptQueryParam)}";
    }
}
