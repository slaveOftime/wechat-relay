using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using WeChatRelay.Serialization;
using WeChatRelay.Services;

namespace WeChatRelay.Commands;

public sealed class HistoryCommandSettings : VerboseCommandSettings
{
    public int Tail { get; init; } = 10;

    public bool Json { get; init; }
}

public static class HistoryCommand
{
    public static int Execute(HistoryCommandSettings settings)
    {
        if (settings.Tail <= 0)
        {
            AnsiConsole.MarkupLine("[bold red]⚠ Invalid tail count.[/] Pass a value greater than zero to [cyan]--tail[/].");
            return 1;
        }

        using var provider = Program.CreateServiceProvider(settings.Verbose);
        var historyStore = provider.GetRequiredService<IHistoryStore>();
        var entries = historyStore.ReadLatest(settings.Tail).ToList();

        if (settings.Json)
        {
            var jsonContext = new WeChatJsonContext(new JsonSerializerOptions(WeChatJsonContext.Default.Options)
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            var json = JsonSerializer.Serialize(entries, jsonContext.ListHistoryEntry);
            Console.WriteLine(json);
            return 0;
        }

        var isFirstEntryWritten = false;
        foreach (var entry in entries)
        {
            if (isFirstEntryWritten)
            {
                AnsiConsole.WriteLine();
            }

            AnsiConsole.MarkupLine(BuildHeader(entry));
            AnsiConsole.WriteLine(ResolveContent(entry));
            isFirstEntryWritten = true;
        }

        return 0;
    }

    private static string BuildHeader(HistoryEntry entry)
    {
        var isSent = entry.Direction == HistoryDirections.Sent;
        var icon = isSent ? "↗" : "↙";
        var direction = isSent ? "SENT" : "RECEIVED";
        var color = isSent ? "green" : "deepskyblue1";
        var type = ResolveMessageType(entry);

        return $"[grey]╭─[/] [{color} bold]{icon} {direction}[/] [grey]{Markup.Escape(entry.Time)}[/] [grey]·[/] [{color}]{Markup.Escape(type)}[/]";
    }

    private static string ResolveContent(HistoryEntry entry)
    {
        if (entry.Received is not null)
        {
            var text = MessageInspector.ExtractText(entry.Received);
            if (!string.IsNullOrWhiteSpace(text))
                return text;

            return MessageInspector.Describe(entry.Received);
        }

        if (!string.IsNullOrEmpty(entry.Content))
            return entry.Content;

        if (entry.Sent is not null)
            return entry.Sent.Kind switch
            {
                "text" => entry.Sent.Text ?? string.Empty,
                "image" => entry.Sent.FilePath ?? string.Empty,
                "audio" => entry.Sent.FilePath ?? string.Empty,
                _ => entry.Sent.Text ?? entry.Sent.FilePath ?? string.Empty
            };

        return string.Empty;
    }

    private static string ResolveMessageType(HistoryEntry entry)
    {
        if (entry.Sent is not null)
            return entry.Sent.Kind.ToUpperInvariant();

        if (entry.Received?.ItemList.Count > 0)
        {
            var kinds = entry.Received.ItemList
                .Select(item => item.Type switch
                {
                    1 => "TEXT",
                    2 => "IMAGE",
                    3 => "AUDIO",
                    4 => "FILE",
                    5 => "VIDEO",
                    _ => $"TYPE-{item.Type}"
                })
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return string.Join("+", kinds);
        }

        if (entry.Received?.MessageType is { } messageType)
            return $"MSG-{messageType}";

        return "UNKNOWN";
    }
}
