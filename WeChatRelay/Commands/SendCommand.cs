using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using WeChatRelay.Models;
using WeChatRelay.Services;

namespace WeChatRelay.Commands;

public sealed class SendCommandSettings : VerboseCommandSettings
{
    public string? Target { get; init; }

    public string? Text { get; init; }
}

public static class SendCommand
{
    public static async Task<int> ExecuteAsync(SendCommandSettings settings, CancellationToken cancellationToken)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

        using var provider = Program.CreateServiceProvider(settings.Verbose);

        var weChat = provider.GetRequiredService<IWeChatService>();
        var contextTokenStore = provider.GetRequiredService<IContextTokenStore>();
        var config = provider.GetRequiredService<WeChatConfig>();

        return await ExecuteCoreAsync(weChat, contextTokenStore, config, settings, linkedCts.Token);
    }

    private static async Task<int> ExecuteCoreAsync(
        IWeChatService weChat,
        IContextTokenStore contextTokenStore,
        WeChatConfig config,
        SendCommandSettings settings,
        CancellationToken ct)
    {
        if (!weChat.IsLoggedIn)
        {
            AnsiConsole.MarkupLine("[bold red]⚠ Not logged in.[/] Run [cyan]wechat-relay login[/] first.");
            return 1;
        }

        var toUser = string.IsNullOrEmpty(settings.Target) ? config.UserId : settings.Target;

        if (string.IsNullOrEmpty(toUser))
        {
            AnsiConsole.MarkupLine("[bold red]⚠ No target configured.[/] Set WeChat:UserId or pass a candidate ID.");
            return 1;
        }

        var message = settings.Text ?? Console.ReadLine() ?? "";
        if (string.IsNullOrEmpty(message))
        {
            AnsiConsole.MarkupLine("[bold red]⚠ No message.[/] Use [cyan]--text <msg>[/] or pipe via stdin.");
            return 1;
        }

        var contextToken = contextTokenStore.GetContextToken(toUser);
        var result = await weChat.SendTextAsync(toUser, message, contextToken: contextToken);

        if (result.Ret == 0)
        {
            AnsiConsole.MarkupLine($"[bold green]✓ Message sent to {toUser}[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[bold red]✗ Failed:[/] {result.ErrMsg ?? "Unknown"} (ret={result.Ret})");
        if (result.Ret == -2)
        {
            AnsiConsole.MarkupLine("\n[yellow]Hint:[/] API needs a context_token from a prior inbound message.");
            AnsiConsole.MarkupLine("  Run [cyan]wechat-relay listen[/] and have the user message you first.");
        }
        return 1;
    }
}
