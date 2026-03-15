using DiscordAiModeration.Core.Models;

namespace DiscordAiModeration.Core.Interfaces;

public interface IAiModerationService
{
    Task<AiDecision> EvaluateAsync(
        ModerationRequest request,
        GuildSettings settings,
        IReadOnlyList<RuleRecord> rules,
        IReadOnlyList<FeedbackExample> examples,
        CancellationToken cancellationToken = default);
}
