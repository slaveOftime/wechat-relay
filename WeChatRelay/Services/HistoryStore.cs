using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WeChatRelay.Models;
using WeChatRelay.Serialization;

namespace WeChatRelay.Services;

public sealed class HistoryOptions
{
    public string? DirectoryPath { get; init; }
}

public sealed class HistoryEntry
{
    public string Time { get; init; } = string.Empty;
    public string Direction { get; init; } = HistoryDirections.Received;
    public string Content { get; init; } = string.Empty;
    public InboundMessage? Received { get; init; }
    public SentHistoryData? Sent { get; init; }
    public string? HookProcessedAt { get; init; }
}

public static class HistoryDirections
{
    public const string Received = "received";
    public const string Sent = "sent";
}

public interface IHistoryStore
{
    Task<bool> SaveReceivedAsync(InboundMessage message, CancellationToken ct = default);
    Task SaveSentAsync(SentHistoryData message, CancellationToken ct = default);
    IReadOnlyList<HistoryEntry> ReadLatest(int count);
    IReadOnlyList<InboundMessage> ReadPendingHookMessages();
    Task MarkHookProcessedAsync(InboundMessage message, CancellationToken ct = default);
}

public sealed class HistoryStore(HistoryOptions options, ILogger<HistoryStore> log) : IHistoryStore
{
    private const string Extension = ".json";

    public async Task<bool> SaveReceivedAsync(InboundMessage message, CancellationToken ct = default)
    {
        if (ReceivedMessageExists(message))
            return false;

        return await SaveAsync(new HistoryEntry
        {
            Time = CreateTimestamp(),
            Direction = HistoryDirections.Received,
            Content = MessageInspector.Describe(message),
            Received = message
        }, ct);
    }

    public async Task SaveSentAsync(SentHistoryData message, CancellationToken ct = default)
    {
        await SaveAsync(new HistoryEntry
        {
            Time = CreateTimestamp(),
            Direction = HistoryDirections.Sent,
            Content = BuildSentContent(message),
            Sent = message
        }, ct);
    }

