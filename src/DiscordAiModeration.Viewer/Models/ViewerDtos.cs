namespace DiscordAiModeration.Viewer.Models;

public sealed record GuildSummaryDto(
    string Id,
    string Name,
    int TextChannelCount,
    string? IconUrl);

public sealed record ChannelSummaryDto(
    string Id,
    string Name,
    string GuildId,
    string GuildName,
    string? CategoryName,
    int Position,
    int RecentMessageCount);

public sealed record AttachmentDto(string FileName, string Url, long? Size);

public sealed record MessageDto(
    string Id,
    string ChannelId,
    string AuthorId,
    string AuthorName,
    string? AuthorAvatarUrl,
    string Content,
    DateTimeOffset Timestamp,
    bool IsBot,
    IReadOnlyList<AttachmentDto> Attachments);

public sealed record MessagePageDto(
    IReadOnlyList<MessageDto> Items,
    bool HasMore,
    string? NextBeforeMessageId);

public sealed record VoiceMemberDto(
    string Id,
    string DisplayName,
    string? AvatarUrl,
    bool IsMuted,
    bool IsDeafened,
    bool IsStreaming,
    bool IsVideoEnabled);

public sealed record VoiceChannelStateDto(
    string Id,
    string Name,
    string GuildId,
    string GuildName,
    string? CategoryName,
    int Position,
    int ConnectedCount,
    IReadOnlyList<VoiceMemberDto> Members);
