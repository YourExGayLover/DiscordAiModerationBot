using DiscordAiModeration.Core.Models;

namespace DiscordAiModeration.Infrastructure.Services;

public static class HeresySourceCatalog
{
    public static IReadOnlyList<string> GetSourcesForRule(string? ruleName)
    {
        if (string.IsNullOrWhiteSpace(ruleName))
        {
            return Array.Empty<string>();
        }

        var normalized = ruleName.Trim().ToLowerInvariant();

        return normalized switch
        {
            var n when n.Contains("euchar") || n.Contains("real presence") =>
                ["CCC 1374", "John 6:51-58", "Ignatius of Antioch, Smyrnaeans 7"],

            var n when n.Contains("bapt") =>
                ["CCC 1213", "CCC 1257", "John 3:5", "Titus 3:5"],

            var n when n.Contains("confession") || n.Contains("absolution") || n.Contains("reconciliation") =>
                ["CCC 1461", "John 20:21-23", "2 Corinthians 5:18"],

            var n when n.Contains("mary") || n.Contains("mother of god") =>
                ["CCC 495", "Luke 1:43", "Council of Ephesus (431)"],

            var n when n.Contains("saint") || n.Contains("intercession") =>
                ["CCC 956", "Revelation 5:8", "Hebrews 12:1"],

            var n when n.Contains("trinity") =>
                ["CCC 253", "Matthew 28:19", "2 Corinthians 13:14"],

            var n when n.Contains("divinity") || n.Contains("christ") || n.Contains("incarnation") =>
                ["CCC 464", "John 1:1-14", "Colossians 2:9", "Ignatius of Antioch, Ephesians 7"],

            var n when n.Contains("church") || n.Contains("magister") || n.Contains("apostolic succession") =>
                ["CCC 85", "CCC 861", "1 Timothy 3:15", "2 Thessalonians 2:15"],

            var n when n.Contains("justification") || n.Contains("faith alone") =>
                ["CCC 1987", "CCC 2010", "James 2:24", "Romans 2:6-7"],

            var n when n.Contains("abortion") =>
                ["CCC 2270", "Jeremiah 1:5", "Psalm 139:13-16", "Didache 2"],

            var n when n.Contains("contraception") =>
                ["CCC 2370", "Genesis 1:28", "Humanae Vitae 14"],

            var n when n.Contains("heresy") || n.Contains("persistent catechism contradiction") =>
                ["CCC 2089", "Titus 3:10-11"],

            _ => ["CCC 2089"]
        };
    }

    public static IReadOnlyList<string> MergeSources(string? ruleName, IEnumerable<string>? aiSources)
    {
        var merged = new List<string>();

        foreach (var source in aiSources ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(source) && !merged.Contains(source, StringComparer.OrdinalIgnoreCase))
            {
                merged.Add(source.Trim());
            }
        }

        foreach (var source in GetSourcesForRule(ruleName))
        {
            if (!merged.Contains(source, StringComparer.OrdinalIgnoreCase))
            {
                merged.Add(source);
            }
        }

        return merged;
    }

    public static string BuildSourceBlock(string? ruleName, IEnumerable<string>? aiSources)
    {
        var sources = MergeSources(ruleName, aiSources);
        return sources.Count == 0
            ? string.Empty
            : string.Join('\n', sources.Select(x => $"• {x}"));
    }
}
