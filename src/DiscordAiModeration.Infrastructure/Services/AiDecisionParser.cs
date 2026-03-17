using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiscordAiModeration.Core.Models;

namespace DiscordAiModeration.Infrastructure.Services;

internal static class AiDecisionParser
{
    public static AiDecision ParseFromJson(string json)
    {
        AiDecisionDto? decision;

        try
        {
            decision = JsonSerializer.Deserialize<AiDecisionDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return AiDecision.NoAlert("AI returned invalid JSON.");
        }

        if (decision is null)
        {
            return AiDecision.NoAlert("AI returned no decision.");
        }

        var ruleName = (decision.RuleName ?? string.Empty).Trim();
        var violatedRules = NormalizeList(decision.ViolatedRules, fallback: ruleName);
        var explanation = FirstNonEmpty(decision.Explanation, decision.Reason, "No explanation provided.");
        var reason = FirstNonEmpty(decision.Reason, decision.Explanation, explanation);
        var verdict = NormalizeVerdict(decision.Verdict, decision.ShouldAlert);
        var shouldAlert = decision.ShouldAlert || verdict is "heresy" or "violation" || violatedRules.Count > 0;
        var confidence = Math.Clamp(decision.Confidence, 0, 100);
        var sources = NormalizeList(decision.Sources);

        return new AiDecision(
            ShouldAlert: shouldAlert,
            Verdict: verdict,
            RuleName: ruleName,
            Confidence: confidence,
            Reason: reason,
            Explanation: explanation,
            ViolatedRules: violatedRules,
            Sources: sources);
    }

    public static string ExtractOpenAiOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString() ?? "{}";
        }

        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            var builder = new StringBuilder();

            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        builder.Append(text.GetString());
                    }
                }
            }

            if (builder.Length > 0)
            {
                return builder.ToString();
            }
        }

        return "{}";
    }

    private static IReadOnlyList<string> NormalizeList(IEnumerable<string>? values, string? fallback = null)
    {
        var results = new List<string>();

        foreach (var value in values ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                var trimmed = value.Trim();
                if (!results.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    results.Add(trimmed);
                }
            }
        }

        if (results.Count == 0 && !string.IsNullOrWhiteSpace(fallback))
        {
            results.Add(fallback.Trim());
        }

        return results;
    }

    private static string NormalizeVerdict(string? verdict, bool shouldAlert)
    {
        if (!string.IsNullOrWhiteSpace(verdict))
        {
            var normalized = verdict.Trim().ToLowerInvariant();
            return normalized switch
            {
                "heresy" => "heresy",
                "violation" => "violation",
                "clean" => "clean",
                "allowed" => "clean",
                _ => shouldAlert ? "violation" : "clean"
            };
        }

        return shouldAlert ? "violation" : "clean";
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private sealed class AiDecisionDto
    {
        [JsonPropertyName("shouldAlert")]
        public bool ShouldAlert { get; set; }

        [JsonPropertyName("verdict")]
        public string? Verdict { get; set; }

        [JsonPropertyName("ruleName")]
        public string? RuleName { get; set; }

        [JsonPropertyName("violated_rules")]
        public List<string>? ViolatedRules { get; set; }

        [JsonPropertyName("confidence")]
        public int Confidence { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("explanation")]
        public string? Explanation { get; set; }

        [JsonPropertyName("sources")]
        public List<string>? Sources { get; set; }
    }
}
