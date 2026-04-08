using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using WeChatRelay.Models;
using WeChatRelay.Services;

namespace WeChatRelay.Commands;

public class LoginCommand : AsyncCommand<LoginCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("-f|--force")]
        public bool Force { get; init; }

        [CommandOption("--verbose")]
        public bool Verbose { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var services = new ServiceCollection();
        Program.ConfigureServices(services, settings.Verbose);
        var provider = services.BuildServiceProvider();

        var weChat = provider.GetRequiredService<IWeChatService>();
        var cache = provider.GetRequiredService<ISessionCache>();

        return await ExecuteAsync(weChat, cache, settings, cts.Token);
    }

    private static async Task<int> ExecuteAsync(IWeChatService weChat, ISessionCache cache, Settings settings, CancellationToken ct)
    {
        var cached = cache.Load();
        var localCreds = LoadLocalConfig();

        // Prefer local config file over session cache
        var effective = localCreds is { BotToken: not null } ? localCreds : cached;

        if (!settings.Force && effective is { BotToken: not null })
        {
            var source = localCreds is { BotToken: not null } ? "appsettings.Local.json" : "session cache";
            PrintCreds(source, effective.BotId, effective.UserId);
            return 0;
        }

        if (settings.Force)
        {
            cache.Clear();
            DeleteLocalConfig();
        }

        var panel = new Panel(new Markup("[yellow]Fetching QR code...[/]"))
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Grey);
        AnsiConsole.Write(panel);

        var qr = await weChat.StartQrLoginAsync(ct);

        if (string.IsNullOrEmpty(qr.QrcodeUrl))
        {
            AnsiConsole.MarkupLine($"[bold red]Error:[/] {qr.Qrcode}");
            return 1;
        }

        AnsiConsole.WriteLine();

        var qrPanel = new Panel(new Markup($@"
[bold white]Scan this QR code with WeChat on your phone[/]

[cyan]{qr.QrcodeUrl}[/]

[grey]Waiting for confirmation (8 min timeout)...[/]
"))
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .Header(new PanelHeader("[bold cyan]QR Code Login[/]"));
        AnsiConsole.Write(qrPanel);

        var (ok, token, accountId, baseUrl, userId, msg) = await weChat.WaitForQrConfirmAsync(qr.Qrcode, ct);

        if (!ok)
        {
            AnsiConsole.MarkupLine($"\n[bold red]Login failed:[/] {msg}");
            return 1;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold green]✓ Login successful![/]");

        var config = new WeChatConfig { BotToken = token, BotId = accountId, UserId = userId, BaseUrl = baseUrl, BotType = "3" };
        SaveLocalConfig(config);
        cache.Save(config);

        AnsiConsole.MarkupLine($"\n[bold blue]Run 'wechat-relay listen' to start receiving messages.[/]");
        return 0;
    }

    private static WeChatConfig? LoadLocalConfig()
    {
        try
        {
            if (!File.Exists("appsettings.Local.json")) return null;
            var root = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText("appsettings.Local.json"));
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
        if (File.Exists("appsettings.Local.json"))
        {
            try { existing = JsonSerializer.Deserialize<Dictionary<string, object?>>(File.ReadAllText("appsettings.Local.json")) ?? new(); }
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

        File.WriteAllText("appsettings.Local.json", JsonSerializer.Serialize(existing, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void DeleteLocalConfig()
    {
        if (File.Exists("appsettings.Local.json")) File.Delete("appsettings.Local.json");
    }

    private static void PrintCreds(string source, string? botId, string? userId)
    {
        var panel = new Panel(new Markup($@"
[bold green]✓ Already logged in[/] ([grey]{source}[/])

[bold]Next steps:[/]
  • Run [cyan]wechat-relay listen[/] to start receiving messages
  • Run [cyan]wechat-relay login --force[/] to login with a new account
"))
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Green);
        AnsiConsole.Write(panel);
    }
}
