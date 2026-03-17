namespace DiscordAiModeration.Core.Models;

public sealed class ModerationContextMessage
{
    public long MessageId { get; set; }
    public long UserId { get; set; }
    public string AuthorDisplay { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsCurrentUser { get; set; }
}
