using System.Collections.Concurrent;
using System.Text.RegularExpressions;
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
            .SelectMany(g =>
                g.TextChannels
                    .OrderBy(c => c.Category is null ? int.MaxValue : c.Category.Position)
                    .ThenBy(c => c.Category is null ? 1 : 0)
                    .ThenBy(c => c.Position)
                    .ThenBy(c => c.Name)
                    .Select(c => new ChannelSummaryDto(
                        c.Id.ToString(),
                        c.Name,
                        g.Id.ToString(),
                        g.Name,
                        c.Category?.Name,
                        c.Position,
                        _liveMessagesByChannel.TryGetValue(c.Id, out var q) ? q.Count : 0)))
            .ToList();
    }

    public async Task<MessagePageDto> GetMessagePageAsync(ulong channelId, ulong? beforeMessageId)
    {
        if (_client.GetChannel(channelId) is not SocketTextChannel channel)
        {
            return new MessagePageDto(Array.Empty<MessageDto>(), false, null);
        }

        var pageSize = Math.Clamp(_options.MaxMessagesPerChannel, 1, 100);

        var fetched = beforeMessageId.HasValue
            ? await channel.GetMessagesAsync(beforeMessageId.Value, Direction.Before, pageSize).FlattenAsync()
            : await channel.GetMessagesAsync(pageSize).FlattenAsync();

        var fetchedDtos = fetched
            .OrderByDescending(m => m.Timestamp)
            .Select(m => ToDto(channelId, m))
            .ToList();

        var hasMore = fetchedDtos.Count == pageSize;
        var nextBeforeMessageId = fetchedDtos.Count > 0 ? fetchedDtos[^1].Id : null;

        if (!beforeMessageId.HasValue && _liveMessagesByChannel.TryGetValue(channelId, out var liveQueue))
        {
            fetchedDtos = liveQueue
                .Concat(fetchedDtos)
                .GroupBy(m => m.Id)
                .Select(g => g.OrderByDescending(x => x.Timestamp).First())
                .OrderByDescending(m => m.Timestamp)
                .Take(pageSize)
                .ToList();
        }

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
        var content = BuildDisplayContent(message, attachments);

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

    private string BuildDisplayContent(IMessage message, IReadOnlyList<AttachmentDto> attachments)
    {
        var content = message.Content?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(content))
        {
            return FormatDiscordText(content);
        }

        var embedParts = new List<string>();

        foreach (var embed in message.Embeds)
        {
            if (!string.IsNullOrWhiteSpace(embed.Title))
            {
                embedParts.Add(embed.Title.Trim());
            }

            if (!string.IsNullOrWhiteSpace(embed.Description))
            {
                embedParts.Add(embed.Description.Trim());
            }

            foreach (var field in embed.Fields)
            {
                var name = field.Name?.Trim();
                var value = field.Value?.Trim();

                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
                {
                    embedParts.Add($"{name}: {value}");
                }
                else if (!string.IsNullOrWhiteSpace(value))
                {
                    embedParts.Add(value);
                }
            }

            if (embed.Footer.HasValue && !string.IsNullOrWhiteSpace(embed.Footer.Value.Text))
            {
                embedParts.Add(embed.Footer.Value.Text.Trim());
            }
        }

        var embedText = string.Join("\n\n", embedParts.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();

        if (!string.IsNullOrWhiteSpace(embedText))
        {
            return FormatDiscordText(embedText);
        }

        if (attachments.Count > 0)
        {
            return "[attachment]";
        }

        return "[no text]";
    }

    private static string FormatDiscordText(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var text = input;
        text = Regex.Replace(text, @"^\s*>\s?", string.Empty, RegexOptions.Multiline);
        text = Regex.Replace(text, @"`([^`]+)`", "$1");
        text = Regex.Replace(text, @"\*\*(.*?)\*\*", "$1");
        text = Regex.Replace(text, @"__(.*?)__", "$1");
        text = Regex.Replace(text, @"\*(.*?)\*", "$1");
        text = Regex.Replace(text, @"~~(.*?)~~", "$1");
        text = Regex.Replace(text, @"<@!?(\d+)>", "@$1");
        text = Regex.Replace(text, @"<@&(\d+)>", "@role:$1");
        text = Regex.Replace(text, @"<#(\d+)>", "#$1");
        text = Regex.Replace(text, @"<t:(\d+):R>", match =>
        {
            if (!long.TryParse(match.Groups[1].Value, out var unix))
            {
                return match.Value;
            }

            var then = DateTimeOffset.FromUnixTimeSeconds(unix);
            var diff = DateTimeOffset.UtcNow - then;

            if (diff.TotalMinutes < 1)
            {
                return "just now";
            }

            if (diff.TotalHours < 1)
            {
                return $"{(int)diff.TotalMinutes} min ago";
            }

            if (diff.TotalDays < 1)
            {
                return $"{(int)diff.TotalHours} hours ago";
            }

            return $"{(int)diff.TotalDays} days ago";
        });
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        return text.Trim();
    }

    private void Trim(ConcurrentQueue<MessageDto> queue)
    {
        while (queue.Count > _options.MaxMessagesPerChannel)
        {
            queue.TryDequeue(out _);
        }
    }
}