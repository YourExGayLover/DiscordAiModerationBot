namespace DiscordAiModeration.Core.Models;

public sealed class AlertRecord
{
    public long Id { get; set; }
    public long GuildId { get; set; }
    public long MessageId { get; set; }
    public long ChannelId { get; set; }
    public long UserId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public int Confidence { get; set; }
    public string? Reason { get; set; }
    public string MessageContent { get; set; } = string.Empty;
    public string FeedbackStatus { get; set; } = "pending";
    public string? FeedbackNotes { get; set; }
    public long? ReviewedByUserId { get; set; }
    public DateTime CreatedUtc { get; set; }
}
