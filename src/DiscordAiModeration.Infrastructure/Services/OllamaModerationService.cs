using System.Diagnostics;
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

    public OllamaModerationService(
        HttpClient httpClient,
        IOptions<AiProviderOptions> options,
        ILogger<OllamaModerationService> logger)
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

        var traceId = BuildTraceId(request);
        var userPrompt = SharedPromptBuilder.BuildUserPrompt(request, rules, examples);

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
                new { role = "user", content = userPrompt }
            }
        };

        var payloadJson = JsonSerializer.Serialize(payload);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/chat")
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
        };

        _logger.LogInformation(
            "Ollama request sending. TraceId={TraceId} Endpoint={Endpoint} Model={Model} MessageId={MessageId} PromptLength={PromptLength} RuleCount={RuleCount} ExampleCount={ExampleCount}",
            traceId,
            httpRequest.RequestUri,
            _options.OllamaModel,
            request.MessageId,
            userPrompt.Length,
            rules.Count,
            examples.Count);

        _logger.LogDebug("Ollama request payload. TraceId={TraceId} Payload={Payload}", traceId, payloadJson);

        var stopwatch = Stopwatch.StartNew();
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        stopwatch.Stop();

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        _logger.LogInformation(
            "Ollama response received. TraceId={TraceId} StatusCode={StatusCode} DurationMs={DurationMs} BodyLength={BodyLength}",
            traceId,
            (int)response.StatusCode,
            stopwatch.ElapsedMilliseconds,
            body.Length);

        _logger.LogDebug("Ollama raw response body. TraceId={TraceId} Body={Body}", traceId, body);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Ollama moderation request failed. TraceId={TraceId} StatusCode={StatusCode} Body={Body}",
                traceId,
                response.StatusCode,
                body);

            return new AiDecision(false, string.Empty, 0, "AI request failed.");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var content = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "{}";
            _logger.LogDebug("Ollama extracted output text. TraceId={TraceId} OutputText={OutputText}", traceId, content);
            return AiDecisionParser.ParseFromJson(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Ollama response. TraceId={TraceId} Raw response: {Body}", traceId, body);
            return new AiDecision(false, string.Empty, 0, "AI parsing failed.");
        }
    }

    private static string BuildTraceId(ModerationRequest request)
    {
        return $"g{request.GuildId}-m{request.MessageId}";
    }
}
