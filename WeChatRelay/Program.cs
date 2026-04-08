using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WeChatRelay.Commands;
using WeChatRelay.Models;
using WeChatRelay.Services;

namespace WeChatRelay;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var mode = args.FirstOrDefault()?.ToLower();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var services = ConfigureServices();
        var provider = services.BuildServiceProvider();

        return mode switch
        {
            "login" => await LoginCommand.ExecuteAsync(
                provider.GetRequiredService<IWeChatService>(),
                provider.GetRequiredService<ISessionCache>(),
                args.Skip(1).ToArray(),
                cts.Token),

            "listen" => await ListenCommand.ExecuteAsync(
                provider.GetRequiredService<IWeChatService>(),
                provider.GetRequiredService<IHookRunner>(),
                provider.GetRequiredService<ISessionCache>(),
                cts.Token),

            "list-send-to" => ListSendToCommand.Execute(
                provider.GetRequiredService<IWeChatService>()),

            "send" => await SendCommand.ExecuteAsync(
                provider.GetRequiredService<IWeChatService>(),
                provider.GetRequiredService<ISessionCache>(),
                args.Skip(1).ToArray(),
                cts.Token),

            _ => PrintUsage()
        };
    }

    private static ServiceCollection ConfigureServices()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .Build();

        var verbose = config.GetValue("AppSettings:Verbose", false);

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(config);

        // Configure console logging
        services.AddLogging(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
            });
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
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

        return services;
    }

    private static int PrintUsage()
    {
        var version = typeof(Program).Assembly.GetName().Version;
        var versionStr = version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "v1.0.0";
        Console.WriteLine("╔═══════════════════════════════════════════════╗");
        Console.WriteLine($"║           wechat-relay  {versionStr,-22}║");
        Console.WriteLine("║  Bridge WeChat messages to any webhook        ║");
        Console.WriteLine("╚═══════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  login              QR code login (credentials cached)");
        Console.WriteLine("  login --force      Force new QR login");
        Console.WriteLine("  listen             Run listener until Ctrl+C");
        Console.WriteLine("  list-send-to       Show available send-to candidates");
        Console.WriteLine("  send [target]      Send a text message");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --text <msg>       Message text for send command");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  wechat-relay login");
        Console.WriteLine("  wechat-relay listen");
        Console.WriteLine("  wechat-relay send --text \"Hello!\"");
        Console.WriteLine("  wechat-relay send user@im.wechat --text \"Hi\"");
        return 1;
    }
}
