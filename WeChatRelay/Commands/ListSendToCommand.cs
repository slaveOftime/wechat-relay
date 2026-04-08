using WeChatRelay.Services;

namespace WeChatRelay.Commands;

public static class ListSendToCommand
{
    public static int Execute(IWeChatService weChat)
    {
        Console.WriteLine("=== Send-to Candidates ===\n");
        var candidates = weChat.GetSendToCandidates();

        if (candidates.Count == 0)
        {
            Console.WriteLine("  (none configured)");
            Console.WriteLine("\nConfigure targets in appsettings.json under WeChat:UserId and WeChat:ToUsers.");
            return 0;
        }

        foreach (var c in candidates)
            Console.WriteLine($"  {c.Id}  ({c.Label})");

        Console.WriteLine($"\nTotal: {candidates.Count} candidate(s)");
        Console.WriteLine("\nUse 'wechat-relay send <candidate> --text <message>' to send.");
        return 0;
    }
}
