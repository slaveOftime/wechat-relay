using System.Text.Json.Serialization;
using WeChatRelay.Models;
using WeChatRelay.Services;

namespace WeChatRelay.Serialization;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GetUpdatesRequest))]
[JsonSerializable(typeof(GetUpdatesResponse))]
[JsonSerializable(typeof(HookPayload))]
[JsonSerializable(typeof(InboundMessage))]
[JsonSerializable(typeof(QrStartResponse))]
[JsonSerializable(typeof(QrStatusResponse))]
[JsonSerializable(typeof(GetUploadUrlRequest))]
[JsonSerializable(typeof(GetUploadUrlResponse))]
[JsonSerializable(typeof(SendMessageRequest))]
[JsonSerializable(typeof(SendMessageResponse))]
[JsonSerializable(typeof(HistoryEntry))]
[JsonSerializable(typeof(SentHistoryData))]
[JsonSerializable(typeof(List<HistoryEntry>))]
internal partial class WeChatJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(StoredSessionState))]
internal partial class SessionStoreJsonContext : JsonSerializerContext
{
}
