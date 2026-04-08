using WeChatRelay.Services;

namespace WeChatRelay.Commands;

public static class ListenCommand
{
    public static async Task<int> ExecuteAsync(IWeChatService weChat, IHookRunner hookRunner, ISessionCache cache, CancellationToken ct)
    {
        if (cache.Load() is not { BotToken: not null })
        { Console.WriteLine("⚠ Not logged in. Run 'wechat-relay login' first."); return 1; }

        Console.WriteLine("=== Listening for WeChat Messages ===");
        Console.WriteLine("Press Ctrl+C to stop.\n");

        try
        {
            var hookTask = hookRunner.ProcessLoopAsync(ct);
            await weChat.StartReceivingAsync(async msg =>
            {
                var text = ExtractText(msg);
                var time = msg.CreateTimeMs.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(msg.CreateTimeMs.Value).LocalDateTime.ToString("HH:mm:ss")
                    : "??:??:??";

                Console.WriteLine($"[{time}] seq={msg.Seq} from={msg.FromUserId} type={msg.MessageType} text=\"{Truncate(text, 80)}\"");

                if (!string.IsNullOrEmpty(msg.FromUserId) && !string.IsNullOrEmpty(msg.ContextToken))
                    cache.SetContextToken(msg.FromUserId, msg.ContextToken);

                hookRunner.Enqueue(msg);
            }, ct);

            await hookTask;
        }
        catch (OperationCanceledException) { }

        Console.WriteLine("\nStopped.");
        return 0;
    }

    private static string ExtractText(Models.InboundMessage msg) =>
        string.Join(" ", msg.ItemList.Where(i => i.Type == 1 && i.TextItem != null).Select(i => i.TextItem!.Text));

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
