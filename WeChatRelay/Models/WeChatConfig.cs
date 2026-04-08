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
}
