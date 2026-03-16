using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DiscordAiModeration.Core.Models;
using DiscordAiModeration.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscordAiModeration.Infrastructure.Services;

public sealed class OpenAiModerationService
{
    private readonly HttpClient _httpClient;
    private readonly AiProviderOptions _options;
    private readonly ILogger<OpenAiModerationService> _logger;

    public OpenAiModerationService(HttpClient httpClient, IOptions<AiProviderOptions> options, ILogger<OpenAiModerationService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AiDecision> EvaluateAsync(
        ModerationRequest request,
        IReadOnlyList<RuleRecord> rules,
        IReadOnlyList<FeedbackExample> examples,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.OpenAiApiKey))
            throw new InvalidOperationException("AI_PROVIDER is set to openai, but OPENAI_API_KEY is missing.");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.OpenAiApiKey);

        var payload = new
        {
            model = _options.OpenAiModel,
            input = new object[]
            {
                new
                {
                    role = "system",
                    content = new object[]
                    {
                        new { type = "input_text", text = SharedPromptBuilder.BuildSystemPrompt() }
                    }
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = SharedPromptBuilder.BuildUserPrompt(request, rules, examples) }
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

        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

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
            var outputText = AiDecisionParser.ExtractOpenAiOutputText(doc.RootElement);
            return AiDecisionParser.ParseFromJson(outputText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse OpenAI response. Raw response: {Body}", body);
            return new AiDecision(false, string.Empty, 0, "AI parsing failed.");
        }
    }
}
