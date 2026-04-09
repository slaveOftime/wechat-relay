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

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    [JsonPropertyName("items")]
    public List<HookPayloadItem> Items { get; init; } = [];

    [JsonPropertyName("context_token")]
    public string? ContextToken { get; init; }
}

internal sealed class HookPayloadItem
{
    [JsonPropertyName("item_type")]
    public int ItemType { get; init; }

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("preview_url")]
    public string? PreviewUrl { get; init; }

    [JsonPropertyName("download_url")]
    public string? DownloadUrl { get; init; }

    [JsonPropertyName("encrypt_query_param")]
    public string? EncryptQueryParam { get; init; }

    [JsonPropertyName("aes_key")]
    public string? AesKey { get; init; }

    [JsonPropertyName("encode_type")]
    public int? EncodeType { get; init; }

    [JsonPropertyName("sample_rate")]
    public int? SampleRate { get; init; }

    [JsonPropertyName("bits_per_sample")]
    public int? BitsPerSample { get; init; }

    [JsonPropertyName("playtime_ms")]
    public int? PlaytimeMs { get; init; }
}
