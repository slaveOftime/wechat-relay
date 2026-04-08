using WeChatRelay.Services;

namespace WeChatRelay.Commands;

public static class SendCommand
{
    public static async Task<int> ExecuteAsync(IWeChatService weChat, ISessionCache cache, string[] args, CancellationToken ct = default)
    {
        if (cache.Load() is not { BotToken: not null })
        { Console.WriteLine("⚠ Not logged in. Run 'wechat-relay login' first."); return 1; }

        var (target, text) = ParseArgs(args);
        var toUser = string.IsNullOrEmpty(target) ? cache.Load()?.UserId : target;

        if (string.IsNullOrEmpty(toUser))
        { Console.WriteLine("⚠ No target configured. Set WeChat:UserId or pass a candidate ID."); return 1; }

        var message = text ?? Console.ReadLine() ?? "";
        if (string.IsNullOrEmpty(message))
        { Console.WriteLine("⚠ No message. Use --text <msg> or pipe via stdin."); return 1; }

        Console.WriteLine($"Sending to {toUser}...");
        var contextToken = cache.GetContextToken(toUser);
        var result = await weChat.SendTextAsync(toUser, message, ct, contextToken);

        if (result.Ret == 0) { Console.WriteLine("✓ Sent."); return 0; }

        Console.WriteLine($"✗ Failed: {result.ErrMsg ?? "Unknown"} (ret={result.Ret})");
        if (result.Ret == -2)
        {
            Console.WriteLine("\nHint: API needs a context_token from a prior inbound message.");
            Console.WriteLine("  Run 'wechat-relay listen' and have the user message you first.");
        }
        return 1;
    }

    private static (string? Target, string? Text) ParseArgs(string[] args)
    {
        string? target = null, text = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--text" && i + 1 < args.Length) text = args[++i];
            else if (target is null) target = args[i];
        }
        return (target, text);
    }
}
