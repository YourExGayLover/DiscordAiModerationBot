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
        IReadOnlyList<RuleRecord> rules,
        IReadOnlyList<FeedbackExample> examples) =>
        settings.UseSimplePrompts
            ? BuildSimpleUserPrompt(request, rules)
            : BuildDetailedUserPrompt(request, rules, examples);

    private static string BuildDetailedSystemPrompt() =>
        """
        You are a Catholic Discord moderation classifier.
        Evaluate exactly one message against the provided server rules.
        Be conservative. Do not alert on sincere questions, quoting for analysis, requests for clarification, or neutral discussion when an exemption rule applies.
        Return strict JSON only.

        Required JSON schema:
        {
          "shouldAlert": true|false,
          "verdict": "heresy" | "violation" | "clean",
          "ruleName": "Exact rule name or empty string",
          "violated_rules": ["Exact rule name"],
          "confidence": 0-100,
          "reason": "One short summary sentence",
          "explanation": "1-3 short sentences explaining why the message matches the rule",
          "sources": ["CCC 1374", "John 6:51-58"]
        }

        Rules:
        - Pick exactly one best matching ruleName when shouldAlert is true.
        - violated_rules should usually contain one item matching ruleName.
        - Use verdict="heresy" when the message positively teaches or promotes doctrinal denial against a Catholic dogma or definitive teaching.
        - Use verdict="violation" for other rule breaks that are not doctrinal heresy.
        - Use verdict="clean" when the message should not alert.
        - Confidence must reflect uncertainty honestly.
        - explanation must explain why the message violates the rule, not merely restate the rule name.
        - sources must only include citations grounded in the supplied rule text or universally standard Catholic references clearly relevant to the detected issue.
        - Do not invent quotations.
        - If the message is clean, return an empty ruleName, empty violated_rules, and empty sources.
        """;

    private static string BuildSimpleSystemPrompt() =>
        """
        You are a Catholic Discord moderation classifier.
        Check one message against the provided server rules.
        Return strict JSON only.

        Schema:
        {
          "shouldAlert": true|false,
          "verdict": "heresy" | "violation" | "clean",
          "ruleName": "Exact rule name or empty string",
          "violated_rules": ["Exact rule name"],
          "confidence": 0-100,
          "reason": "Short summary",
          "explanation": "Short why statement",
          "sources": ["CCC or Scripture references"]
        }

        Do not alert on sincere questions when an exemption applies.
        Pick one best rule only.
        """;

    private static string BuildDetailedUserPrompt(
        ModerationRequest request,
        IReadOnlyList<RuleRecord> rules,
        IReadOnlyList<FeedbackExample> examples)
    {
        var rulesText = string.Join(
            "\n\n",
            rules.Select((r, i) =>
            {
                var sampleExamples = DeserializeExamples(r.ExamplesJson);
                var exampleText = sampleExamples.Length == 0 ? "None" : string.Join(" | ", sampleExamples);
                return $"Rule {i + 1}: {r.Name}\nDescription: {r.Description}\nExamples: {exampleText}";
            }));

        var feedbackText = examples.Count == 0
            ? "No prior moderator feedback examples available."
            : string.Join(
                "\n\n",
                examples.Select((e, i) =>
                    $"Feedback Example {i + 1}\nRule: {e.RuleName}\nOutcome: {e.Outcome}\nMessage: {e.MessageContent}\nModerator notes: {e.Notes}"));

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

    private static string BuildSimpleUserPrompt(ModerationRequest request, IReadOnlyList<RuleRecord> rules)
    {
        var rulesText = string.Join(
            "\n",
            rules.Select((r, i) => $"{i + 1}. {r.Name}: {r.Description}"));

        return $"""
        Rules:
        {rulesText}

        Message:
        {request.Content}
        """;
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
