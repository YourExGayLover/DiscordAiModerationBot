using System.Text.Json;
using DiscordAiModeration.Core.Models;

namespace DiscordAiModeration.Infrastructure.Services;

internal static class SharedPromptBuilder
{
    public static string BuildSystemPrompt() => """
You are a Discord moderation classifier.
You must evaluate one message against the server's rules.
Be conservative. Only alert when the message might reasonably violate a rule.
Return strict JSON only.
Schema:
{
  "shouldAlert": true|false,
  "ruleName": "Harassment",
  "confidence": 0-100,
  "reason": "Brief reason"
}
Rules:
- If the message clearly does not violate a rule, set shouldAlert to false and confidence low.
- Confidence must reflect uncertainty, not certainty theater.
- Pick exactly one best matching ruleName when shouldAlert is true.
- Use moderator feedback examples as guidance.
""";

    public static string BuildUserPrompt(
        ModerationRequest request,
        IReadOnlyList<RuleRecord> rules,
        IReadOnlyList<FeedbackExample> examples)
    {
        var rulesText = string.Join("\n\n", rules.Select((r, i) =>
        {
            var sampleExamples = DeserializeExamples(r.ExamplesJson);
            var exampleText = sampleExamples.Length == 0 ? "None" : string.Join(" | ", sampleExamples);
            return $"Rule {i + 1}: {r.Name}\nDescription: {r.Description}\nExamples: {exampleText}";
        }));

        var feedbackText = examples.Count == 0
            ? "No prior moderator feedback examples available."
            : string.Join("\n\n", examples.Select((e, i) =>
                $"Feedback Example {i + 1}\nRule: {e.RuleName}\nOutcome: {e.Outcome}\nMessage: {e.MessageContent}\nModerator notes: {e.Notes}"));

        return $"""
Server rules:
{rulesText}

Prior moderator feedback:
{feedbackText}

Message to evaluate:
Author mention: {request.Username}
Channel mention: {request.ChannelMention}
Content:
{request.Content}
""";
    }

    public static string[] DeserializeExamples(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();

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
