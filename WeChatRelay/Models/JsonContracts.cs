using System.Text.Json.Serialization;

namespace WeChatRelay.Models;

internal sealed class GetUpdatesRequest
{
    [JsonPropertyName("get_updates_buf")]
    public string GetUpdatesBuf { get; init; } = string.Empty;
}

internal sealed class HookPayload
{
    [JsonPropertyName("seq")]
    public long? Seq { get; init; }

    [JsonPropertyName("message_id")]
    public long? MessageId { get; init; }

    [JsonPropertyName("from_user_id")]
    public string? FromUserId { get; init; }

    [JsonPropertyName("to_user_id")]
    public string? ToUserId { get; init; }

    [JsonPropertyName("create_time_ms")]
    public long? CreateTimeMs { get; init; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("message_type")]
    public int? MessageType { get; init; }

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("context_token")]
    public string? ContextToken { get; init; }
}
