using System.Text.Json;
using WeChatRelay.Models;
using WeChatRelay.Services;

namespace WeChatRelay.Commands;

public static class LoginCommand
{
    private const string LocalConfigPath = "appsettings.Local.json";

    public static async Task<int> ExecuteAsync(IWeChatService weChat, ISessionCache cache, string[] args, CancellationToken ct = default)
    {
        Console.WriteLine("=== WeChat QR Login ===\n");

        var cached = cache.Load();
        var localCreds = LoadLocalConfig();
        var force = args.Contains("--force") || args.Contains("-f");

        // Prefer local config file over session cache
        var effective = localCreds is { BotToken: not null } ? localCreds : cached;

        if (!force && effective is { BotToken: not null })
        {
            var source = localCreds is { BotToken: not null } ? "appsettings.Local.json" : "session cache";
            PrintCreds(source, effective.BotId, effective.UserId);
            return 0;
        }

        if (force) { cache.Clear(); DeleteLocalConfig(); Console.WriteLine("Credentials cleared.\n"); }

        Console.WriteLine("Fetching QR code...");
        var qr = await weChat.StartQrLoginAsync(ct);

        if (string.IsNullOrEmpty(qr.QrcodeUrl))
        { Console.WriteLine($"Error: {qr.Qrcode}"); return 1; }

        Console.WriteLine();
        Console.WriteLine("┌─────────────────────────────────────────────────┐");
        Console.WriteLine("│  Scan this QR code with WeChat on your phone    │");
        Console.WriteLine("│                                                 │");
        Console.WriteLine($"│  {qr.QrcodeUrl}  │");
        Console.WriteLine("│                                                 │");
        Console.WriteLine("│  Waiting for confirmation (8 min timeout)...    │");
        Console.WriteLine("└─────────────────────────────────────────────────┘");
        Console.WriteLine();

        var (ok, token, accountId, baseUrl, userId, msg) = await weChat.WaitForQrConfirmAsync(qr.Qrcode, ct);

        if (!ok) { Console.WriteLine($"Login failed: {msg}"); return 1; }

        Console.WriteLine("\n✓ Login successful!\n");

        var config = new WeChatConfig { BotToken = token, BotId = accountId, UserId = userId, BaseUrl = baseUrl, BotType = "3" };
        SaveLocalConfig(config);
        cache.Save(config);

        Console.WriteLine("Credentials saved to appsettings.Local.json and session cache.");
        Console.WriteLine($"  Bot ID:   {accountId}");
        Console.WriteLine($"  User ID:  {userId}");
        Console.WriteLine("\nRun 'wechat-relay listen' to start receiving messages.");
        return 0;
    }

    private static WeChatConfig? LoadLocalConfig()
    {
        try
        {
            if (!File.Exists(LocalConfigPath)) return null;
            var root = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(LocalConfigPath));
            if (!root.TryGetProperty("WeChat", out var wc)) return null;
            return new WeChatConfig
            {
                BotToken = wc.GetProperty("BotToken").GetString(),
                BotId = wc.GetProperty("BotId").GetString(),
                UserId = wc.GetProperty("UserId").GetString(),
                BaseUrl = wc.GetProperty("BaseUrl").GetString(),
                BotType = wc.TryGetProperty("BotType", out var bt) ? (bt.GetString() ?? "3") : "3",
                ToUsers = wc.TryGetProperty("ToUsers", out var tu) ? tu.GetString() : null
            };
        }
        catch { return null; }
    }

    private static void SaveLocalConfig(WeChatConfig c)
    {
        var existing = new Dictionary<string, object?>();
        if (File.Exists(LocalConfigPath))
        {
            try { existing = JsonSerializer.Deserialize<Dictionary<string, object?>>(File.ReadAllText(LocalConfigPath)) ?? new(); }
            catch { existing = new(); }
        }

        existing["WeChat"] = new
        {
            BotToken = c.BotToken,
            BotId = c.BotId,
            UserId = c.UserId,
            BaseUrl = c.BaseUrl,
            BotType = c.BotType,
            ToUsers = c.ToUsers
        };

        File.WriteAllText(LocalConfigPath, JsonSerializer.Serialize(existing, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void DeleteLocalConfig()
    {
        if (File.Exists(LocalConfigPath)) File.Delete(LocalConfigPath);
    }

    private static void PrintCreds(string source, string? botId, string? userId)
    {
        Console.WriteLine($"✓ Valid credentials found in {source}.");
        Console.WriteLine($"  Bot ID:  {botId}");
        Console.WriteLine($"  User ID: {userId}");
        Console.WriteLine("\nTo force a new login: wechat-relay login --force");
    }
}
