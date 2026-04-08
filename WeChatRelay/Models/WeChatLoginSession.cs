namespace WeChatRelay.Models;

public sealed class WeChatLoginSession
{
    public required string BotToken { get; init; }
    public required string BotId { get; init; }
    public string? UserId { get; init; }
    public required string BaseUrl { get; init; }
    public string BotType { get; init; } = "3";
    public DateTimeOffset SavedAt { get; init; } = DateTimeOffset.UtcNow;
}
