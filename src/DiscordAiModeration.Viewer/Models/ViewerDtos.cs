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

public sealed record AttachmentDto(
    string FileName,
    string Url,
    long? Size);

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
    string UserId,
    string DisplayName,
    string? AvatarUrl,
    bool IsStreaming,
    bool IsSelfMuted,
    bool IsSelfDeafened,
    bool IsServerMuted,
    bool IsServerDeafened,
    bool IsSuppressed);

public sealed record VoiceChannelDto(
    string ChannelId,
    string ChannelName,
    IReadOnlyList<VoiceMemberDto> Members);

public sealed record VoiceSnapshotDto(
    string GuildId,
    string GuildName,
    int ConnectedCount,
    int ActiveChannelCount,
    IReadOnlyList<VoiceChannelDto> Channels);

public sealed record UserProfileDto(
    string UserId,
    string GuildId,
    string DisplayName,
    string Username,
    string? GlobalName,
    string? Nickname,
    string? AvatarUrl,
    bool IsBot,
    string? JoinedAt,
    string CreatedAt,
    IReadOnlyList<string> Roles,
    bool IsInVoice,
    string? VoiceChannelName,
    bool IsStreaming,
    bool IsMuted,
    bool IsDeafened);
