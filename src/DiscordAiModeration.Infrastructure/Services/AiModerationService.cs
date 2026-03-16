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

        _logger.LogDebug("Evaluating moderation request with provider {Provider}", provider);

        return provider switch
        {
            "openai" => await _openAiService.EvaluateAsync(request, rules, examples, cancellationToken),
            "ollama" or "llama" => await _ollamaService.EvaluateAsync(request, rules, examples, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported AI provider: {_options.Provider}. Use 'openai' or 'ollama'.")
        };
    }
}
