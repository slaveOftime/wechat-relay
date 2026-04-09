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

    [JsonPropertyName("voice_item")]
    public VoiceItem? VoiceItem { get; init; }

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
    [JsonPropertyName("media")]
    public CdnMedia? Media { get; init; }

    [JsonPropertyName("thumb_media")]
    public CdnMedia? ThumbMedia { get; init; }

    [JsonPropertyName("aeskey")]
    public string? AesKey { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("mid_size")]
    public long? MidSize { get; init; }

    [JsonPropertyName("thumb_size")]
    public long? ThumbSize { get; init; }

    [JsonPropertyName("thumb_height")]
    public int? ThumbHeight { get; init; }

    [JsonPropertyName("thumb_width")]
    public int? ThumbWidth { get; init; }

    [JsonPropertyName("hd_size")]
    public long? HdSize { get; init; }
}

public class VoiceItem
{
    [JsonPropertyName("media")]
    public CdnMedia? Media { get; init; }

    [JsonPropertyName("encode_type")]
    public int? EncodeType { get; init; }

    [JsonPropertyName("bits_per_sample")]
    public int? BitsPerSample { get; init; }

    [JsonPropertyName("sample_rate")]
    public int? SampleRate { get; init; }

    [JsonPropertyName("playtime")]
    public int? Playtime { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

public class FileItem
{
    [JsonPropertyName("file_name")]
    public string? FileName { get; init; }

    [JsonPropertyName("md5")]
    public string? Md5 { get; init; }

    [JsonPropertyName("len")]
    public long? Len { get; init; }

    [JsonPropertyName("media")]
    public CdnMedia? Media { get; init; }
}

public class VideoItem
{
    [JsonPropertyName("md5")]
    public string? Md5 { get; init; }

    [JsonPropertyName("len")]
    public long? Len { get; init; }

    [JsonPropertyName("media")]
    public CdnMedia? Media { get; init; }

    [JsonPropertyName("thumb_media")]
    public CdnMedia? ThumbMedia { get; init; }

    [JsonPropertyName("video_size")]
    public long? VideoSize { get; init; }
}

public class CdnMedia
{
    [JsonPropertyName("encrypt_query_param")]
    public string? EncryptQueryParam { get; init; }

    [JsonPropertyName("aes_key")]
    public string? AesKey { get; init; }

    [JsonPropertyName("encrypt_type")]
    public int? EncryptType { get; init; }
}

public class RefMessage
{
    [JsonPropertyName("from_user_id")]
    public string? FromUserId { get; init; }

    [JsonPropertyName("to_user_id")]
    public string? ToUserId { get; init; }

    [JsonPropertyName("create_time_ms")]
    public long? CreateTimeMs { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("type")]
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

    [JsonPropertyName("image_item")]
    public ImageItemOut? ImageItem { get; init; }

    [JsonPropertyName("voice_item")]
    public VoiceItemOut? VoiceItem { get; init; }
}

public class TextItemOut
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

public class ImageItemOut
{
    [JsonPropertyName("media")]
    public CdnMedia Media { get; init; } = new();

    [JsonPropertyName("mid_size")]
    public long MidSize { get; init; }

    [JsonPropertyName("hd_size")]
    public long? HdSize { get; init; }
}

public class VoiceItemOut
{
    [JsonPropertyName("media")]
    public CdnMedia Media { get; init; } = new();

    [JsonPropertyName("encode_type")]
    public int? EncodeType { get; init; }

    [JsonPropertyName("bits_per_sample")]
    public int? BitsPerSample { get; init; }

    [JsonPropertyName("sample_rate")]
    public int? SampleRate { get; init; }

    [JsonPropertyName("playtime")]
    public int? Playtime { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

public sealed class AudioSendOptions
{
    public int? EncodeType { get; init; }
    public int? BitsPerSample { get; init; }
    public int? SampleRate { get; init; }
    public int? PlaytimeMs { get; init; }
}

public sealed class GetUploadUrlRequest
{
    [JsonPropertyName("aeskey")]
    public string AesKey { get; init; } = string.Empty;

    [JsonPropertyName("base_info")]
    public UploadBaseInfo BaseInfo { get; init; } = new();

    [JsonPropertyName("filekey")]
    public string FileKey { get; init; } = string.Empty;

    [JsonPropertyName("filesize")]
    public long FileSize { get; init; }

    [JsonPropertyName("media_type")]
    public int MediaType { get; init; }

    [JsonPropertyName("no_need_thumb")]
    public bool NoNeedThumb { get; init; } = true;

    [JsonPropertyName("rawfilemd5")]
    public string RawFileMd5 { get; init; } = string.Empty;

    [JsonPropertyName("rawsize")]
    public long RawSize { get; init; }

    [JsonPropertyName("to_user_id")]
    public string ToUserId { get; init; } = string.Empty;
}

public sealed class UploadBaseInfo
{
    [JsonPropertyName("channel_version")]
    public string ChannelVersion { get; init; } = "1.0.0";
}

public sealed class GetUploadUrlResponse
{
    [JsonPropertyName("upload_param")]
    public string? UploadParam { get; init; }

    [JsonPropertyName("thumb_upload_param")]
    public string? ThumbUploadParam { get; init; }
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
