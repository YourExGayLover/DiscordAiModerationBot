using System;

namespace DiscordAiModeration.Bot.Services;

internal static class DebugDashboard
{
    private static readonly object Sync = new();

    public static void Queue(long guildId, long channelId, long messageId, long userId, string preview)
    {
        WriteBlock(
            ConsoleColor.Cyan,
            "QUEUE",
            ("Guild", guildId.ToString()),
            ("Channel", channelId.ToString()),
            ("Message", messageId.ToString()),
            ("User", userId.ToString()),
            ("Preview", preview));
    }

    public static void Processing(long guildId, long channelId, long messageId)
    {
        WriteBlock(
            ConsoleColor.Blue,
            "PROCESSING",
            ("Guild", guildId.ToString()),
            ("Channel", channelId.ToString()),
            ("Message", messageId.ToString()));
    }

    public static void Skipped(string reason, long messageId)
    {
        WriteBlock(
            ConsoleColor.DarkGray,
            "SKIPPED",
            ("Message", messageId.ToString()),
            ("Reason", reason));
    }

    public static void SendingToAi(long messageId, int ruleCount, int feedbackCount, int threshold)
    {
        WriteBlock(
            ConsoleColor.DarkCyan,
            "AI REQUEST",
            ("Message", messageId.ToString()),
            ("Rules", ruleCount.ToString()),
            ("Feedback", feedbackCount.ToString()),
            ("Threshold", $"{threshold}%"));
    }

    public static void FeedbackSuppressed(long messageId, string reason)
    {
        WriteBlock(
            ConsoleColor.Magenta,
            "SUPPRESSED",
            ("Message", messageId.ToString()),
            ("Reason", reason));
    }

    public static void ConfidenceAdjusted(long messageId, int originalConfidence, int adjustedConfidence, string notes)
    {
        WriteBlock(
            ConsoleColor.Yellow,
            "CONFIDENCE ADJUSTED",
            ("Message", messageId.ToString()),
            ("Original", $"{originalConfidence}%"),
            ("Adjusted", $"{adjustedConfidence}%"),
            ("Notes", notes));
    }

    public static void Decision(long messageId, bool shouldAlert, string? ruleName, int confidence, string? reason, long elapsedMs)
    {
        var color = shouldAlert ? ConsoleColor.Red : ConsoleColor.Green;
        WriteBlock(
            color,
            shouldAlert ? "AI ALERT" : "AI CLEAR",
            ("Message", messageId.ToString()),
            ("Rule", string.IsNullOrWhiteSpace(ruleName) ? "none" : ruleName),
            ("Confidence", $"{confidence}%"),
            ("Elapsed", $"{elapsedMs} ms"),
            ("Reason", string.IsNullOrWhiteSpace(reason) ? "No reason provided." : reason));
    }

    public static void BelowThreshold(long messageId, int confidence, int threshold)
    {
        WriteBlock(
            ConsoleColor.DarkYellow,
            "BELOW THRESHOLD",
            ("Message", messageId.ToString()),
            ("Confidence", $"{confidence}%"),
            ("Threshold", $"{threshold}%"));
    }

    public static void AlertSent(long alertId, long messageId, string? ruleName, int confidence, long alertChannelId, string messageLink)
    {
        WriteBlock(
            ConsoleColor.Red,
            "ALERT SENT",
            ("Alert", $"#{alertId}"),
            ("Message", messageId.ToString()),
            ("Rule", string.IsNullOrWhiteSpace(ruleName) ? "none" : ruleName),
            ("Confidence", $"{confidence}%"),
            ("Alert Channel", alertChannelId.ToString()),
            ("Link", messageLink));
    }

    public static void Error(long messageId, Exception ex)
    {
        WriteBlock(
            ConsoleColor.DarkRed,
            "ERROR",
            ("Message", messageId.ToString()),
            ("Type", ex.GetType().Name),
            ("Details", ex.Message));
    }

    private static void WriteBlock(ConsoleColor color, string title, params (string Label, string Value)[] rows)
    {
        lock (Sync)
        {
            var originalColor = Console.ForegroundColor;
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var width = Math.Min(Console.WindowWidth > 0 ? Console.WindowWidth - 1 : 119, 160);
            var border = new string('═', Math.Max(24, width));

            Console.ForegroundColor = color;
            Console.WriteLine();
            Console.WriteLine($"╔{border}");
            Console.WriteLine($"║ {timestamp} | {title}");
            Console.WriteLine($"╠{border}");

            foreach (var (label, value) in rows)
            {
                WriteWrappedLine(label, value, width);
            }

            Console.WriteLine($"╚{border}");
            Console.ForegroundColor = originalColor;
        }
    }

    private static void WriteWrappedLine(string label, string value, int width)
    {
        var safeValue = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
        var prefix = $"║ {label}: ";
        var contentWidth = Math.Max(20, width - prefix.Length - 1);

        if (safeValue.Length <= contentWidth)
        {
            Console.WriteLine(prefix + safeValue);
            return;
        }

        var remaining = safeValue;
        var isFirstLine = true;
        while (remaining.Length > 0)
        {
            var take = Math.Min(contentWidth, remaining.Length);
            var chunk = remaining[..take];
            remaining = remaining[take..];
            Console.WriteLine((isFirstLine ? prefix : "║ " + new string(' ', label.Length + 2)) + chunk);
            isFirstLine = false;
        }
    }
}
