using System.Text.Json;
using Microsoft.Extensions.Logging;
using WeChatRelay.Models;

namespace WeChatRelay.Services;

public interface ISessionCache
{
    WeChatConfig? Load();
    void Save(WeChatConfig config);
    void Clear();
    string? GetContextToken(string userId);
    void SetContextToken(string userId, string contextToken);
}

public class CachedSession
{
    public string? BotToken { get; init; }
    public string? BotId { get; init; }
    public string? UserId { get; init; }
    public string? BaseUrl { get; init; }
    public string BotType { get; init; } = "3";
    public string? ToUsers { get; init; }
    public DateTimeOffset SavedAt { get; init; }
    public Dictionary<string, string> ContextTokens { get; init; } = new();
}

/// <summary>
/// Persistent session cache — credentials survive restarts indefinitely.
/// Only invalidated when the server-side session actually expires (detected via API error).
/// </summary>
public class SessionCache(ILogger<SessionCache> log, string? cachePath = null) : ISessionCache
{
    private readonly string _path = cachePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "wechat-relay", "session.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public WeChatConfig? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var s = JsonSerializer.Deserialize<CachedSession>(File.ReadAllText(_path));
            if (s is not { BotToken: not null }) return null;

            // No TTL — credentials are kept until they actually expire (API will reject them)
            return new WeChatConfig { BotToken = s.BotToken, BotId = s.BotId, UserId = s.UserId, BaseUrl = s.BaseUrl, BotType = s.BotType, ToUsers = s.ToUsers };
        }
        catch (Exception ex) { log.LogWarning(ex, "Failed to load session"); return null; }
    }

    public void Save(WeChatConfig c)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var existing = ReadCache();
            var session = new CachedSession
            {
                BotToken = c.BotToken, BotId = c.BotId, UserId = c.UserId, BaseUrl = c.BaseUrl,
                BotType = c.BotType, ToUsers = c.ToUsers,
                SavedAt = DateTimeOffset.UtcNow,
                ContextTokens = existing?.ContextTokens ?? new Dictionary<string, string>()
            };
            File.WriteAllText(_path, JsonSerializer.Serialize(session, JsonOpts));
            log.LogInformation("Session saved to {Path}", _path);
        }
        catch (Exception ex) { log.LogError(ex, "Failed to save session"); }
    }

    public void Clear()
    {
        try { if (File.Exists(_path)) { File.Delete(_path); log.LogInformation("Session cleared"); } }
        catch (Exception ex) { log.LogWarning(ex, "Failed to clear session"); }
    }

    public string? GetContextToken(string userId)
    {
        try { return ReadCache()?.ContextTokens.GetValueOrDefault(userId); }
        catch { return null; }
    }

    public void SetContextToken(string userId, string token)
    {
        try
        {
            var s = ReadCache();
            if (s is null) { log.LogDebug("No session to attach context token to"); return; }
            s.ContextTokens[userId] = token;
            File.WriteAllText(_path, JsonSerializer.Serialize(s, JsonOpts));
        }
        catch (Exception ex) { log.LogWarning(ex, "Failed to save context token"); }
    }

    private CachedSession? ReadCache() => File.Exists(_path) ? JsonSerializer.Deserialize<CachedSession>(File.ReadAllText(_path)) : null;
}
