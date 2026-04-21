namespace DiscordAiModeration.Viewer.Models;

public sealed record GuildSummaryDto(string Id, string Name, int TextChannelCount);

public sealed record ChannelSummaryDto(string Id, string Name, string GuildId, string GuildName, int Position, int RecentMessageCount);

public sealed record AttachmentDto(string FileName, string Url, long? Size);

public sealed record MessageDto(
    string Id,
    string ChannelId,
    string AuthorId,
    string AuthorName,
    string Content,
    DateTimeOffset Timestamp,
    bool IsBot,
    IReadOnlyList<AttachmentDto> Attachments);

public sealed record MessagePageDto(
    IReadOnlyList<MessageDto> Items,
    bool HasMore,
    string? NextBeforeMessageId);
