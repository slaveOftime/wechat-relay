namespace WeChatRelay.Models;

public sealed class WeChatOptions
{
    public string BaseUrl { get; init; } = "https://ilinkai.weixin.qq.com/";
    public string BotType { get; init; } = "3";
    public string? UserId { get; init; }
    public string? ToUsers { get; init; }
}
