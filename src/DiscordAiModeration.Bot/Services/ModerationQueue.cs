using System.Threading.Channels;
using Discord;
using Discord.WebSocket;
using DiscordAiModeration.Core.Interfaces;
using DiscordAiModeration.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiscordAiModeration.Bot.Services;

public sealed class ModerationQueue
{
    private readonly Channel<ModerationRequest> _queue = Channel.CreateUnbounded<ModerationRequest>();
    private readonly IDatabase _database;
    private readonly IAiModerationService _aiModerationService;
    private readonly DiscordSocketClient _discordClient;
    private readonly ILogger<ModerationQueue> _logger;

    public ModerationQueue(
        IDatabase database,
        IAiModerationService aiModerationService,
        DiscordSocketClient discordClient,
        ILogger<ModerationQueue> logger)
    {
        _database = database;
        _aiModerationService = aiModerationService;
        _discordClient = discordClient;
        _logger = logger;
    }

    public ValueTask EnqueueAsync(ModerationRequest request, CancellationToken cancellationToken = default)
        => _queue.Writer.WriteAsync(request, cancellationToken);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var request in _queue.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                await ProcessAsync(request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process moderation request for message {MessageId}", request.MessageId);
            }
        }
    }

    private async Task ProcessAsync(ModerationRequest request, CancellationToken cancellationToken)
    {
        var settings = await _database.GetGuildSettingsAsync(request.GuildId, cancellationToken);
        if (settings is null || settings.AlertChannelId is null || !settings.AiEnabled)
            return;

        var rules = await _database.ListRulesAsync(request.GuildId, cancellationToken);
        if (rules.Count == 0)
            return;

        var feedbackExamples = await _database.GetFeedbackExamplesAsync(request.GuildId, 12, cancellationToken);
        var decision = await _aiModerationService.EvaluateAsync(request, settings, rules, feedbackExamples, cancellationToken);

        if (!decision.ShouldAlert || decision.Confidence < settings.ConfidenceThreshold)
            return;

        var alertId = await _database.InsertAlertAsync(new AlertRecord
        {
            GuildId = request.GuildId,
            MessageId = request.MessageId,
            ChannelId = request.ChannelId,
            UserId = request.UserId,
            RuleName = decision.RuleName,
            Confidence = decision.Confidence,
            Reason = decision.Reason,
            MessageContent = request.Content,
            FeedbackStatus = "pending",
            CreatedUtc = DateTime.UtcNow
        }, cancellationToken);

        if (_discordClient.GetChannel((ulong)settings.AlertChannelId.Value) is not IMessageChannel alertChannel)
            return;

        var pingText = settings.PingRoleId is long roleId ? $"<@&{roleId}>" : string.Empty;

        var embed = new EmbedBuilder()
            .WithTitle("⚠ Possible Rule Violation")
            .WithColor(Color.Orange)
            .AddField("Alert ID", $"#{alertId}", true)
            .AddField("User", request.Username, true)
            .AddField("Channel", request.ChannelMention, true)
            .AddField("Rule", decision.RuleName, true)
            .AddField("Confidence", $"{decision.Confidence}%", true)
            .AddField("Reason", string.IsNullOrWhiteSpace(decision.Reason) ? "No reason provided." : decision.Reason)
            .AddField("Message", $"```{TrimForCodeBlock(request.Content, 900)}```")
            .WithCurrentTimestamp()
            .Build();

        await alertChannel.SendMessageAsync(text: pingText, embed: embed);
    }

    private static string TrimForCodeBlock(string content, int maxLength)
    {
        content = content.Replace("```", "'''");
        return content.Length <= maxLength ? content : content[..maxLength] + "...";
    }
}
