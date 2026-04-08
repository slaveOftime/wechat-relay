using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using WeChatRelay.Models;
using WeChatRelay.Services;

namespace WeChatRelay.Commands;

public sealed class LoginCommandSettings : VerboseCommandSettings
{
    public bool Force { get; init; }
}

public static class LoginCommand
{
    public static async Task<int> ExecuteAsync(LoginCommandSettings settings, CancellationToken cancellationToken)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

        using var provider = Program.CreateServiceProvider(settings.Verbose);

        var weChat = provider.GetRequiredService<IWeChatService>();
        var loginSessionStore = provider.GetRequiredService<ILoginSessionStore>();
        var contextTokenStore = provider.GetRequiredService<IContextTokenStore>();

        return await ExecuteCoreAsync(weChat, loginSessionStore, contextTokenStore, settings, linkedCts.Token);
    }

    private static async Task<int> ExecuteCoreAsync(
        IWeChatService weChat,
        ILoginSessionStore loginSessionStore,
        IContextTokenStore contextTokenStore,
        LoginCommandSettings settings,
        CancellationToken ct)
    {
        var session = loginSessionStore.Load();

        if (!settings.Force && session is not null)
        {
            PrintCreds("local session store", session.BotId, session.UserId);
            return 0;
        }

        if (settings.Force)
        {
            loginSessionStore.Clear();
            contextTokenStore.Clear();
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

        var qrPanel = new Panel(new Markup($@"[bold white]Scan this QR code with WeChat on your phone[/]

[cyan]{qr.QrcodeUrl}[/]

[grey]Waiting for confirmation (8 min timeout)...[/]"))
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

        contextTokenStore.Clear();
        loginSessionStore.Save(new WeChatLoginSession
        {
            BotToken = token!,
            BotId = accountId!,
            UserId = userId,
            BaseUrl = baseUrl!,
            BotType = "3"
        });

        AnsiConsole.MarkupLine($"\n[bold blue]Run 'wechat-relay listen' to start receiving messages.[/]");
        return 0;
    }

    private static void PrintCreds(string source, string? botId, string? userId)
    {
        var panel = new Panel(new Markup($@"[bold green]✓ Already logged in[/] ([grey]{source}[/])

[bold]Next steps:[/]
  • Run [cyan]wechat-relay listen[/] to start receiving messages
  • Run [cyan]wechat-relay login --force[/] to login with a new account"))
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Green);
        AnsiConsole.Write(panel);
    }
}
