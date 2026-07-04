using System.Collections.Concurrent;
using System.Diagnostics;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WeChatRelay.Models;
using WeChatRelay.Serialization;

namespace WeChatRelay.Services;

/// <summary>
/// Runs the hook command for each incoming message without blocking the listener.
/// Uses unprocessed received history entries to survive restarts without duplicating message payloads.
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

public class HookRunner(
    HookConfig hookCfg,
    IInboundMediaStore inboundMediaStore,
    IHistoryStore historyStore,
    ILogger<HookRunner> log) : IHookRunner
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private readonly ConcurrentQueue<InboundMessage> _queue = new();

    public void Enqueue(InboundMessage msg)
    {
        _queue.Enqueue(msg);
        log.LogDebug("Hook enqueued: seq={Seq}", msg.Seq);
    }

    public async Task ProcessLoopAsync(CancellationToken ct)
    {
        EnqueuePendingFromHistory();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_queue.TryDequeue(out var msg))
                {
                    try
                    {
                        await InvokeHookAsync(msg, ct);
                        await historyStore.MarkHookProcessedAsync(msg, CancellationToken.None);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "Hook failed for seq={Seq}; will retry while listener is running.", msg.Seq);
                        await RequeueAfterDelayAsync(msg, ct);
                    }
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

    private void EnqueuePendingFromHistory()
    {
        try
        {
            var messages = historyStore.ReadPendingHookMessages();
            foreach (var msg in messages)
            {
                _queue.Enqueue(msg);
            }

            if (messages.Count > 0)
                log.LogInformation("Enqueued {Count} pending hook messages from history", messages.Count);
        }
        catch (Exception ex) { log.LogWarning(ex, "Failed to enqueue pending hook messages from history"); }
    }

    private async Task RequeueAfterDelayAsync(InboundMessage msg, CancellationToken ct)
    {
        await Task.Delay(RetryDelay, ct);
        _queue.Enqueue(msg);
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
        var commandParts = SplitCommand(hookCfg.Command);
        var startInfo = new ProcessStartInfo
        {
            FileName = commandParts[0],
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var placeholderExpanded = false;
        foreach (var part in commandParts.Skip(1))
        {
            if (part.Contains("{payload}", StringComparison.Ordinal))
            {
                startInfo.ArgumentList.Add(part.Replace("{payload}", json, StringComparison.Ordinal));
                placeholderExpanded = true;
            }
            else
            {
                startInfo.ArgumentList.Add(part);
            }
        }

        if (!placeholderExpanded)
            startInfo.ArgumentList.Add(json);

        if (!string.IsNullOrEmpty(hookCfg.WorkingDirectory))
            startInfo.WorkingDirectory = hookCfg.WorkingDirectory;

        using var process = StartHookProcess(startInfo, hookCfg.Command, json);

        string output;
        string error;
        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            output = await outputTask;
            error = await errorTask;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }

        if (!string.IsNullOrWhiteSpace(output))
            log.LogDebug("Hook stdout for seq={Seq}: {Output}", msg.Seq, output.Trim());

        if (process.ExitCode == 0)
        {
            log.LogInformation("Hook completed: seq={Seq}", msg.Seq);
            return;
        }

        var failure = string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim();
        throw new InvalidOperationException($"Hook exited {process.ExitCode} for seq={msg.Seq}: {failure}");
    }

    private static Process StartHookProcess(ProcessStartInfo startInfo, string command, string payload)
    {
        try
        {
            return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start hook process.");
        }
        catch (Win32Exception ex)
        {
            var shellStartInfo = CreateShellStartInfo(command, payload, startInfo.WorkingDirectory);
            try
            {
                return Process.Start(shellStartInfo) ?? throw new InvalidOperationException("Failed to start hook process.");
            }
            catch (Win32Exception shellEx)
            {
                throw new InvalidOperationException(
                    $"Failed to start hook process '{startInfo.FileName}' directly or via shell '{shellStartInfo.FileName}'.",
                    new AggregateException(ex, shellEx));
            }
        }
    }

    private static ProcessStartInfo CreateShellStartInfo(string command, string payload, string workingDirectory)
    {
        var payloadVariable = OperatingSystem.IsWindows()
            ? "\"%WECHAT_RELAY_PAYLOAD%\""
            : "\"$WECHAT_RELAY_PAYLOAD\"";
        var commandText = command.Contains("{payload}", StringComparison.Ordinal)
            ? command.Replace("{payload}", payloadVariable, StringComparison.Ordinal)
            : $"{command} {payloadVariable}";

        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows()
                ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"
                : "/bin/sh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(commandText);
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(commandText);
        }

        startInfo.Environment["WECHAT_RELAY_PAYLOAD"] = payload;

        if (!string.IsNullOrEmpty(workingDirectory))
            startInfo.WorkingDirectory = workingDirectory;

        return startInfo;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    private static IReadOnlyList<string> SplitCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new InvalidOperationException("Hook command cannot be empty.");

        var parts = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var backslashCount = 0;

        foreach (var ch in command)
        {
            if (ch == '\\')
            {
                backslashCount += 1;
                continue;
            }

            if (ch == '"')
            {
                if (backslashCount > 0)
                {
                    current.Append('\\', backslashCount / 2);
                    if (backslashCount % 2 == 1)
                    {
                        current.Append('"');
                        backslashCount = 0;
                        continue;
                    }
                }

                backslashCount = 0;
                inQuotes = !inQuotes;
                continue;
            }

            if (backslashCount > 0)
            {
                current.Append('\\', backslashCount);
                backslashCount = 0;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (backslashCount > 0)
            current.Append('\\', backslashCount);

        if (inQuotes)
            throw new InvalidOperationException($"Hook command has an unterminated quoted segment: {command}");

        if (current.Length > 0)
            parts.Add(current.ToString());

        if (parts.Count == 0)
            throw new InvalidOperationException("Hook command cannot be empty.");

        return parts;
    }
}
