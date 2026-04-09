using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WeChatRelay.Models;
using WeChatRelay.Serialization;

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

public class HookRunner(HookConfig hookCfg, IInboundMediaStore inboundMediaStore, ILogger<HookRunner> log) : IHookRunner
{
    private readonly ConcurrentQueue<InboundMessage> _queue = new();
    private readonly string _queueFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "wechat-relay", "pending-messages.jsonl");

    public void Enqueue(InboundMessage msg)
    {
        _queue.Enqueue(msg);
        AppendToFile(msg);
        log.LogDebug("Hook enqueued: seq={Seq}", msg.Seq);
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
            File.AppendAllText(_queueFile, JsonSerializer.Serialize(msg, WeChatJsonContext.Default.InboundMessage) + Environment.NewLine);
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
                var msg = JsonSerializer.Deserialize(line.Trim(), WeChatJsonContext.Default.InboundMessage);
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
        var items = await inboundMediaStore.BuildHookItemsAsync(msg, ct);

        // Build the hook payload JSON
        var json = JsonSerializer.Serialize(new HookPayload
        {
            Seq = msg.Seq,
            MessageId = msg.MessageId,
            FromUserId = msg.FromUserId,
            ToUserId = msg.ToUserId,
            CreateTimeMs = msg.CreateTimeMs,
            SessionId = msg.SessionId,
            MessageType = msg.MessageType,
            Text = MessageInspector.ExtractText(msg),
            Summary = MessageInspector.Describe(msg),
            Items = items,
            ContextToken = msg.ContextToken
        }, WeChatJsonContext.Default.HookPayload);
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
            log.LogInformation("Hook completed: seq={Seq}", msg.Seq);
        else
            log.LogWarning("Hook exited {Code} for seq={Seq}: {Error}", process.ExitCode, msg.Seq, error.Trim());
    }
}
