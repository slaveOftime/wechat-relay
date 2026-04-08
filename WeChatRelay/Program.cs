using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeChatRelay.Commands;
using WeChatRelay.Models;
using WeChatRelay.Services;

namespace WeChatRelay;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var commandLineArgs = args.Length == 0 ? ["--help"] : args;
        return BuildCommandLine().Parse(commandLineArgs).Invoke();
    }

    public static void ConfigureServices(IServiceCollection services, bool verbose = false)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IOptions<WeChatOptions>>(Options.Create(BuildWeChatOptions(config.GetSection("WeChat"))));

        // Configure console logging
        services.AddLogging(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Warning);
        });

        services.AddSingleton<WeChatConfig>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<WeChatOptions>>().Value;
            var session = sp.GetRequiredService<ILoginSessionStore>().Load();
            return WeChatConfig.Create(options, session);
        });

        services.AddSingleton<SessionStore>();
        services.AddSingleton<ILoginSessionStore>(sp => sp.GetRequiredService<SessionStore>());
        services.AddSingleton<IContextTokenStore>(sp => sp.GetRequiredService<SessionStore>());
        services.AddHttpClient<IWeChatService, WeChatService>();

        services.AddSingleton(BuildHookConfig(config.GetSection("Hook")));
        services.AddSingleton<IHookRunner, HookRunner>();
    }

    public static ServiceProvider CreateServiceProvider(bool verbose = false)
    {
        var services = new ServiceCollection();
        ConfigureServices(services, verbose);
        return services.BuildServiceProvider();
    }

    private static WeChatOptions BuildWeChatOptions(IConfigurationSection section) =>
        new()
        {
            BaseUrl = section[nameof(WeChatOptions.BaseUrl)] ?? "https://ilinkai.weixin.qq.com/",
            BotType = section[nameof(WeChatOptions.BotType)] ?? "3",
            UserId = section[nameof(WeChatOptions.UserId)],
            ToUsers = section[nameof(WeChatOptions.ToUsers)]
        };

    private static HookConfig BuildHookConfig(IConfigurationSection section) =>
        new()
        {
            Command = section[nameof(HookConfig.Command)] ?? "echo",
            WorkingDirectory = section[nameof(HookConfig.WorkingDirectory)]
        };

    private static RootCommand BuildCommandLine()
    {
        var rootCommand = new RootCommand("Bridge WeChat messages to any webhook");

        var forceOption = new Option<bool>("--force")
        {
            Description = "Login with a new account and clear stored session state"
        };
        forceOption.Aliases.Add("-f");
        var loginVerboseOption = CreateVerboseOption();
        var loginCommand = new Command("login", "QR code login (stored locally)")
        {
            forceOption,
            loginVerboseOption
        };
        loginCommand.SetAction(parseResult =>
            LoginCommand.ExecuteAsync(new LoginCommandSettings
            {
                Force = parseResult.GetValue(forceOption),
                Verbose = parseResult.GetValue(loginVerboseOption)
            }, CancellationToken.None).GetAwaiter().GetResult());

        var listenVerboseOption = CreateVerboseOption();
        var listenCommand = new Command("listen", "Run listener until Ctrl+C");
        listenCommand.Options.Add(listenVerboseOption);
        listenCommand.SetAction(parseResult =>
            ListenCommand.ExecuteAsync(new ListenCommandSettings
            {
                Verbose = parseResult.GetValue(listenVerboseOption)
            }, CancellationToken.None).GetAwaiter().GetResult());

        var listSendToVerboseOption = CreateVerboseOption();
        var listSendToCommand = new Command("list-send-to", "Show available send-to candidates");
        listSendToCommand.Options.Add(listSendToVerboseOption);
        listSendToCommand.SetAction(parseResult =>
            ListSendToCommand.Execute(new ListSendToCommandSettings
            {
                Verbose = parseResult.GetValue(listSendToVerboseOption)
            }));

        var targetArgument = new Argument<string?>("target")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Optional WeChat user ID. Defaults to configured UserId"
        };
        var textOption = new Option<string?>("--text")
        {
            Description = "Message text. If omitted, reads from stdin"
        };
        var sendVerboseOption = CreateVerboseOption();
        var sendCommand = new Command("send", "Send a text message")
        {
            targetArgument,
            textOption,
            sendVerboseOption
        };
        sendCommand.SetAction(parseResult =>
            SendCommand.ExecuteAsync(new SendCommandSettings
            {
                Target = parseResult.GetValue(targetArgument),
                Text = parseResult.GetValue(textOption),
                Verbose = parseResult.GetValue(sendVerboseOption)
            }, CancellationToken.None).GetAwaiter().GetResult());

        rootCommand.Subcommands.Add(loginCommand);
        rootCommand.Subcommands.Add(listenCommand);
        rootCommand.Subcommands.Add(listSendToCommand);
        rootCommand.Subcommands.Add(sendCommand);

        return rootCommand;
    }

    private static Option<bool> CreateVerboseOption()
    {
        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Enable debug logging"
        };
        verboseOption.Aliases.Add("-v");
        return verboseOption;
    }
}

public class VerboseCommandSettings
{
    public bool Verbose { get; init; }
}
