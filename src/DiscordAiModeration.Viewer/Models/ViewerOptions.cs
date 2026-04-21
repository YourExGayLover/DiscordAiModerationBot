namespace DiscordAiModeration.Viewer.Models;

public sealed class ViewerOptions
{
    public const string SectionName = "Viewer";

    public int Port { get; set; } = 5118;
    public int MaxMessagesPerChannel { get; set; } = 100;
    public bool AllowAttachmentLinks { get; set; } = true;
    public ulong? PreferredGuildId { get; set; }
}
