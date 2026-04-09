using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using WeChatRelay.Models;
using WeChatRelay.Services;

namespace WeChatRelay.Commands;

public sealed class SendCommandSettings : VerboseCommandSettings
{
    public string? Target { get; init; }

    public string? Text { get; init; }

    public string? ImagePath { get; init; }

    public string? AudioPath { get; init; }

    public string? AudioFormat { get; init; }

    public int? AudioSampleRate { get; init; }

    public int? AudioBitsPerSample { get; init; }

    public int? AudioPlaytimeMs { get; init; }
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

        var selectedInputCount =
            (string.IsNullOrWhiteSpace(settings.Text) ? 0 : 1) +
            (string.IsNullOrWhiteSpace(settings.ImagePath) ? 0 : 1) +
            (string.IsNullOrWhiteSpace(settings.AudioPath) ? 0 : 1);
        if (selectedInputCount > 1)
        {
            AnsiConsole.MarkupLine("[bold red]⚠ Pick one payload type.[/] Use only one of [cyan]--text[/], [cyan]--image[/], or [cyan]--audio[/].");
            return 1;
        }

        var contextToken = contextTokenStore.GetContextToken(toUser);
        SendMessageResponse result;
        var sentLabel = "Message";

        if (!string.IsNullOrWhiteSpace(settings.ImagePath))
        {
            if (!File.Exists(settings.ImagePath))
            {
                AnsiConsole.MarkupLine($"[bold red]⚠ Image file not found:[/] [grey]{Markup.Escape(settings.ImagePath)}[/]");
                return 1;
            }

            result = await weChat.SendImageAsync(toUser, settings.ImagePath, contextToken: contextToken);
            sentLabel = "Image";
        }
        else if (!string.IsNullOrWhiteSpace(settings.AudioPath))
        {
            if (!File.Exists(settings.AudioPath))
            {
                AnsiConsole.MarkupLine($"[bold red]⚠ Audio file not found:[/] [grey]{Markup.Escape(settings.AudioPath)}[/]");
                return 1;
            }

            var encodeType = ResolveAudioEncodeType(settings.AudioPath, settings.AudioFormat, out var encodeTypeError);
            if (encodeTypeError is not null)
            {
                AnsiConsole.MarkupLine($"[bold red]⚠ Invalid audio format:[/] {Markup.Escape(encodeTypeError)}");
                return 1;
            }

            result = await weChat.SendAudioAsync(toUser, settings.AudioPath, new AudioSendOptions
            {
                EncodeType = encodeType,
                SampleRate = settings.AudioSampleRate,
                BitsPerSample = settings.AudioBitsPerSample,
                PlaytimeMs = settings.AudioPlaytimeMs
            }, contextToken: contextToken);
            sentLabel = "Audio";
        }
        else
        {
            var message = settings.Text ?? Console.ReadLine() ?? "";
            if (string.IsNullOrEmpty(message))
            {
                AnsiConsole.MarkupLine("[bold red]⚠ No message.[/] Use [cyan]--text <msg>[/], [cyan]--image <path>[/], [cyan]--audio <path>[/], or pipe text via stdin.");
                return 1;
            }

            result = await weChat.SendTextAsync(toUser, message, contextToken: contextToken);
        }

        if (result.Ret == 0)
        {
            AnsiConsole.MarkupLine($"[bold green]✓ {sentLabel} sent to {toUser}[/]");
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

    private static int? ResolveAudioEncodeType(string audioPath, string? audioFormat, out string? error)
    {
        error = null;

        var format = audioFormat;
        if (string.IsNullOrWhiteSpace(format))
        {
            var extension = Path.GetExtension(audioPath);
            format = string.IsNullOrWhiteSpace(extension) ? null : extension.TrimStart('.');
        }

        if (string.IsNullOrWhiteSpace(format))
        {
            return null;
        }

        return format.Trim().ToLowerInvariant() switch
        {
            "pcm" => 1,
            "wav" => 1,
            "adpcm" => 2,
            "feature" => 3,
            "speex" => 4,
            "spx" => 4,
            "amr" => 5,
            "sil" => 6,
            "silk" => 6,
            "mp3" => 7,
            "ogg" => 8,
            "ogg-speex" => 8,
            _ => SetError($"Unsupported format '{format}'. Use pcm, wav, adpcm, feature, speex, amr, silk, mp3, or ogg.", out error)
        };
    }

    private static int? SetError(string message, out string? error)
    {
        error = message;
        return null;
    }
}
