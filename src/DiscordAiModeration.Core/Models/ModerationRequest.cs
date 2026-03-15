namespace DiscordAiModeration.Core.Models;

public sealed class ModerationRequest
{
    public long GuildId { get; set; }
    public long ChannelId { get; set; }
    public long MessageId { get; set; }
    public long UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string ChannelMention { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
}
