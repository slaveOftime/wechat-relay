using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using WeChatRelay.Services;

namespace WeChatRelay.Commands;

public sealed class ListenCommandSettings : VerboseCommandSettings
{
    public string? HookCommand { get; init; }
}

public static class ListenCommand
{
    public static async Task<int> ExecuteAsync(ListenCommandSettings settings, CancellationToken cancellationToken)
    {
        if (settings.HookCommand is not null && string.IsNullOrWhiteSpace(settings.HookCommand))
        {
            AnsiConsole.MarkupLine("[bold red]⚠ Invalid hook command.[/] Pass a non-empty value to [cyan]--hook[/].");
            return 1;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

        using var provider = Program.CreateServiceProvider(settings.Verbose, settings.HookCommand);

        var weChat = provider.GetRequiredService<IWeChatService>();
        var hookRunner = provider.GetRequiredService<IHookRunner>();
        var contextTokenStore = provider.GetRequiredService<IContextTokenStore>();

        return await ExecuteCoreAsync(weChat, hookRunner, contextTokenStore, linkedCts.Token);
    }

    private static async Task<int> ExecuteCoreAsync(
        IWeChatService weChat,
        IHookRunner hookRunner,
        IContextTokenStore contextTokenStore,
        CancellationToken ct)
    {
        if (!weChat.IsLoggedIn)
        {
            AnsiConsole.MarkupLine("[bold red]⚠ Not logged in.[/] Run [cyan]wechat-relay login[/] first.");
            return 1;
        }

        AnsiConsole.Write(new FigletText("Listening")
            .LeftJustified()
            .Color(Color.Green));

        AnsiConsole.MarkupLine("\n[bold yellow]Bridge WeChat messages to any webhook[/]");
        AnsiConsole.MarkupLine("[grey]Press Ctrl+C to stop.\n[/]");

        try
        {
            var hookTask = hookRunner.ProcessLoopAsync(ct);
            await weChat.StartReceivingAsync(async msg =>
            {
                var time = msg.CreateTimeMs.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(msg.CreateTimeMs.Value).LocalDateTime.ToString("HH:mm:ss")
                    : "??:??:??";
                var seq = msg.Seq?.ToString() ?? "?";
                var fromUserId = msg.FromUserId ?? "<unknown>";
                var messageType = msg.MessageType?.ToString() ?? "?";
                var summary = MessageInspector.Describe(msg);
                var suffix = string.IsNullOrWhiteSpace(summary) ? string.Empty : $" {Markup.Escape(summary)}";

                AnsiConsole.MarkupLine(
                    $"[grey][[{Markup.Escape(time)}]][/] [bold]{Markup.Escape(seq)}[/] [blue]{Markup.Escape(fromUserId)}[/] [grey]type={Markup.Escape(messageType)}[/]{suffix}");

                if (!string.IsNullOrEmpty(msg.FromUserId) && !string.IsNullOrEmpty(msg.ContextToken))
                    contextTokenStore.SetContextToken(msg.FromUserId, msg.ContextToken);

                hookRunner.Enqueue(msg);
            }, ct);

            await hookTask;
        }
        catch (OperationCanceledException) { }

        AnsiConsole.MarkupLine("\n[yellow]Stopped.[/]");
        return 0;
    }
}
