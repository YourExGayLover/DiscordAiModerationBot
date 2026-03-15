using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiscordAiModeration.Core.Interfaces;
using DiscordAiModeration.Core.Models;
using DiscordAiModeration.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscordAiModeration.Infrastructure.Services;

public sealed class AiModerationService : IAiModerationService
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;
    private readonly ILogger<AiModerationService> _logger;

    public AiModerationService(HttpClient httpClient, IOptions<OpenAiOptions> options, ILogger<AiModerationService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    public async Task<AiDecision> EvaluateAsync(
        ModerationRequest request,
        GuildSettings settings,
        IReadOnlyList<RuleRecord> rules,
        IReadOnlyList<FeedbackExample> examples,
        CancellationToken cancellationToken = default)
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

        var systemPrompt = """
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

        var userPrompt = $"""
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

        var payload = new
        {
            model = _options.Model,
            input = new object[]
            {
                new
                {
                    role = "system",
                    content = new object[]
                    {
                        new { type = "input_text", text = systemPrompt }
                    }
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = userPrompt }
                    }
                }
            },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "moderation_decision",
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        properties = new
                        {
                            shouldAlert = new { type = "boolean" },
                            ruleName = new { type = "string" },
                            confidence = new { type = "integer", minimum = 0, maximum = 100 },
                            reason = new { type = "string" }
                        },
                        required = new[] { "shouldAlert", "ruleName", "confidence", "reason" }
                    }
                }
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("OpenAI moderation request failed. Status={StatusCode} Body={Body}", response.StatusCode, body);
            return new AiDecision(false, string.Empty, 0, "AI request failed.");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var outputText = ExtractOutputText(doc.RootElement);
            var decision = JsonSerializer.Deserialize<AiDecisionDto>(outputText, new JsonSerializerOptions
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse OpenAI response. Raw response: {Body}", body);
            return new AiDecision(false, string.Empty, 0, "AI parsing failed.");
        }
    }

    private static string[] DeserializeExamples(string? json)
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

    private static string ExtractOutputText(JsonElement root)
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
