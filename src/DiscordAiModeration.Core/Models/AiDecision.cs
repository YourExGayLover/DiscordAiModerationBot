namespace DiscordAiModeration.Core.Models;

public sealed record AiDecision(bool ShouldAlert, string RuleName, int Confidence, string Reason);
