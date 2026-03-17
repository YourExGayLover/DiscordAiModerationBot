using System.Text.Json;
using DiscordAiModeration.Core.Models;

namespace DiscordAiModeration.Infrastructure.Services;

internal static class SharedPromptBuilder
{
    public static string BuildSystemPrompt(GuildSettings settings)
        => settings.UseSimplePrompts ? BuildSimpleSystemPrompt() : BuildDetailedSystemPrompt();

    public static string BuildUserPrompt<TRule, TExample>(
        ModerationRequest request,
        GuildSettings settings,
        IReadOnlyList<TRule> rules,
        IReadOnlyList<TExample> examples)
        => settings.UseSimplePrompts
            ? BuildSimpleUserPrompt(request, rules)
            : BuildDetailedUserPrompt(request, rules, examples);

    private static string BuildDetailedSystemPrompt() =>
        """
        You are a Discord moderation classifier.
        You must evaluate one message against the server's rules.
        Be conservative. Only alert when the message itself is promoting, asserting, or urging a likely violation.
        Respect any explicit exemption rules such as good-faith discussion, quotation for analysis, requests for clarification, or catechetical questions.
        Return strict JSON only.

        Schema:
        {
          "shouldAlert": true|false,
          "ruleName": "Exact rule name or empty string",
          "confidence": 0-100,
          "reason": "Brief reason"
        }

        Rules:
        - If the message clearly does not violate a rule, set shouldAlert to false and confidence low.
        - Confidence must reflect uncertainty, not certainty theater.
        - Pick exactly one best matching ruleName when shouldAlert is true.
        - Do not punish curiosity, imperfect wording, or comparative theological discussion unless the user is clearly teaching against the provided rules.
        - Use moderator feedback examples as guidance.
        - When shouldAlert is true, the reason should be 1-3 short sentences.
        - If the matched rule description includes a line beginning with "Catechism Quote:" or "CCC References:", include a short catechism quotation or citation in the reason.
        - Do not invent a quote that is not supplied in the rule text. If no catechism quote is supplied, keep the reason concise and factual.
        """;

    private static string BuildSimpleSystemPrompt() =>
        """
        You are a Discord moderation classifier. Check one message against the provided server rules.
        Be conservative. Do not alert on sincere questions or quoted views for discussion when an exemption rule applies.

        Return strict JSON only using this schema:
        { "shouldAlert": true|false, "ruleName": "Exact matching rule name or empty string", "confidence": 0-100, "reason": "Short reason" }

        Keep the reason short.
        Pick one best rule only.
        If the matched rule includes a catechism quote or CCC reference, include it in the reason.
        Do not invent a quote that is not present in the rule text.
        """;

    private static string BuildDetailedUserPrompt<TRule, TExample>(
        ModerationRequest request,
        IReadOnlyList<TRule> rules,
        IReadOnlyList<TExample> examples)
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
                    var notes = GetStringProperty(e, "Notes");
                    return $"Feedback Example {i + 1}\nRule: {ruleName}\nOutcome: {outcome}\nMessage: {messageContent}\nModerator notes: {notes}";
                }));

        return $"""
        Server rules:
        {rulesText}

        Prior moderator feedback:
        {feedbackText}

        Message to evaluate:
        Author mention: {request.Username}
        Channel mention: {request.ChannelMention}
        Content: {request.Content}
        """;
    }

    private static string BuildSimpleUserPrompt<TRule>(
        ModerationRequest request,
        IReadOnlyList<TRule> rules)
    {
        var rulesText = string.Join(
            "\n",
            rules.Select((r, i) =>
            {
                var ruleName = GetStringProperty(r, "Name");
                var description = GetStringProperty(r, "Description");
                return $"{i + 1}. {ruleName}: {description}";
            }));

        return $"""
        Rules:
        {rulesText}

        Message:
        {request.Content}
        """;
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
