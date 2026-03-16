using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiscordAiModeration.Core.Models;

namespace DiscordAiModeration.Infrastructure.Services;

internal static class AiDecisionParser
{
    public static AiDecision ParseFromJson(string json)
    {
        var decision = JsonSerializer.Deserialize<AiDecisionDto>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (decision is null)
            return new AiDecision(false, string.Empty, 0, "AI returned no decision.");

        return new AiDecision(
            decision.ShouldAlert,
            decision.RuleName ?? string.Empty,
            Math.Clamp(decision.Confidence, 0, 100),
            decision.Reason ?? string.Empty);
    }

    public static string ExtractOpenAiOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
            return outputText.GetString() ?? "{}";

        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            var builder = new StringBuilder();
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                        builder.Append(text.GetString());
                }
            }

            if (builder.Length > 0)
                return builder.ToString();
        }

        return "{}";
    }

    private sealed class AiDecisionDto
    {
        [JsonPropertyName("shouldAlert")]
        public bool ShouldAlert { get; set; }

        [JsonPropertyName("ruleName")]
        public string? RuleName { get; set; }

        [JsonPropertyName("confidence")]
        public int Confidence { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }
}
