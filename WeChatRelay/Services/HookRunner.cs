using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WeChatRelay.Models;

namespace WeChatRelay.Services;

/// <summary>
/// Runs the hook command for each incoming message without blocking the listener.
/// Uses a persistent queue (SQLite-like file) to survive restarts.
/// </summary>
public interface IHookRunner
{
    void Enqueue(InboundMessage msg);
    Task ProcessLoopAsync(CancellationToken ct);
}

public class HookConfig
{
    public string Command { get; init; } = "echo";
    public string? WorkingDirectory { get; init; }
}

public class HookRunner(HookConfig hookCfg, ILogger<HookRunner> log) : IHookRunner
{
    private readonly ConcurrentQueue<InboundMessage> _queue = new();
    private readonly string _queueFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "wechat-relay", "pending-messages.jsonl");

    public void Enqueue(InboundMessage msg)
    {
        _queue.Enqueue(msg);
        AppendToFile(msg);
        log.LogInformation("Hook enqueued: seq={Seq} from={From}", msg.Seq, msg.FromUserId);
    }

    public async Task ProcessLoopAsync(CancellationToken ct)
    {
        DrainPersisted();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_queue.TryDequeue(out var msg))
                {
                    try { await InvokeHookAsync(msg, ct); }
                    catch (Exception ex) { log.LogError(ex, "Hook failed for seq={Seq}", msg.Seq); }
                }
                else
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { log.LogError(ex, "ProcessLoop error"); }
        }
    }

    private void AppendToFile(InboundMessage msg)
    {
        try
        {
            var dir = Path.GetDirectoryName(_queueFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(_queueFile, JsonSerializer.Serialize(msg) + Environment.NewLine);
        }
        catch (Exception ex) { log.LogWarning(ex, "Failed to persist message to queue"); }
    }

    private void DrainPersisted()
    {
        if (!File.Exists(_queueFile)) return;
        try
        {
            foreach (var line in File.ReadLines(_queueFile))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var msg = JsonSerializer.Deserialize<InboundMessage>(line.Trim());
                if (msg is not null) _queue.Enqueue(msg);
            }
            // Clear the file after draining
            File.WriteAllText(_queueFile, "");
            log.LogInformation("Drained {Count} persisted messages from queue", _queue.Count);
        }
        catch (Exception ex) { log.LogWarning(ex, "Failed to drain persisted queue"); }
    }

    private async Task InvokeHookAsync(InboundMessage msg, CancellationToken ct)
    {
        // Build the hook payload JSON
        var payload = new
        {
            seq = msg.Seq,
            message_id = msg.MessageId,
            from_user_id = msg.FromUserId,
            to_user_id = msg.ToUserId,
            create_time_ms = msg.CreateTimeMs,
            session_id = msg.SessionId,
            message_type = msg.MessageType,
            text = ExtractText(msg),
            context_token = msg.ContextToken
        };

        var json = JsonSerializer.Serialize(payload);
        var escaped = json.Replace("\"", "\\\"");

        var args = hookCfg.Command;

        // If the command contains a placeholder for the payload, replace it
        if (args.Contains("{payload}"))
            args = args.Replace("{payload}", json);
        else
            args = $"{args} \"{escaped}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd",
            Arguments = $"/c {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrEmpty(hookCfg.WorkingDirectory))
            startInfo.WorkingDirectory = hookCfg.WorkingDirectory;

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start hook process");
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode == 0)
            log.LogDebug("Hook completed for seq={Seq}: {Output}", msg.Seq, output.Trim());
        else
            log.LogWarning("Hook exited {Code} for seq={Seq}: {Error}", process.ExitCode, msg.Seq, error.Trim());
    }

    private static string ExtractText(InboundMessage msg)
    {
        var texts = msg.ItemList.Where(i => i.Type == 1 && i.TextItem != null).Select(i => i.TextItem!.Text);
        return string.Join(" ", texts);
    }
}
