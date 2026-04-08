using System.Text.Json;
using Microsoft.Extensions.Logging;
using WeChatRelay.Models;
using WeChatRelay.Serialization;

namespace WeChatRelay.Services;

public interface ILoginSessionStore
{
    WeChatLoginSession? Load();
    void Save(WeChatLoginSession session);
    void Clear();
}

public interface IContextTokenStore
{
    string? GetContextToken(string userId);
    void SetContextToken(string userId, string contextToken);
    void Clear();
}

internal sealed record StoredSessionState
{
    public WeChatLoginSession? LoginSession { get; init; }
    public Dictionary<string, string> ContextTokens { get; init; } = new(StringComparer.Ordinal);
}

public sealed class SessionStore(
    ILogger<SessionStore> log,
    string? rootPath = null) : ILoginSessionStore, IContextTokenStore
{
    private readonly string _rootPath = rootPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "wechat-relay");

    private string SessionStatePath => Path.Combine(_rootPath, "session-state.json");

    public WeChatLoginSession? Load() => ReadState().LoginSession;

    public void Save(WeChatLoginSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        WriteState(ReadState() with
        {
            LoginSession = session
        });
    }

    public void Clear()
    {
        try
        {
            var state = ReadState();
            if (state.LoginSession is null)
            {
                DeleteStateFile();
                return;
            }

            WriteState(state with
            {
                ContextTokens = new Dictionary<string, string>(StringComparer.Ordinal)
            });
        }
        catch (InvalidOperationException ex)
        {
            log.LogWarning(ex, "Failed to clear stored context tokens");
        }
    }

    void ILoginSessionStore.Clear()
    {
        try
        {
            var state = ReadState();
            if (state.ContextTokens.Count == 0)
            {
                DeleteStateFile();
                return;
            }

            WriteState(state with
            {
                LoginSession = null
            });
        }
        catch (InvalidOperationException ex)
        {
            log.LogWarning(ex, "Failed to clear login session");
        }
    }

    public string? GetContextToken(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        return ReadState().ContextTokens.GetValueOrDefault(userId);
    }

    public void SetContextToken(string userId, string contextToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextToken);

        var state = ReadState();
        var contextTokens = new Dictionary<string, string>(state.ContextTokens, StringComparer.Ordinal)
        {
            [userId] = contextToken
        };

        WriteState(state with
        {
            ContextTokens = contextTokens
        });
    }

    private StoredSessionState ReadState()
    {
        if (!File.Exists(SessionStatePath))
        {
            return new StoredSessionState();
        }

        try
        {
            var payload = File.ReadAllText(SessionStatePath);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new StoredSessionState();
            }

            return JsonSerializer.Deserialize(payload, SessionStoreJsonContext.Default.StoredSessionState) ?? new StoredSessionState();
        }
        catch (IOException ex)
        {
            log.LogWarning(ex, "Failed to read session state");
            return new StoredSessionState();
        }
        catch (UnauthorizedAccessException ex)
        {
            log.LogWarning(ex, "Failed to read session state");
            return new StoredSessionState();
        }
        catch (JsonException ex)
        {
            log.LogWarning(ex, "Failed to deserialize session state");
            return new StoredSessionState();
        }
    }

    private void WriteState(StoredSessionState state)
    {
        var directory = Path.GetDirectoryName(SessionStatePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var serializedState = JsonSerializer.Serialize(state, SessionStoreJsonContext.Default.StoredSessionState);
        var tempPath = Path.Combine(directory ?? _rootPath, $"{Path.GetFileName(SessionStatePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(tempPath, serializedState);

            if (File.Exists(SessionStatePath))
            {
                File.Replace(tempPath, SessionStatePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, SessionStatePath);
            }
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException("Failed to persist session state.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException("Failed to persist session state.", ex);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private void DeleteStateFile()
    {
        try
        {
            if (File.Exists(SessionStatePath))
            {
                File.Delete(SessionStatePath);
            }
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException("Failed to delete session state.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException("Failed to delete session state.", ex);
        }
    }
}
