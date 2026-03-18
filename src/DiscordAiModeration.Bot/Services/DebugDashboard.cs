using System;

namespace DiscordAiModeration.Bot.Services;

internal static class DebugDashboard
{
    private static readonly object Sync = new();

    public static void Queue(long guildId, long channelId, long messageId, long userId, string preview)
    {
        WriteFlow(ConsoleColor.Cyan, messageId, "QUEUED");
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
        WriteFlow(ConsoleColor.Blue, messageId, "PROCESSING");
        WriteBlock(
            ConsoleColor.Blue,
            "PROCESSING",
            ("Guild", guildId.ToString()),
            ("Channel", channelId.ToString()),
            ("Message", messageId.ToString()));
    }

    public static void Skipped(string reason, long messageId)
    {
        WriteFlow(ConsoleColor.DarkGray, messageId, NormalizeSkippedState(reason));
        WriteBlock(
            ConsoleColor.DarkGray,
            "SKIPPED",
            ("Message", messageId.ToString()),
            ("Reason", reason));
    }

    public static void SendingToAi(long messageId, int ruleCount, int feedbackCount, int threshold)
    {
        WriteFlow(ConsoleColor.DarkCyan, messageId, "SENT_TO_AI");
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
        WriteFlow(ConsoleColor.Magenta, messageId, "SKIPPED_FALSE_POSITIVE_HISTORY");
        WriteBlock(
            ConsoleColor.Magenta,
            "SUPPRESSED",
            ("Message", messageId.ToString()),
            ("Reason", reason));
    }

    public static void ConfidenceAdjusted(long messageId, int originalConfidence, int adjustedConfidence, string notes)
    {
        WriteFlow(ConsoleColor.Yellow, messageId, "CONFIDENCE_ADJUSTED");
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
        WriteFlow(color, messageId, shouldAlert ? "AI_ALERT" : "AI_NO_ALERT");
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
        WriteFlow(ConsoleColor.DarkYellow, messageId, "BELOW_THRESHOLD");
        WriteBlock(
            ConsoleColor.DarkYellow,
            "BELOW THRESHOLD",
            ("Message", messageId.ToString()),
            ("Confidence", $"{confidence}%"),
            ("Threshold", $"{threshold}%"));
    }

    public static void AlertSent(long alertId, long messageId, string? ruleName, int confidence, long alertChannelId, string messageLink)
    {
        WriteFlow(ConsoleColor.Red, messageId, "ALERT_SENT");
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
        WriteFlow(ConsoleColor.DarkRed, messageId, "ERROR");
        WriteBlock(
            ConsoleColor.DarkRed,
            "ERROR",
            ("Message", messageId.ToString()),
            ("Type", ex.GetType().Name),
            ("Details", ex.Message));
    }

    private static void WriteFlow(ConsoleColor color, long messageId, string state)
    {
        lock (Sync)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            Console.WriteLine($"{timestamp} | {messageId} | {state}");
            Console.ForegroundColor = originalColor;
        }
    }

    private static string NormalizeSkippedState(string reason)
    {
        var text = (reason ?? string.Empty).Trim().ToLowerInvariant();

        if (text.Contains("no guild settings"))
        {
            return "SKIPPED_NO_SETTINGS";
        }

        if (text.Contains("ai moderation disabled"))
        {
            return "SKIPPED_AI_DISABLED";
        }

        if (text.Contains("no alert channel"))
        {
            return "SKIPPED_NO_ALERT_CHANNEL";
        }

        if (text.Contains("no moderation rules"))
        {
            return "SKIPPED_NO_RULES";
        }

        if (text.Contains("did not trigger an alert"))
        {
            return "AI_NO_ALERT";
        }

        if (text.Contains("could not be resolved"))
        {
            return "SKIPPED_ALERT_CHANNEL_UNRESOLVED";
        }

        return "SKIPPED";
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
