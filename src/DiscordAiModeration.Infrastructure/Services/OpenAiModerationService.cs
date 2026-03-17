using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DiscordAiModeration.Core.Interfaces;
using DiscordAiModeration.Core.Models;
using DiscordAiModeration.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscordAiModeration.Infrastructure.Services;

public sealed class OpenAiModerationService : IAiModerationService
{
    private readonly HttpClient _httpClient;
    private readonly AiProviderOptions _options;
    private readonly ILogger<OpenAiModerationService> _logger;

    public OpenAiModerationService(
        HttpClient httpClient,
        IOptions<AiProviderOptions> options,
        ILogger<OpenAiModerationService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AiDecision> EvaluateAsync(
        ModerationRequest request,
        GuildSettings settings,
        IReadOnlyList<RuleRecord> rules,
        IReadOnlyList<FeedbackExample> examples,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.OpenAiApiKey))
        {
            throw new InvalidOperationException("AI_PROVIDER is set to openai, but OPENAI_API_KEY is missing.");
        }

        var traceId = BuildTraceId(request);
        var userPrompt = SharedPromptBuilder.BuildUserPrompt(request, settings, rules, examples);

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
                        new { type = "input_text", text = SharedPromptBuilder.BuildSystemPrompt(settings) }
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
                            verdict = new { type = "string" },
                            ruleName = new { type = "string" },
                            violated_rules = new
                            {
                                type = "array",
                                items = new { type = "string" }
                            },
                            confidence = new { type = "integer", minimum = 0, maximum = 100 },
                            reason = new { type = "string" },
                            explanation = new { type = "string" },
                            sources = new
                            {
                                type = "array",
                                items = new { type = "string" }
                            }
                        },
                        required = new[]
                        {
                            "shouldAlert",
                            "verdict",
                            "ruleName",
                            "violated_rules",
                            "confidence",
                            "reason",
                            "explanation",
                            "sources"
                        }
                    }
                }
            }
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        httpRequest.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

        _logger.LogInformation(
            "OpenAI request sending. TraceId={TraceId} Endpoint={Endpoint} Model={Model} MessageId={MessageId} PromptLength={PromptLength} RuleCount={RuleCount} ExampleCount={ExampleCount}",
            traceId,
            httpRequest.RequestUri,
            _options.OpenAiModel,
            request.MessageId,
            userPrompt.Length,
            rules.Count,
            examples.Count);

        var stopwatch = Stopwatch.StartNew();
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        stopwatch.Stop();

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        _logger.LogInformation(
            "OpenAI response received. TraceId={TraceId} StatusCode={StatusCode} DurationMs={DurationMs} BodyLength={BodyLength}",
            traceId,
            (int)response.StatusCode,
            stopwatch.ElapsedMilliseconds,
            body.Length);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "OpenAI moderation request failed. TraceId={TraceId} StatusCode={StatusCode} Body={Body}",
                traceId,
                response.StatusCode,
                body);

            return AiDecision.NoAlert("AI request failed.");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var outputText = AiDecisionParser.ExtractOpenAiOutputText(doc.RootElement);
            return AiDecisionParser.ParseFromJson(outputText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse OpenAI response. TraceId={TraceId} Raw response: {Body}", traceId, body);
            return AiDecision.NoAlert("AI parsing failed.");
        }
    }

    private static string BuildTraceId(ModerationRequest request) => $"g{request.GuildId}-m{request.MessageId}";
}
