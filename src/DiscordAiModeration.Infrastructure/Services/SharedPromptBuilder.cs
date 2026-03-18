using System.Text;
using System.Text.Json;
using DiscordAiModeration.Core.Models;

namespace DiscordAiModeration.Infrastructure.Services;

internal static class SharedPromptBuilder
{
    public static string BuildSystemPrompt(GuildSettings settings) =>
        settings.UseSimplePrompts ? BuildSimpleSystemPrompt() : BuildDetailedSystemPrompt();

    public static string BuildUserPrompt(
        ModerationRequest request,
        GuildSettings settings,
        IReadOnlyList<object> rules,
        IReadOnlyList<object> examples) =>
        settings.UseSimplePrompts
            ? BuildSimpleUserPrompt(request, rules, examples)
            : BuildDetailedUserPrompt(request, rules, examples);

    private static string BuildDetailedSystemPrompt() =>
        """
        You are a Discord moderation classifier.
        Evaluate the target message using the provided server rules and the recent channel context.
        Be conservative. Only alert when the target message itself is promoting, asserting, or urging a likely violation.
        Do not alert merely because forbidden language appears in context or because the target message is discussing, quoting, asking about, or refuting a bad claim.
        Return strict JSON only.

        Schema:
        {
          "shouldAlert": true|false,
          "ruleName": "Exact rule name or empty string",
          "confidence": 0-100,
          "reason": "Brief reason"
        }

        Rules:
        - Judge the target message, not the surrounding messages.
        - Use context to determine whether the target message is a quote, summary, rebuttal, sincere question, sarcasm, correction, or endorsement.
        - If the target message is reporting someone else's belief, quoting for analysis, or refuting a false doctrine, do not alert unless the user is also clearly endorsing it.
        - Treat questions, requests for clarification, catechetical discussion, and comparative theology conservatively.
        - Distinguish assertion from quotation. Example: "Protestants say the Eucharist is symbolic" is normally a report, not an endorsement.
        - Distinguish refutation from promotion. Example: "That is wrong because Christ said..." should not be flagged as heresy.
        - Confidence must reflect uncertainty, not certainty theater.
        - Pick exactly one best matching ruleName when shouldAlert is true.
        - Use moderator feedback examples as guidance.
        - When shouldAlert is true, the reason should be 1-3 short sentences and mention the contextual factor when relevant.
        - If the matched rule description includes a line beginning with "Catechism Quote:" or "CCC References:", include a short catechism quotation or citation in the reason.
        - Do not invent a quote that is not supplied in the rule text. If no catechism quote is supplied, keep the reason concise and factual.
        """;

    private static string BuildSimpleSystemPrompt() =>
        """
        You are a Discord moderation classifier.
        Check the target message against the provided server rules using the recent channel context.
        Be conservative.
        Do not alert on sincere questions, quotations for discussion, or messages that are clearly rebutting or describing a view rather than teaching it.
        Use context to decide whether the message is a quote, question, rebuttal, sarcasm, or assertion.
        Return strict JSON only using this schema:
        { "shouldAlert": true|false, "ruleName": "Exact matching rule name or empty string", "confidence": 0-100, "reason": "Short reason" }
        Keep the reason short. Pick one best rule only.
        If the matched rule includes a catechism quote or CCC reference, include it in the reason. Do not invent a quote that is not present in the rule text.
        """;

    private static string BuildDetailedUserPrompt(
        ModerationRequest request,
        IReadOnlyList<object> rules,
        IReadOnlyList<object> examples)
    {
        var rulesText = string.Join(
            "\n\n",
            rules.Select((r, i) =>
            {
                var examplesJson = GetStringProperty(r, "ExamplesJson");
                var sampleExamples = DeserializeExamples(examplesJson);
                var exampleText = sampleExamples.Length == 0 ? "None" : string.Join(" | ", sampleExamples);
                var ruleName = GetStringProperty(r, "Name");
                var description = GetStringProperty(r, "Description");
                return $"Rule {i + 1}: {ruleName}\nDescription: {description}\nExamples: {exampleText}";
            }));

        var feedbackText = examples.Count == 0
            ? "No prior moderator feedback examples available."
            : string.Join(
                "\n\n",
                examples.Select((e, i) =>
                {
                    var ruleName = GetStringProperty(e, "RuleName");
                    var outcome = GetStringProperty(e, "Outcome");
                    var messageContent = GetStringProperty(e, "MessageContent");
                    var reason = GetStringProperty(e, "Reason");
                    var notes = GetStringProperty(e, "Notes");
                    return $"Feedback Example {i + 1}\nRule: {ruleName}\nOutcome: {outcome}\nMessage: {messageContent}\nModerator reason: {reason}\nModerator notes: {notes}";
                }));

        return $"""
        Server rules:
        {rulesText}

        Prior moderator feedback:
        {feedbackText}

        Target message to evaluate:
        Author mention: {request.Username}
        Channel mention: {request.ChannelMention}
        Content: {request.Content}

        Recent channel context (oldest first, target message excluded):
        {FormatContext(request.RecentContext)}
        """;
    }

    private static string BuildSimpleUserPrompt(
        ModerationRequest request,
        IReadOnlyList<object> rules,
        IReadOnlyList<object> examples)
    {
        var rulesText = string.Join(
            "\n",
            rules.Select((r, i) =>
            {
                var ruleName = GetStringProperty(r, "Name");
                var description = GetStringProperty(r, "Description");
                return $"{i + 1}. {ruleName}: {description}";
            }));

        var feedbackText = examples.Count == 0
            ? "None"
            : string.Join(
                "\n",
                examples.Take(6).Select((e, i) =>
                {
                    var ruleName = GetStringProperty(e, "RuleName");
                    var outcome = GetStringProperty(e, "Outcome");
                    var messageContent = GetStringProperty(e, "MessageContent");
                    return $"{i + 1}. [{outcome}] Rule={ruleName} Message={messageContent}";
                }));

        return $"""
        Rules:
        {rulesText}

        Moderator feedback examples:
        {feedbackText}

        Target message:
        {request.Content}

        Recent context:
        {FormatContext(request.RecentContext)}
        """;
    }

    private static string FormatContext(IReadOnlyList<ModerationContextMessage> context)
    {
        if (context.Count == 0)
        {
            return "No recent context available.";
        }

        var builder = new StringBuilder();
        for (var i = 0; i < context.Count; i++)
        {
            var item = context[i];
            var speaker = string.IsNullOrWhiteSpace(item.AuthorDisplay) ? "Unknown user" : item.AuthorDisplay;
            var prefix = item.IsCurrentUser ? "same-user" : "other-user";
            builder.Append(i + 1)
                .Append(". [")
                .Append(prefix)
                .Append("] ")
                .Append(speaker)
                .Append(": ")
                .AppendLine(string.IsNullOrWhiteSpace(item.Content) ? "<no text>" : item.Content);
        }

        return builder.ToString().TrimEnd();
    }

    private static string GetStringProperty<T>(T source, string propertyName)
    {
        var property = source?.GetType().GetProperty(propertyName);
        var value = property?.GetValue(source)?.ToString();
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value;
    }

    public static string[] DeserializeExamples(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
