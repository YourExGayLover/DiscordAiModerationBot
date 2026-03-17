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
        IReadOnlyList<FeedbackExampleRecord> examples) =>
        settings.UseSimplePrompts
            ? BuildSimpleUserPrompt(request, rules)
            : BuildDetailedUserPrompt(request, rules, examples);

    private static string BuildDetailedSystemPrompt() => """
You are a Discord moderation classifier.
You must evaluate one message against the server's rules. Be conservative. Only alert when the message itself is promoting, asserting, or urging a likely violation.
Respect any explicit exemption rules such as good-faith discussion, quotation for analysis, requests for clarification, or catechetical questions.
Return strict JSON only.

Schema:
{
  "shouldAlert": true|false,
  "ruleName": "Exact rule name or empty string",
  "confidence": 0-100,
  "reason": "Brief reason. When shouldAlert=true and the matched rule includes a Catechism quote or CCC reference, include one short CCC citation in the reason."
}

Rules:
- If the message clearly does not violate a rule, set shouldAlert to false and confidence low.
- Confidence must reflect uncertainty, not certainty theater.
- Pick exactly one best matching ruleName when shouldAlert is true.
- Do not punish curiosity, imperfect wording, or comparative theological discussion unless the user is clearly teaching against the provided rules.
- Use moderator feedback examples as guidance.
- When shouldAlert=true and the matched rule description includes "Catechism Quote:" or "CCC References:", append one short citation in this format:
  Example: Promotes rejection of the Real Presence. CCC 1374: "In the most blessed sacrament of the Eucharist..."
- Keep the reason compact and avoid multi-paragraph output.
""";

    private static string BuildSimpleSystemPrompt() => """
You are a Discord moderation classifier.
Check one message against the provided server rules. Be conservative.
Do not alert on sincere questions or quoted views for discussion when an exemption rule applies.

Return strict JSON only using this schema:
{
  "shouldAlert": true|false,
  "ruleName": "Exact matching rule name or empty string",
  "confidence": 0-100,
  "reason": "Short reason. If shouldAlert=true and the matching rule includes a Catechism quote or CCC reference, include one short CCC citation."
}

Keep the reason short. Pick one best rule only.
""";

    private static string BuildDetailedUserPrompt(
        ModerationRequest request,
        IReadOnlyList<RuleRecord> rules,
        IReadOnlyList<FeedbackExampleRecord> examples)
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

    private static string BuildSimpleUserPrompt(
        ModerationRequest request,
        IReadOnlyList<RuleRecord> rules)
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
