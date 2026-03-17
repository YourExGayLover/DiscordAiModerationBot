using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Discord;
using Discord.WebSocket;
using DiscordAiModeration.Bot.Helpers;
using DiscordAiModeration.Core.Interfaces;
using DiscordAiModeration.Core.Models;
using DiscordAiModeration.Infrastructure.Services;
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
    {
        _logger.LogInformation(
            "Queued moderation request Guild={GuildId} Channel={ChannelId} Message={MessageId} User={UserId} Preview=\"{Preview}\"",
            request.GuildId,
            request.ChannelId,
            request.MessageId,
            request.UserId,
            HttpLoggingHelper.SafeMessagePreview(request.Content));

        return _queue.Writer.WriteAsync(request, cancellationToken);
    }

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
        var stopwatch = Stopwatch.StartNew();

        var settings = await _database.GetGuildSettingsAsync(request.GuildId, cancellationToken);
        if (settings is null || !settings.AiEnabled || settings.AlertChannelId is null)
        {
            return;
        }

        var rules = await _database.ListRulesAsync(request.GuildId, cancellationToken);
        if (rules.Count == 0)
        {
            return;
        }

        var feedbackExamples = await _database.GetFeedbackExamplesAsync(request.GuildId, 12, cancellationToken);
        var decision = await _aiModerationService.EvaluateAsync(request, settings, rules, feedbackExamples, cancellationToken);

        _logger.LogInformation(
            "AI decision for message {MessageId}: ShouldAlert={ShouldAlert} Verdict={Verdict} Rule={RuleName} Confidence={Confidence} Reason=\"{Reason}\" ElapsedMs={ElapsedMs}",
            request.MessageId,
            decision.ShouldAlert,
            decision.Verdict,
            decision.RuleName,
            decision.Confidence,
            HttpLoggingHelper.Truncate(decision.Reason, 300),
            stopwatch.ElapsedMilliseconds);

        if (!decision.ShouldAlert || decision.Confidence < settings.ConfidenceThreshold)
        {
            return;
        }

        var violatedRules = decision.ViolatedRules.Count == 0 && !string.IsNullOrWhiteSpace(decision.RuleName)
            ? new[] { decision.RuleName }
            : decision.ViolatedRules;

        var mergedSources = HeresySourceCatalog.MergeSources(decision.RuleName, decision.Sources);
        var explanation = string.IsNullOrWhiteSpace(decision.Explanation)
            ? decision.Reason
            : decision.Explanation.Trim();

        var finalReason = BuildStoredReason(decision, violatedRules, mergedSources, explanation);

        var alertId = await _database.InsertAlertAsync(
            new AlertRecord
            {
                GuildId = request.GuildId,
                MessageId = request.MessageId,
                ChannelId = request.ChannelId,
                UserId = request.UserId,
                RuleName = decision.RuleName,
                Confidence = decision.Confidence,
                Reason = finalReason,
                MessageContent = request.Content,
                FeedbackStatus = "pending",
                CreatedUtc = DateTime.UtcNow
            },
            cancellationToken);

        if (_discordClient.GetChannel((ulong)settings.AlertChannelId.Value) is not IMessageChannel alertChannel)
        {
            return;
        }

        var pingText = settings.PingRoleId is long roleId ? $"<@&{roleId}>" : string.Empty;
        var messageLink = BuildDiscordMessageLink(request.GuildId, request.ChannelId, request.MessageId);

        var embed = new EmbedBuilder()
            .WithTitle(GetAlertTitle(decision.Verdict))
            .WithColor(GetAlertColor(decision.Verdict))
            .AddField("Alert ID", $"#{alertId}", true)
            .AddField("User", request.Username, true)
            .AddField("Confidence", $"{decision.Confidence}%", true)
            .AddField("Verdict", string.IsNullOrWhiteSpace(decision.Verdict) ? "violation" : decision.Verdict, true)
            .AddField("Message Link", messageLink, false)
            .AddField("Violated Rules", FormatRuleList(violatedRules), false)
            .AddField("Explanation", TrimForField(explanation, 1024), false)
            .AddField("Sources", TrimForField(FormatSources(mergedSources), 1024), false)
            .AddField("Message", $"```{TrimForCodeBlock(request.Content, 900)}```")
            .WithCurrentTimestamp()
            .Build();

        await alertChannel.SendMessageAsync(text: pingText, embed: embed);
    }

    private static string BuildStoredReason(
        AiDecision decision,
        IReadOnlyList<string> violatedRules,
        IReadOnlyList<string> mergedSources,
        string explanation)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Verdict: {decision.Verdict}");
        builder.AppendLine($"Confidence: {decision.Confidence}%");
        builder.AppendLine($"Violated Rules: {FormatRuleList(violatedRules)}");
        builder.AppendLine($"Summary: {decision.Reason}");
        builder.AppendLine();
        builder.AppendLine("Explanation:");
        builder.AppendLine(explanation);

        if (mergedSources.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Sources:");
            foreach (var source in mergedSources)
            {
                builder.AppendLine($"- {source}");
            }
        }

        return builder.ToString().Trim();
    }

    private static string GetAlertTitle(string? verdict) => verdict?.ToLowerInvariant() switch
    {
        "heresy" => "❌ Heresy Detected",
        "violation" => "⚠ Rule Violation Detected",
        _ => "⚠ Moderation Alert"
    };

    private static Color GetAlertColor(string? verdict) => verdict?.ToLowerInvariant() switch
    {
        "heresy" => Color.Red,
        "violation" => Color.Orange,
        _ => Color.DarkGrey
    };

    private static string BuildDiscordMessageLink(long guildId, long channelId, long messageId) =>
        $"https://discord.com/channels/{guildId}/{channelId}/{messageId}";

    private static string FormatRuleList(IReadOnlyList<string> rules) =>
        rules.Count == 0 ? "None provided." : string.Join("\n", rules.Select(x => $"• {x}"));

    private static string FormatSources(IReadOnlyList<string> sources) =>
        sources.Count == 0 ? "No sources provided." : string.Join("\n", sources.Select(x => $"• {x}"));

    private static string TrimForCodeBlock(string content, int maxLength)
    {
        content = content.Replace("```", "'''");
        return content.Length <= maxLength ? content : content[..maxLength] + "...";
    }

    private static string TrimForField(string content, int maxLength) =>
        content.Length <= maxLength ? content : content[..(maxLength - 3)] + "...";
}
