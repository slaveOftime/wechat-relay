namespace WeChatRelay.Models;

/// <summary>
/// WeChat credentials from QR login.
/// </summary>
public class WeChatConfig
{
    public string? BotToken { get; init; }
    public string? BotId { get; init; }
    public string? UserId { get; init; }
    public string? BaseUrl { get; init; }
    public string BotType { get; init; } = "3";
    public string? ToUsers { get; init; }

    public static WeChatConfig Create(WeChatOptions options, WeChatLoginSession? session) =>
        new()
        {
            BotToken = session?.BotToken,
            BotId = session?.BotId,
            UserId = session?.UserId ?? options.UserId,
            BaseUrl = session?.BaseUrl ?? options.BaseUrl,
            BotType = session?.BotType ?? options.BotType,
            ToUsers = options.ToUsers
        };
}
