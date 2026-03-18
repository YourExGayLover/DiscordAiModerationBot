using System.Text.RegularExpressions;
using DiscordAiModeration.Core.Models;

namespace DiscordAiModeration.Bot.Services;

internal static partial class FeedbackLearningHelper
{
    internal sealed class FeedbackAdjustment
    {
        public bool SuppressAlert { get; init; }
        public int ConfidenceDelta { get; init; }
        public string? Notes { get; init; }
    }

    public static bool IsKnownRejectedDuplicate(
        string content,
        IReadOnlyList<FeedbackExample> examples)
    {
        if (string.IsNullOrWhiteSpace(content) || examples.Count == 0)
        {
            return false;
        }

        var normalizedTarget = Normalize(content);

        foreach (var example in examples)
        {
            if (!string.Equals(example.Outcome, "rejected", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normalizedExample = Normalize(example.MessageContent);
            if (string.IsNullOrWhiteSpace(normalizedExample))
            {
                continue;
            }

            if (normalizedTarget == normalizedExample)
            {
                return true;
            }

            var similarity = ComputeTokenSimilarity(normalizedTarget, normalizedExample);
            if (similarity >= 0.96)
            {
                return true;
            }
        }

        return false;
    }

    public static FeedbackAdjustment AdjustDecision(
        AiDecision decision,
        string content,
        IReadOnlyList<FeedbackExample> examples)
    {
        if (!decision.ShouldAlert || string.IsNullOrWhiteSpace(content) || examples.Count == 0)
        {
            return new FeedbackAdjustment();
        }

        var normalizedTarget = Normalize(content);
        var matchingRuleExamples = examples
            .Where(x => string.Equals(x.RuleName, decision.RuleName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingRuleExamples.Count == 0)
        {
            return new FeedbackAdjustment();
        }

        double bestRejected = 0;
        double bestApproved = 0;

        foreach (var example in matchingRuleExamples)
        {
            var normalizedExample = Normalize(example.MessageContent);
            if (string.IsNullOrWhiteSpace(normalizedExample))
            {
                continue;
            }

            var similarity = ComputeTokenSimilarity(normalizedTarget, normalizedExample);

            if (string.Equals(example.Outcome, "rejected", StringComparison.OrdinalIgnoreCase))
            {
                bestRejected = Math.Max(bestRejected, similarity);
            }
            else if (string.Equals(example.Outcome, "approved", StringComparison.OrdinalIgnoreCase))
            {
                bestApproved = Math.Max(bestApproved, similarity);
            }
        }

        if (bestRejected >= 0.97 && bestRejected > bestApproved)
        {
            return new FeedbackAdjustment
            {
                SuppressAlert = true,
                Notes = $"Suppressed due to near-duplicate rejected feedback example (similarity {bestRejected:P0})."
            };
        }

        var delta = 0;
        var notes = new List<string>();

        if (bestRejected >= 0.85 && bestRejected > bestApproved)
        {
            delta += bestRejected >= 0.93 ? -25 : -15;
            notes.Add($"penalized by rejected history ({bestRejected:P0})");
        }

        if (bestApproved >= 0.85 && bestApproved >= bestRejected)
        {
            delta += bestApproved >= 0.93 ? 10 : 6;
            notes.Add($"boosted by approved history ({bestApproved:P0})");
        }

        return new FeedbackAdjustment
        {
            ConfidenceDelta = delta,
            Notes = notes.Count == 0 ? null : string.Join("; ", notes)
        };
    }

    private static string Normalize(string value)
    {
        var lower = value.ToLowerInvariant();
        lower = UrlRegex().Replace(lower, " ");
        lower = MentionRegex().Replace(lower, " ");
        lower = NonWordRegex().Replace(lower, " ");
        lower = WhitespaceRegex().Replace(lower, " ").Trim();
        return lower;
    }

    private static double ComputeTokenSimilarity(string a, string b)
    {
        var aTokens = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var bTokens = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        if (aTokens.Count == 0 || bTokens.Count == 0)
        {
            return 0;
        }

        var intersection = aTokens.Intersect(bTokens).Count();
        var union = aTokens.Union(bTokens).Count();
        return union == 0 ? 0 : (double)intersection / union;
    }

    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"<@!?\d+>|<@&\d+>|<#\d+>", RegexOptions.IgnoreCase)]
    private static partial Regex MentionRegex();

    [GeneratedRegex(@"[^\p{L}\p{N}\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex NonWordRegex();

    [GeneratedRegex(@"\s+", RegexOptions.IgnoreCase)]
    private static partial Regex WhitespaceRegex();
}