    public IReadOnlyList<HistoryEntry> ReadLatest(int count)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "History count must be greater than zero.");

        var directory = ResolveDirectory();
        if (!Directory.Exists(directory))
            return [];

        return EnumerateHistoryFiles(directory)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Take(count)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Select(ReadEntry)
            .ToList();
    }

    public IReadOnlyList<InboundMessage> ReadPendingHookMessages()
    {
        var directory = ResolveDirectory();
        if (!Directory.Exists(directory))
            return [];

        var messages = new List<InboundMessage>();
        foreach (var path in EnumerateHistoryFiles(directory)
                     .OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            try
            {
                var entry = ReadEntry(path);
                if (entry.Direction == HistoryDirections.Received &&
                    entry.Received is not null &&
                    string.IsNullOrWhiteSpace(entry.HookProcessedAt))
                {
                    messages.Add(entry.Received);
                }
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Failed to inspect pending hook history entry from {Path}.", path);
            }
        }

        return messages;
    }

    public async Task MarkHookProcessedAsync(InboundMessage message, CancellationToken ct = default)
    {
        var directory = ResolveDirectory();
        if (!Directory.Exists(directory))
            return;

        foreach (var path in EnumerateHistoryFiles(directory)
                     .OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var entry = ReadEntry(path);
            if (entry.Direction != HistoryDirections.Received ||
                entry.Received is null ||
                !MatchesReceivedMessage(entry.Received, message))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(entry.HookProcessedAt))
                return;

            var processedEntry = new HistoryEntry
            {
                Time = entry.Time,
                Direction = entry.Direction,
                Content = entry.Content,
                Received = entry.Received,
                Sent = entry.Sent,
                HookProcessedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };
            var json = JsonSerializer.Serialize(processedEntry, WeChatJsonContext.Default.HistoryEntry);
            await File.WriteAllTextAsync(path, json, Encoding.UTF8, ct);
            return;
        }
    }

    private async Task<bool> SaveAsync(HistoryEntry entry, CancellationToken ct)
    {
        try
        {
            var directory = ResolveDirectory();
            Directory.CreateDirectory(directory);

            var path = CreateHistoryFilePath(directory, entry);
            var json = JsonSerializer.Serialize(entry, WeChatJsonContext.Default.HistoryEntry);
            await File.WriteAllTextAsync(path, json, Encoding.UTF8, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Failed to write {Direction} message history.", entry.Direction);
            return false;
        }
    }

    private HistoryEntry ReadEntry(string path)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        var entry = JsonSerializer.Deserialize(json, WeChatJsonContext.Default.HistoryEntry)
            ?? throw new JsonException($"History entry file '{path}' is empty.");
        var content = entry.Content ?? string.Empty;

        return string.IsNullOrWhiteSpace(entry.Time)
            ? new HistoryEntry
            {
                Time = GetTimestampFromPath(path),
                Direction = entry.Direction,
                Content = content,
                Received = entry.Received,
                Sent = entry.Sent,
                HookProcessedAt = entry.HookProcessedAt
            }
            : new HistoryEntry
            {
                Time = entry.Time,
                Direction = entry.Direction,
                Content = content,
                Received = entry.Received,
                Sent = entry.Sent,
                HookProcessedAt = entry.HookProcessedAt
            };
    }

    private bool ReceivedMessageExists(InboundMessage message)
    {
        if (message.Seq is null && message.MessageId is null)
            return false;

        var directory = ResolveDirectory();
        if (!Directory.Exists(directory))
            return false;

        foreach (var path in EnumerateHistoryFiles(directory))
        {
            try
            {
                var entry = ReadEntry(path);
                if (entry.Direction == HistoryDirections.Received &&
                    entry.Received is not null &&
                    MatchesReceivedMessage(entry.Received, message))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Failed to inspect existing history entry from {Path}.", path);
            }
        }

        return false;
    }

    private string ResolveDirectory()
    {
        return AppPaths.ResolveRootedPath(options.DirectoryPath);
    }

    private static IEnumerable<string> EnumerateHistoryFiles(string directory) =>
        Directory.EnumerateFiles(directory, $"*{Extension}", SearchOption.TopDirectoryOnly)
            .Where(IsHistoryFile);

    private static bool IsHistoryFile(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var sentSuffix = $".{HistoryDirections.Sent}";
        if (fileName.EndsWith(sentSuffix, StringComparison.Ordinal))
        {
            fileName = fileName[..^sentSuffix.Length];
        }

        return DateTime.TryParseExact(
            fileName,
            "yyyy-MM-dd HH-mm-fff",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);
    }

    private static string CreateHistoryFilePath(string directory, HistoryEntry entry)
    {
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var timestamp = attempt == 0 ? entry.Time : CreateTimestamp();
            var suffix = entry.Direction == HistoryDirections.Sent ? $".{HistoryDirections.Sent}{Extension}" : Extension;
            var path = Path.Combine(directory, $"{timestamp}{suffix}");

            try
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                return path;
            }
            catch (IOException) when (File.Exists(path))
            {
                Thread.Sleep(1);
            }
        }

        throw new IOException("Could not create a unique history filename.");
    }

    private static string BuildSentContent(SentHistoryData message) =>
        message.Kind switch
        {
            "text" => message.Text ?? string.Empty,
            "image" => message.FilePath ?? string.Empty,
            "audio" => message.FilePath ?? string.Empty,
            _ => message.Text ?? message.FilePath ?? string.Empty
        };

    private static string CreateTimestamp() =>
        DateTime.Now.ToString("yyyy-MM-dd HH-mm-fff", CultureInfo.InvariantCulture);

    private static string GetTimestampFromPath(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var sentSuffix = $".{HistoryDirections.Sent}";
        return fileName.EndsWith(sentSuffix, StringComparison.Ordinal)
            ? fileName[..^sentSuffix.Length]
            : fileName;
    }

    private static bool MatchesReceivedMessage(InboundMessage left, InboundMessage right)
    {
        if (left.Seq is { } leftSeq && right.Seq is { } rightSeq && leftSeq != rightSeq)
            return false;

        if (left.MessageId is { } leftMessageId && right.MessageId is { } rightMessageId && leftMessageId != rightMessageId)
            return false;

        return left.Seq.HasValue && right.Seq.HasValue ||
               left.MessageId.HasValue && right.MessageId.HasValue;
    }
}

public sealed class SentHistoryData
{
    public string ToUserId { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string? Text { get; init; }
    public string? FilePath { get; init; }
    public string? AudioFormat { get; init; }
    public int? AudioSampleRate { get; init; }
    public int? AudioBitsPerSample { get; init; }
    public int? AudioPlaytimeMs { get; init; }
}
