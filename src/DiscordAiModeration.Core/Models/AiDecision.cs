namespace DiscordAiModeration.Core.Models;

public sealed record AiDecision(
    bool ShouldAlert,
    string Verdict,
    string RuleName,
    int Confidence,
    string Reason,
    string Explanation,
    IReadOnlyList<string> ViolatedRules,
    IReadOnlyList<string> Sources)
{
    public static AiDecision NoAlert(string reason = "No violation detected.") =>
        new(false, "clean", string.Empty, 0, reason, reason, Array.Empty<string>(), Array.Empty<string>());
}
