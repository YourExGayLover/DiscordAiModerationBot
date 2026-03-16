using System.Diagnostics;
using DiscordAiModeration.Core.Interfaces;
using DiscordAiModeration.Core.Models;
using DiscordAiModeration.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscordAiModeration.Infrastructure.Services;

public sealed class AiModerationService : IAiModerationService
{
    private readonly AiProviderOptions _options;
    private readonly OpenAiModerationService _openAiService;
    private readonly OllamaModerationService _ollamaService;
    private readonly ILogger<AiModerationService> _logger;

    public AiModerationService(
        IOptions<AiProviderOptions> options,
        OpenAiModerationService openAiService,
        OllamaModerationService ollamaService,
        ILogger<AiModerationService> logger)
    {
        _options = options.Value;
        _openAiService = openAiService;
        _ollamaService = ollamaService;
        _logger = logger;
    }

    public async Task<AiDecision> EvaluateAsync(
        ModerationRequest request,
        GuildSettings settings,
        IReadOnlyList<RuleRecord> rules,
        IReadOnlyList<FeedbackExample> examples,
        CancellationToken cancellationToken = default)
    {
        var provider = (_options.Provider ?? "openai").Trim().ToLowerInvariant();
        var traceId = BuildTraceId(request);
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "AI moderation starting. TraceId={TraceId} Provider={Provider} GuildId={GuildId} ChannelId={ChannelId} MessageId={MessageId} UserId={UserId} Rules={RuleCount} Examples={ExampleCount} Threshold={Threshold}",
            traceId,
            provider,
            request.GuildId,
            request.ChannelId,
            request.MessageId,
            request.UserId,
            rules.Count,
            examples.Count,
            settings.ConfidenceThreshold);

        AiDecision decision = provider switch
        {
            "openai" => await _openAiService.EvaluateAsync(request, settings, rules, examples, cancellationToken),
            "ollama" or "llama" => await _ollamaService.EvaluateAsync(request, settings, rules, examples, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported AI provider: {_options.Provider}. Use 'openai' or 'ollama'.")
        };

        stopwatch.Stop();

        _logger.LogInformation(
            "AI moderation finished. TraceId={TraceId} Provider={Provider} MessageId={MessageId} ShouldAlert={ShouldAlert} RuleName={RuleName} Confidence={Confidence} DurationMs={DurationMs} Reason={Reason}",
            traceId,
            provider,
            request.MessageId,
            decision.ShouldAlert,
            decision.RuleName,
            decision.Confidence,
            stopwatch.ElapsedMilliseconds,
            decision.Reason);

        return decision;
    }

    private static string BuildTraceId(ModerationRequest request)
    {
        return $"g{request.GuildId}-m{request.MessageId}";
    }
}
