namespace DiscordAiModeration.Core.Models;

public sealed class RuleRecord
{
    public long Id { get; set; }
    public long GuildId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ExamplesJson { get; set; }
}
