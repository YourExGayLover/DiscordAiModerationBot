using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using DiscordAiModeration.Viewer.Models;
using Microsoft.Extensions.Options;

namespace DiscordAiModeration.Viewer.Services;

public sealed class DiscordViewerState
{
    private readonly DiscordSocketClient _client;
    private readonly ViewerOptions _options;
    private readonly ConcurrentDictionary<ulong, ConcurrentQueue<MessageDto>> _liveMessagesByChannel = new();

    public DiscordViewerState(DiscordSocketClient client, IOptions<ViewerOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public IReadOnlyList<GuildSummaryDto> GetGuilds(DiscordSocketClient client)
    {
        IEnumerable<SocketGuild> guilds = client.Guilds;

        if (_options.PreferredGuildId is ulong preferredGuildId)
        {
            guilds = guilds.Where(x => x.Id == preferredGuildId);
        }

        return guilds
            .OrderBy(x => x.Name)
            .Select(g => new GuildSummaryDto(
                g.Id.ToString(),
                g.Name,
                g.TextChannels.Count,
                g.IconUrl))
            .ToList();
    }

    public IReadOnlyList<ChannelSummaryDto> GetChannels(DiscordSocketClient client, ulong? guildId)
    {
        IEnumerable<SocketGuild> guilds = client.Guilds;

        if (_options.PreferredGuildId is ulong preferredGuildId)
        {
            guilds = guilds.Where(x => x.Id == preferredGuildId);
        }

        if (guildId.HasValue)
        {
            guilds = guilds.Where(x => x.Id == guildId.Value);
        }

        return guilds
            .SelectMany(g => g.TextChannels.Select(c => new ChannelSummaryDto(
                c.Id.ToString(),
                c.Name,
                g.Id.ToString(),
                g.Name,
                c.Category?.Name,
                c.Position,
                _liveMessagesByChannel.TryGetValue(c.Id, out var q) ? q.Count : 0)))
            .OrderBy(x => x.CategoryName ?? "zzzzzzzz")
            .ThenBy(x => x.Position)
            .ThenBy(x => x.Name)
            .ToList();
    }

    public async Task<MessagePageDto> GetMessagePageAsync(ulong channelId, ulong? beforeMessageId)
    {
        if (_client.GetChannel(channelId) is not SocketTextChannel channel)
        {
            return new MessagePageDto(Array.Empty<MessageDto>(), false, null);
        }

        var pageSize = Math.Clamp(_options.MaxMessagesPerChannel, 1, 100);

        IEnumerable<IMessage> fetched = beforeMessageId.HasValue
            ? await channel.GetMessagesAsync(beforeMessageId.Value, Direction.Before, pageSize).FlattenAsync()
            : await channel.GetMessagesAsync(pageSize).FlattenAsync();

        var fetchedDtos = fetched
            .OrderByDescending(m => m.Timestamp)
            .Select(m => ToDto(channelId, m))
            .ToList();

        if (!beforeMessageId.HasValue && _liveMessagesByChannel.TryGetValue(channelId, out var liveQueue))
        {
            var combined = liveQueue
                .Concat(fetchedDtos)
                .GroupBy(m => m.Id)
                .Select(g => g.OrderByDescending(x => x.Timestamp).First())
                .OrderByDescending(m => m.Timestamp)
                .ToList();

            fetchedDtos = combined;
        }

        var hasMore = fetchedDtos.Count == pageSize;
        var nextBeforeMessageId = fetchedDtos.Count > 0 ? fetchedDtos[^1].Id : null;

        return new MessagePageDto(fetchedDtos, hasMore, nextBeforeMessageId);
    }

    public void StoreLiveMessage(SocketMessage message)
    {
        if (message.Channel is not SocketTextChannel)
        {
            return;
        }

        var queue = _liveMessagesByChannel.GetOrAdd(message.Channel.Id, _ => new ConcurrentQueue<MessageDto>());
        queue.Enqueue(ToDto(message.Channel.Id, message));
        Trim(queue);
    }

    private MessageDto ToDto(ulong channelId, IMessage message)
    {
        AttachmentDto[] attachments = _options.AllowAttachmentLinks
            ? message.Attachments.Select(a => new AttachmentDto(a.Filename, a.Url, a.Size)).ToArray()
            : Array.Empty<AttachmentDto>();

        var avatarUrl = message.Author.GetDisplayAvatarUrl(size: 64) ?? message.Author.GetDefaultAvatarUrl();
        var content = message.Content;

        if (string.IsNullOrWhiteSpace(content))
        {
            var embedText = message.Embeds
                .Select(e =>
                {
                    var parts = new List<string>();

                    if (!string.IsNullOrWhiteSpace(e.Title))
                        parts.Add(e.Title);

                    if (!string.IsNullOrWhiteSpace(e.Description))
                        parts.Add(e.Description);

                    if (e.Fields != null)
                    {
                        foreach (var field in e.Fields)
                        {
                            parts.Add($"{field.Name}: {field.Value}");
                        }
                    }

                    return string.Join("\n", parts);
                })
                .Where(x => !string.IsNullOrWhiteSpace(x));

            content = string.Join("\n\n", embedText);
        }
        return new MessageDto(
            message.Id.ToString(),
            channelId.ToString(),
            message.Author.Id.ToString(),
            message.Author.GlobalName ?? message.Author.Username,
            avatarUrl,
            content,
            message.Timestamp,
            message.Author.IsBot,
            attachments);
    }

    private void Trim(ConcurrentQueue<MessageDto> queue)
    {
        while (queue.Count > _options.MaxMessagesPerChannel)
        {
            queue.TryDequeue(out _);
        }
    }
}
