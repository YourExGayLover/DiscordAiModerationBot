namespace DiscordAiModeration.Core.Models;

public sealed class GuildSettings
{
    public long GuildId { get; set; }
    public long? AlertChannelId { get; set; }
    public long? PingRoleId { get; set; }
    public int ConfidenceThreshold { get; set; } = 70;
    public bool AiEnabled { get; set; } = true;
    public bool UseSimplePrompts { get; set; }
}
