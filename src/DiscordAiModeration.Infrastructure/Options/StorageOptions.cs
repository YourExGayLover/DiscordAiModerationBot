namespace DiscordAiModeration.Infrastructure.Options;

public sealed class StorageOptions
{
    public string SqlitePath { get; set; } = "botdata.db";
}
