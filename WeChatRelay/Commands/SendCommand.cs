using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using WeChatRelay.Services;

namespace WeChatRelay.Commands;

public class SendCommand : AsyncCommand<SendCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "[target]")]
        public string? Target { get; init; }

        [CommandOption("--text")]
        public string? Text { get; init; }

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
        if (cache.Load() is not { BotToken: not null })
        {
            AnsiConsole.MarkupLine("[bold red]⚠ Not logged in.[/] Run [cyan]wechat-relay login[/] first.");
            return 1;
        }

        var toUser = string.IsNullOrEmpty(settings.Target) ? cache.Load()?.UserId : settings.Target;

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

        var contextToken = cache.GetContextToken(toUser);
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
