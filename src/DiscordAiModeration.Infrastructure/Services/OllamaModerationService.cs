using System.Text;
using System.Text.Json;
using DiscordAiModeration.Core.Models;
using DiscordAiModeration.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscordAiModeration.Infrastructure.Services;

public sealed class OllamaModerationService
{
    private readonly HttpClient _httpClient;
    private readonly AiProviderOptions _options;
    private readonly ILogger<OllamaModerationService> _logger;

    public OllamaModerationService(HttpClient httpClient, IOptions<AiProviderOptions> options, ILogger<OllamaModerationService> logger)
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
        var baseUrl = string.IsNullOrWhiteSpace(_options.OllamaBaseUrl)
            ? "http://localhost:11434"
            : _options.OllamaBaseUrl.TrimEnd('/');

        var payload = new
        {
            model = _options.OllamaModel,
            stream = false,
            format = new
            {
                type = "object",
                properties = new
                {
                    shouldAlert = new { type = "boolean" },
                    ruleName = new { type = "string" },
                    confidence = new { type = "integer" },
                    reason = new { type = "string" }
                },
                required = new[] { "shouldAlert", "ruleName", "confidence", "reason" },
                additionalProperties = false
            },
            messages = new object[]
            {
                new { role = "system", content = SharedPromptBuilder.BuildSystemPrompt() },
                new { role = "user", content = SharedPromptBuilder.BuildUserPrompt(request, rules, examples) }
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/chat")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Ollama moderation request failed. Status={StatusCode} Body={Body}", response.StatusCode, body);
            return new AiDecision(false, string.Empty, 0, "AI request failed.");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var content = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "{}";
            return AiDecisionParser.ParseFromJson(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Ollama response. Raw response: {Body}", body);
            return new AiDecision(false, string.Empty, 0, "AI parsing failed.");
        }
    }
}
