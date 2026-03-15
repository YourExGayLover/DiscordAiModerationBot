namespace DiscordAiModeration.Core.Models;

public sealed class FeedbackExample
{
    public string RuleName { get; set; } = string.Empty;
    public string MessageContent { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
