using System.Text.Json.Serialization;

namespace WeChatRelay.Models;

// ───────────── QR Login ─────────────

public class QrStartResponse
{
    [JsonPropertyName("qrcode_img_content")]
    public string? QrcodeUrl { get; init; }

    [JsonPropertyName("qrcode")]
    public string Qrcode { get; init; } = string.Empty;

    public int Ret { get; init; }
}

public class QrStatusResponse
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("bot_token")]
    public string? BotToken { get; init; }

    [JsonPropertyName("ilink_bot_id")]
    public string? AccountId { get; init; }

    [JsonPropertyName("baseurl")]
    public string? BaseUrl { get; init; }

    [JsonPropertyName("ilink_user_id")]
    public string? UserId { get; init; }
}

// ───────────── Inbound Messages ─────────────

public class GetUpdatesResponse
{
    [JsonPropertyName("ret")]
    public int Ret { get; init; }

    [JsonPropertyName("err_code")]
    public int? ErrCode { get; init; }

    [JsonPropertyName("err_msg")]
    public string? ErrMsg { get; init; }

    [JsonPropertyName("msgs")]
    public List<InboundMessage> Msgs { get; init; } = [];

    [JsonPropertyName("sync_buf")]
    public string SyncBuf { get; init; } = string.Empty;

    [JsonPropertyName("get_updates_buf")]
    public string GetUpdatesBuf { get; init; } = string.Empty;

    [JsonPropertyName("longpolling_timeout_ms")]
    public int? LongPollingTimeoutMs { get; init; }
}

public class InboundMessage
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
    public int? MessageType { get; init; } // 1=USER, 2=BOT

    [JsonPropertyName("message_state")]
    public int? MessageState { get; init; }

    [JsonPropertyName("item_list")]
    public List<MessageItem> ItemList { get; init; } = [];

    [JsonPropertyName("context_token")]
    public string? ContextToken { get; init; }
}

public class MessageItem
{
    [JsonPropertyName("type")]
    public int Type { get; init; } // 1=TEXT, 2=IMAGE, 3=VOICE, 4=FILE, 5=VIDEO

    [JsonPropertyName("text_item")]
    public TextItem? TextItem { get; init; }

    [JsonPropertyName("image_item")]
    public ImageItem? ImageItem { get; init; }

    [JsonPropertyName("file_item")]
    public FileItem? FileItem { get; init; }

    [JsonPropertyName("video_item")]
    public VideoItem? VideoItem { get; init; }

    [JsonPropertyName("ref_msg")]
    public RefMessage? RefMsg { get; init; }
}

public class TextItem
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

public class ImageItem
{
    public string? Md5 { get; init; }
    public long? Len { get; init; }
    public string? Url { get; init; }
    public CdnMedia? CdnMedia { get; init; }
}

public class FileItem
{
    public string? FileName { get; init; }
    public string? Md5 { get; init; }
    public long? Len { get; init; }
    public CdnMedia? CdnMedia { get; init; }
}

public class VideoItem
{
    public string? Md5 { get; init; }
    public long? Len { get; init; }
    public CdnMedia? CdnMedia { get; init; }
}

public class CdnMedia
{
    public string? EncryptQueryParam { get; init; }
    public string? AesKey { get; init; }
}

public class RefMessage
{
    public string? FromUserId { get; init; }
    public string? ToUserId { get; init; }
    public long? CreateTimeMs { get; init; }
    public string? Content { get; init; }
    public int? Type { get; init; }
}

// ───────────── Send Message ─────────────

public class SendMessageRequest
{
    [JsonPropertyName("msg")]
    public OutboundMessage Msg { get; init; } = new();
}

public class OutboundMessage
{
    [JsonPropertyName("from_user_id")]
    public string? FromUserId { get; init; }

    [JsonPropertyName("to_user_id")]
    public string ToUserId { get; init; } = string.Empty;

    [JsonPropertyName("client_id")]
    public string? ClientId { get; init; }

    [JsonPropertyName("message_type")]
    public int? MessageType { get; init; } = 2;

    [JsonPropertyName("message_state")]
    public int? MessageState { get; init; } = 2;

    [JsonPropertyName("context_token")]
    public string? ContextToken { get; init; }

    [JsonPropertyName("item_list")]
    public List<OutboundItem> ItemList { get; init; } = [];
}

public class OutboundItem
{
    [JsonPropertyName("type")]
    public int Type { get; init; }

    [JsonPropertyName("text_item")]
    public TextItemOut? TextItem { get; init; }
}

public class TextItemOut
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

public class SendMessageResponse
{
    [JsonPropertyName("ret")]
    public int Ret { get; init; }

    [JsonPropertyName("err_code")]
    public int? ErrCode { get; init; }

    [JsonPropertyName("err_msg")]
    public string? ErrMsg { get; init; }
}

// ───────────── Send-To Candidate ─────────────

public record SendToCandidate(
    string Id,
    string Label,
    string Kind); // "wechat" | "oly"
