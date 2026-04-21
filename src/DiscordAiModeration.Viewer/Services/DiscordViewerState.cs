using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using DiscordAiModeration.Viewer.Models;
using Microsoft.Extensions.Options;

namespace DiscordAiModeration.Viewer.Services;

public sealed class DiscordViewerState
{
    private readonly ConcurrentDictionary<ulong, ConcurrentQueue<MessageDto>> _messagesByChannel = new();
    private readonly ViewerOptions _options;

    public DiscordViewerState(IOptions<ViewerOptions> options)
    {
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
                g.Id,
                g.Name,
                g.TextChannels.Count))
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
                c.Id,
                c.Name,
                g.Id,
                g.Name,
                c.Position,
                _messagesByChannel.TryGetValue(c.Id, out var q) ? q.Count : 0)))
            .OrderBy(x => x.GuildName)
            .ThenBy(x => x.Position)
            .ThenBy(x => x.Name)
            .ToList();
    }

    public IReadOnlyList<MessageDto> GetMessages(ulong channelId)
    {
        if (!_messagesByChannel.TryGetValue(channelId, out var queue))
        {
            return Array.Empty<MessageDto>();
        }

        return queue
            .OrderBy(x => x.Timestamp)
            .ToList();
    }

    public void StoreHistoricalMessages(ulong channelId, IEnumerable<IMessage> messages)
    {
        var queue = _messagesByChannel.GetOrAdd(channelId, _ => new ConcurrentQueue<MessageDto>());

        foreach (var message in messages.OrderBy(x => x.Timestamp))
        {
            queue.Enqueue(ToDto(channelId, message));
        }

        Trim(queue);
    }

    public void StoreLiveMessage(SocketMessage message)
    {
        if (message.Channel is not SocketTextChannel)
        {
            return;
        }

        var queue = _messagesByChannel.GetOrAdd(message.Channel.Id, _ => new ConcurrentQueue<MessageDto>());
        queue.Enqueue(ToDto(message.Channel.Id, message));
        Trim(queue);
    }

    private MessageDto ToDto(ulong channelId, IMessage message)
    {
        AttachmentDto[] attachments = _options.AllowAttachmentLinks
            ? message.Attachments.Select(a => new AttachmentDto(a.Filename, a.Url, a.Size)).ToArray()
            : Array.Empty<AttachmentDto>();

        return new MessageDto(
            message.Id,
            channelId,
            message.Author.Id,
            message.Author.GlobalName ?? message.Author.Username,
            message.Content,
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
