using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using WeChatRelay.Commands;
using WeChatRelay.Models;
using WeChatRelay.Services;

namespace WeChatRelay;

public static class Program
{
    public static int Main(string[] args)
    {
        var app = new CommandApp<DefaultCommand>();

        app.Configure(config =>
        {
            config.SetApplicationName("wechat-relay");
            config.SetApplicationVersion(typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

            config.AddCommand<LoginCommand>("login")
                .WithDescription("QR code login (credentials cached)")
                .WithExample("wechat-relay login")
                .WithExample("wechat-relay login --force");

            config.AddCommand<ListenCommand>("listen")
                .WithDescription("Run listener until Ctrl+C")
                .WithExample("wechat-relay listen");

            config.AddCommand<ListSendToCommand>("list-send-to")
                .WithDescription("Show available send-to candidates")
                .WithExample("wechat-relay list-send-to");

            config.AddCommand<SendCommand>("send")
                .WithDescription("Send a text message")
                .WithExample("wechat-relay send --text \"Hello!\"")
                .WithExample("wechat-relay send user@im.wechat --text \"Hi\"");
        });

        return app.Run(args);
    }

    public static void ConfigureServices(IServiceCollection services, bool verbose = false)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .Build();

        services.AddSingleton<IConfiguration>(config);

        // Configure console logging
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Warning);
        });

        services.AddSingleton<WeChatConfig>(sp =>
        {
            var file = config.GetSection("WeChat").Get<WeChatConfig>() ?? new WeChatConfig();
            var cache = sp.GetRequiredService<ISessionCache>();
            var cached = cache.Load();
            return cached is { BotToken: not null }
                ? new WeChatConfig { BotToken = cached.BotToken, BotId = cached.BotId, UserId = cached.UserId ?? file.UserId, BaseUrl = cached.BaseUrl ?? file.BaseUrl, BotType = cached.BotType, ToUsers = cached.ToUsers ?? file.ToUsers }
                : file;
        });

        services.AddSingleton<ISessionCache, SessionCache>();
        services.AddHttpClient<IWeChatService, WeChatService>();

        var hookCfg = config.GetSection("Hook").Get<HookConfig>() ?? new HookConfig();
        services.AddSingleton(hookCfg);
        services.AddSingleton<IHookRunner, HookRunner>();
    }
}

public class DefaultCommand : AsyncCommand<DefaultCommand.Settings>
{
    public class Settings : CommandSettings
    {
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        AnsiConsole.Write(new FigletText("wechat-relay")
            .LeftJustified()
            .Color(Color.Cyan1));

        AnsiConsole.MarkupLine("\n[bold yellow]Bridge WeChat messages to any webhook[/]");
        AnsiConsole.MarkupLine("[grey]https://github.com/slaveoftime/wechat-relay[/]\n");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[underline]Commands:[/]");
        AnsiConsole.MarkupLine("  [green]login[/]              QR code login (credentials cached)");
        AnsiConsole.MarkupLine("  [green]listen[/]             Run listener until Ctrl+C");
        AnsiConsole.MarkupLine("  [green]list-send-to[/]       Show available send-to candidates");
        AnsiConsole.MarkupLine("  [green]send[/] <target>      Send a text message");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[underline]Examples:[/]");
        AnsiConsole.MarkupLine("  [grey]wechat-relay login[/]");
        AnsiConsole.MarkupLine("  [grey]wechat-relay listen[/]");
        AnsiConsole.MarkupLine("  [grey]wechat-relay send --text \"Hello!\"[/]");
        AnsiConsole.MarkupLine("  [grey]wechat-relay send user@im.wechat --text \"Hi\"[/]");

        return await Task.FromResult(1);
    }
}
