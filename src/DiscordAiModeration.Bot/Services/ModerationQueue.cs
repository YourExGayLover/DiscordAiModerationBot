using System.Diagnostics;
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
            _logger.LogInformation(
                "Skipping message {MessageId}: AI moderation disabled for guild {GuildId}.",
                request.MessageId,
                request.GuildId);
            return;
        }

        if (settings.AlertChannelId is null)
        {
            _logger.LogWarning(
                "Skipping message {MessageId}: no alert channel configured for guild {GuildId}.",
                request.MessageId,
                request.GuildId);
            return;
        }

        var rules = await _database.ListRulesAsync(request.GuildId, cancellationToken);
        if (rules.Count == 0)
        {
            _logger.LogInformation(
                "Skipping message {MessageId}: no moderation rules configured for guild {GuildId}.",
                request.MessageId,
                request.GuildId);
            return;
        }

        var feedbackExamples = await _database.GetFeedbackExamplesAsync(request.GuildId, 12, cancellationToken);

        _logger.LogInformation(
            "Sending message {MessageId} to AI ProviderRules={RuleCount} FeedbackExamples={FeedbackCount} Threshold={Threshold}",
            request.MessageId,
            rules.Count,
            feedbackExamples.Count,
            settings.ConfidenceThreshold);

        var decision = await _aiModerationService.EvaluateAsync(
            request,
            settings,
            rules,
            feedbackExamples,
            cancellationToken);

        _logger.LogInformation(
            "AI decision for message {MessageId}: ShouldAlert={ShouldAlert} Rule={RuleName} Confidence={Confidence} Reason=\"{Reason}\" ElapsedMs={ElapsedMs}",
            request.MessageId,
            decision.ShouldAlert,
            decision.RuleName,
            decision.Confidence,
            HttpLoggingHelper.Truncate(decision.Reason, 300),
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

        var finalReason = BuildReasonWithCatechismSupport(decision.Reason, decision.RuleName, rules);

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
            .WithTitle("Moderator Review Needed")
            .WithColor(Color.Orange)
            .AddField("Alert ID", $"#{alertId}", true)
            .AddField("User", request.Username, true)
            .AddField("Message Link", messageLink, false)
            .AddField("Matched Rule", decision.RuleName, true)
            .AddField("Confidence", $"{decision.Confidence}%", true)
            .AddField("Reason", string.IsNullOrWhiteSpace(finalReason) ? "No reason provided." : finalReason)
            .AddField("Message", $"```{TrimForCodeBlock(request.Content, 900)}```")
            .WithCurrentTimestamp()
            .Build();

        var components = BuildReviewComponents(alertId);

        await alertChannel.SendMessageAsync(
            text: pingText,
            embed: embed,
            components: components);

        _logger.LogInformation(
            "Alert sent AlertId={AlertId} MessageId={MessageId} Rule={RuleName} Confidence={Confidence} AlertChannelId={AlertChannelId} MessageLink={MessageLink}",
            alertId,
            request.MessageId,
            decision.RuleName,
            decision.Confidence,
            settings.AlertChannelId.Value,
            messageLink);
    }

    private static MessageComponent BuildReviewComponents(long alertId)
    {
        return new ComponentBuilder()
            .WithButton(
                label: "Approve",
                customId: $"review:approve:{alertId}",
                style: ButtonStyle.Success,
                emote: new Emoji("✅"))
            .WithButton(
                label: "Dismiss",
                customId: $"review:dismiss:{alertId}",
                style: ButtonStyle.Secondary,
                emote: new Emoji("🗑️"))
            .Build();
    }

    private static string BuildDiscordMessageLink(long guildId, long channelId, long messageId)
        => $"https://discord.com/channels/{guildId}/{channelId}/{messageId}";

    private static string TrimForCodeBlock(string content, int maxLength)
    {
        content = content.Replace("```", "'''");
        return content.Length <= maxLength ? content : content[..maxLength] + "...";
    }

    private static string BuildReasonWithCatechismSupport(
        string? reason,
        string? ruleName,
        IReadOnlyList<RuleRecord> rules)
    {
        var cleanedReason = string.IsNullOrWhiteSpace(reason)
            ? "Likely violation of Catholic moderation rules."
            : reason.Trim();

        if (string.IsNullOrWhiteSpace(ruleName))
        {
            return cleanedReason;
        }

        if (ContainsCatechismSupport(cleanedReason))
        {
            return cleanedReason;
        }

        var support = TryExtractCatechismSupportFromRules(ruleName, rules);
        if (string.IsNullOrWhiteSpace(support))
        {
            support = TryGetBuiltInCatechismSupport(ruleName);
        }

        if (string.IsNullOrWhiteSpace(support))
        {
            return cleanedReason;
        }

        return $"{cleanedReason}\n\n{support}";
    }

    private static string? TryExtractCatechismSupportFromRules(
        string ruleName,
        IReadOnlyList<RuleRecord> rules)
    {
        foreach (var rule in rules)
        {
            if (!string.Equals(rule.Name, ruleName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var description = rule.Description;
            if (string.IsNullOrWhiteSpace(description))
            {
                return null;
            }

            var quote = ExtractMetadataValue(description, "Catechism Quote:");
            var refs = ExtractMetadataValue(description, "CCC References:");

            if (!string.IsNullOrWhiteSpace(quote) && !string.IsNullOrWhiteSpace(refs))
            {
                return $"Catechism: \"{quote.Trim()}\" (CCC {refs.Trim()})";
            }

            if (!string.IsNullOrWhiteSpace(quote))
            {
                return $"Catechism: \"{quote.Trim()}\"";
            }

            if (!string.IsNullOrWhiteSpace(refs))
            {
                return $"Catechism: see CCC {refs.Trim()}.";
            }

            return null;
        }

        return null;
    }

    private static bool ContainsCatechismSupport(string reason)
        => reason.Contains("CCC", StringComparison.OrdinalIgnoreCase)
        || reason.Contains("Catechism", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractMetadataValue(string text, string label)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            if (line.TrimStart().StartsWith(label, StringComparison.OrdinalIgnoreCase))
            {
                return line[(line.IndexOf(':') + 1)..].Trim();
            }
        }

        return null;
    }

    private static string? TryGetBuiltInCatechismSupport(string ruleName)
    {
        var normalized = ruleName.Trim().ToLowerInvariant();

        return normalized switch
        {
            var n when n.Contains("trinity") || n.Contains("christ's divinity") || n.Contains("christs divinity")
                => "Catechism: \"The Trinity is One. We do not confess three Gods, but one God in three persons.\" (CCC 253)",

            var n when n.Contains("incarnation") || n.Contains("resurrection") || n.Contains("creedal")
                => "Catechism: \"The Resurrection of Jesus is the crowning truth of our faith in Christ.\" (CCC 638)",

            var n when n.Contains("real presence") || n.Contains("eucharist")
                => "Catechism: \"In the most blessed sacrament of the Eucharist the body and blood, together with the soul and divinity, of our Lord Jesus Christ... is truly, really, and substantially contained.\" (CCC 1374)",

            var n when n.Contains("baptism")
                => "Catechism: \"Holy Baptism is the basis of the whole Christian life... Through Baptism we are freed from sin and reborn as sons of God.\" (CCC 1213)",

            var n when n.Contains("confession") || n.Contains("absolution")
                => "Catechism: \"Since Christ entrusted to his apostles the ministry of reconciliation, bishops who are their successors, and priests, the bishops' collaborators, continue to exercise this ministry.\" (CCC 1461)",

            var n when n.Contains("teaching authority") || n.Contains("apostolic succession")
                => "Catechism: \"The task of giving an authentic interpretation of the Word of God... has been entrusted to the living teaching office of the Church alone.\" (CCC 85)",

            var n when n.Contains("anti-catholic slander")
                => "Catechism: \"Idolatry not only refers to false pagan worship. It remains a constant temptation to faith.\" (CCC 2113)",

            var n when n.Contains("leaving the church") || n.Contains("rejecting the sacraments")
                => "Catechism: \"The Church affirms that for believers the sacraments of the New Covenant are necessary for salvation.\" (CCC 1129)",

            var n when n.Contains("persistent catechism contradiction") || n.Contains("heresy")
                => "Catechism: \"Heresy is the obstinate post-baptismal denial of some truth which must be believed with divine and catholic faith.\" (CCC 2089)",

            var n when n.Contains("abortion")
                => "Catechism: \"Human life must be respected and protected absolutely from the moment of conception.\" (CCC 2270) \"Since the first century the Church has affirmed the moral evil of every procured abortion.\" (CCC 2271)",

            var n when n.Contains("contraception")
                => "Catechism: \"Every action which... proposes, whether as an end or as a means, to render procreation impossible is intrinsically evil.\" (CCC 2370)",

            var n when n.Contains("porn") || n.Contains("lewd")
                => "Catechism: \"Pornography... does grave injury to the dignity of its participants... It is a grave offense.\" (CCC 2354)",

            var n when n.Contains("sex outside marriage") || n.Contains("fornication")
                => "Catechism: \"Fornication is carnal union between an unmarried man and an unmarried woman. It is gravely contrary to the dignity of persons.\" (CCC 2353)",

            var n when n.Contains("adultery")
                => "Catechism: \"Adultery refers to marital infidelity... It is an injustice.\" (CCC 2380)",

            var n when n.Contains("drug")
                => "Catechism: \"The use of drugs inflicts very grave damage on human health and life. Their use, except on strictly therapeutic grounds, is a grave offense.\" (CCC 2291)",

            var n when n.Contains("drunkenness") || n.Contains("alcohol")
                => "Catechism: \"The virtue of temperance disposes us to avoid every kind of excess: the abuse of food, alcohol, tobacco, or medicine.\" (CCC 2290)",

            _ => null
        };
    }
}
