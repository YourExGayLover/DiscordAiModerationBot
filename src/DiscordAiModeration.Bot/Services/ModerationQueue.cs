using System.Diagnostics;
using System.Text.RegularExpressions;
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
    private readonly ILogger _logger;

    public ModerationQueue(
        IDatabase database,
        IAiModerationService aiModerationService,
        DiscordSocketClient discordClient,
        ILogger logger)
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

        _logger.LogInformation(
            "Processing moderation request Guild={GuildId} Channel={ChannelId} Message={MessageId}",
            request.GuildId,
            request.ChannelId,
            request.MessageId);

        var settings = await _database.GetGuildSettingsAsync(request.GuildId, cancellationToken);
        if (settings is null)
        {
            _logger.LogInformation("Skipping message {MessageId}: no guild settings found.", request.MessageId);
            return;
        }

        if (!settings.AiEnabled)
        {
            _logger.LogInformation("Skipping message {MessageId}: AI moderation disabled for guild {GuildId}.", request.MessageId, request.GuildId);
            return;
        }

        if (settings.AlertChannelId is null)
        {
            _logger.LogWarning("Skipping message {MessageId}: no alert channel configured for guild {GuildId}.", request.MessageId, request.GuildId);
            return;
        }

        var rules = await _database.ListRulesAsync(request.GuildId, cancellationToken);
        if (rules.Count == 0)
        {
            _logger.LogInformation("Skipping message {MessageId}: no moderation rules configured for guild {GuildId}.", request.MessageId, request.GuildId);
            return;
        }

        var feedbackExamples = await _database.GetFeedbackExamplesAsync(request.GuildId, 12, cancellationToken);

        _logger.LogInformation(
            "Sending message {MessageId} to AI ProviderRules={RuleCount} FeedbackExamples={FeedbackCount} Threshold={Threshold}",
            request.MessageId,
            rules.Count,
            feedbackExamples.Count,
            settings.ConfidenceThreshold);

        var decision = await _aiModerationService.EvaluateAsync(request, settings, rules, feedbackExamples, cancellationToken);

        var matchedRule = rules.FirstOrDefault(r =>
            string.Equals(r.Name, decision.RuleName, StringComparison.OrdinalIgnoreCase));

        var reasonWithCatechism = BuildReasonWithCatechism(decision.Reason, matchedRule?.Description);

        _logger.LogInformation(
            "AI decision for message {MessageId}: ShouldAlert={ShouldAlert} Rule={RuleName} Confidence={Confidence} Reason=\"{Reason}\" ElapsedMs={ElapsedMs}",
            request.MessageId,
            decision.ShouldAlert,
            decision.RuleName,
            decision.Confidence,
            HttpLoggingHelper.Truncate(reasonWithCatechism, 300),
            stopwatch.ElapsedMilliseconds);

        if (!decision.ShouldAlert)
        {
            _logger.LogInformation("Message {MessageId} did not trigger an alert.", request.MessageId);
            return;
        }

        if (decision.Confidence < settings.ConfidenceThreshold)
        {
            _logger.LogInformation(
                "Message {MessageId} below threshold. Confidence={Confidence} Threshold={Threshold}",
                request.MessageId,
                decision.Confidence,
                settings.ConfidenceThreshold);
            return;
        }

        var alertId = await _database.InsertAlertAsync(
            new AlertRecord
            {
                GuildId = request.GuildId,
                MessageId = request.MessageId,
                ChannelId = request.ChannelId,
                UserId = request.UserId,
                RuleName = decision.RuleName,
                Confidence = decision.Confidence,
                Reason = reasonWithCatechism,
                MessageContent = request.Content,
                FeedbackStatus = "pending",
                CreatedUtc = DateTime.UtcNow
            },
            cancellationToken);

        if (_discordClient.GetChannel((ulong)settings.AlertChannelId.Value) is not IMessageChannel alertChannel)
        {
            _logger.LogWarning(
                "Alert {AlertId} for message {MessageId} was stored, but alert channel {AlertChannelId} could not be resolved.",
                alertId,
                request.MessageId,
                settings.AlertChannelId.Value);
            return;
        }

        var pingText = settings.PingRoleId is long roleId ? $"<@&{roleId}>" : string.Empty;
        var messageLink = BuildDiscordMessageLink(request.GuildId, request.ChannelId, request.MessageId);

        var embed = new EmbedBuilder()
            .WithTitle("⚠ Possible Rule Violation")
            .WithColor(Color.Orange)
            .AddField("Alert ID", $"#{alertId}", true)
            .AddField("User", request.Username, true)
            .AddField("Message Link", messageLink, false)
            .AddField("Rule", decision.RuleName, true)
            .AddField("Confidence", $"{decision.Confidence}%", true)
            .AddField("Reason", TrimForEmbedField(reasonWithCatechism, 1000))
            .AddField("Message", $"```{TrimForCodeBlock(request.Content, 900)}```")
            .WithCurrentTimestamp()
            .Build();

        await alertChannel.SendMessageAsync(text: pingText, embed: embed);

        _logger.LogInformation(
            "Alert sent AlertId={AlertId} MessageId={MessageId} Rule={RuleName} Confidence={Confidence} AlertChannelId={AlertChannelId} MessageLink={MessageLink}",
            alertId,
            request.MessageId,
            decision.RuleName,
            decision.Confidence,
            settings.AlertChannelId.Value,
            messageLink);
    }

    private static string BuildDiscordMessageLink(long guildId, long channelId, long messageId) =>
        $"https://discord.com/channels/{guildId}/{channelId}/{messageId}";

    private static string TrimForCodeBlock(string content, int maxLength)
    {
        content = content.Replace("```", "'''");
        return content.Length <= maxLength ? content : content[..maxLength] + "...";
    }

    private static string TrimForEmbedField(string content, int maxLength) =>
        string.IsNullOrWhiteSpace(content)
            ? "No reason provided."
            : content.Length <= maxLength
                ? content
                : content[..Math.Max(0, maxLength - 3)] + "...";

    private static string BuildReasonWithCatechism(string? aiReason, string? ruleDescription)
    {
        var cleanReason = string.IsNullOrWhiteSpace(aiReason)
            ? "Likely violates the matched Catholic moderation rule."
            : aiReason.Trim();

        if (ContainsCatechismCitation(cleanReason))
        {
            return cleanReason;
        }

        var (references, quote) = ExtractCatechismMetadata(ruleDescription);
        if (string.IsNullOrWhiteSpace(references) && string.IsNullOrWhiteSpace(quote))
        {
            return cleanReason;
        }

        if (!string.IsNullOrWhiteSpace(references) && !string.IsNullOrWhiteSpace(quote))
        {
            return $"{cleanReason} {references}: \"{quote}\"";
        }

        if (!string.IsNullOrWhiteSpace(references))
        {
            return $"{cleanReason} {references}.";
        }

        return $"{cleanReason} Catechism Quote: \"{quote}\"";
    }

    private static bool ContainsCatechismCitation(string text) =>
        text.Contains("CCC ", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("Catechism", StringComparison.OrdinalIgnoreCase);

    private static (string? references, string? quote) ExtractCatechismMetadata(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return (null, null);
        }

        static string? Extract(string source, string label)
        {
            var match = Regex.Match(
                source,
                $"{Regex.Escape(label)}\\s*:\\s*(.+?)(?=(?:\\r?\\n[A-Z][A-Za-z ]+\\s*:)|$)",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            return match.Success ? Normalize(match.Groups[1].Value) : null;
        }

        var references = Extract(description, "CCC References");
        var quote = Extract(description, "Catechism Quote");

        return (references, quote);
    }

    private static string Normalize(string text) =>
        Regex.Replace(text, "\\s+", " ").Trim().Trim('"');
}
