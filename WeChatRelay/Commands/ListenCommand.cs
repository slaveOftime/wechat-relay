using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using WeChatRelay.Services;

namespace WeChatRelay.Commands;

public class ListenCommand : AsyncCommand<ListenCommand.Settings>
{
    public class Settings : CommandSettings
    {
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
        var hookRunner = provider.GetRequiredService<IHookRunner>();
        var cache = provider.GetRequiredService<ISessionCache>();

        return await ExecuteAsync(weChat, hookRunner, cache, cts.Token);
    }

    private static async Task<int> ExecuteAsync(IWeChatService weChat, IHookRunner hookRunner, ISessionCache cache, CancellationToken ct)
    {
        if (cache.Load() is not { BotToken: not null })
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

                AnsiConsole.MarkupLine($"[grey][{time}][/] [bold]{msg.Seq}[/] [blue]{msg.FromUserId}[/] [grey]type={msg.MessageType}[/]");

                if (!string.IsNullOrEmpty(msg.FromUserId) && !string.IsNullOrEmpty(msg.ContextToken))
                    cache.SetContextToken(msg.FromUserId, msg.ContextToken);

                hookRunner.Enqueue(msg);
            }, ct);

            await hookTask;
        }
        catch (OperationCanceledException) { }

        AnsiConsole.MarkupLine("\n[yellow]Stopped.[/]");
        return 0;
    }
}
