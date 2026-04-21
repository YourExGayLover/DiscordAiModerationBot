namespace DiscordAiModeration.Viewer.Models;

public sealed record GuildSummaryDto(ulong Id, string Name, int TextChannelCount);

public sealed record ChannelSummaryDto(ulong Id, string Name, ulong GuildId, string GuildName, int Position, int RecentMessageCount);

public sealed record AttachmentDto(string FileName, string Url, long? Size);

public sealed record MessageDto(
    ulong Id,
    ulong ChannelId,
    ulong AuthorId,
    string AuthorName,
    string Content,
    DateTimeOffset Timestamp,
    bool IsBot,
    IReadOnlyList<AttachmentDto> Attachments);
