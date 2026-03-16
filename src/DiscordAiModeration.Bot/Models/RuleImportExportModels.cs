using System.Text.Json.Serialization;

namespace DiscordAiModeration.Bot.Models;

public sealed class RulesExportFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("guildId")]
    public long GuildId { get; set; }

    [JsonPropertyName("exportedUtc")]
    public DateTime ExportedUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("rules")]
    public List<RuleImportItem> Rules { get; set; } = new();
}

public sealed class RuleImportItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("examples")]
    public List<string> Examples { get; set; } = new();
}
