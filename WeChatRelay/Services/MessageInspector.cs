using WeChatRelay.Models;

namespace WeChatRelay.Services;

internal static class MessageInspector
{
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

        var videoCount = msg.ItemList.Count(i => i.Type == 5 && i.VideoItem is not null);
        if (videoCount == 1)
        {
            parts.Add("[video]");
        }
        else if (videoCount > 1)
        {
            parts.Add($"[{videoCount} videos]");
        }

        var fileCount = msg.ItemList.Count(i => i.Type == 4 && i.FileItem is not null);
        if (fileCount == 1)
        {
            parts.Add("[file]");
        }
        else if (fileCount > 1)
        {
            parts.Add($"[{fileCount} files]");
        }

        foreach (var voiceItem in msg.ItemList.Where(i => i.Type == 3).Select(i => i.VoiceItem).Where(i => i is not null))
        {
            var duration = voiceItem!.Playtime.HasValue ? $" {voiceItem.Playtime.Value}ms" : string.Empty;
            parts.Add($"[audio{duration}]");
        }

        return string.Join(" ", parts);
    }
}
