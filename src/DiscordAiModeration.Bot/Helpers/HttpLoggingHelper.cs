using System;

namespace DiscordAiModeration.Bot.Helpers;

public static class HttpLoggingHelper
{
    public static string SafeMessagePreview(string? message, int maxLength = 120)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        var cleaned = message
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();

        return Truncate(cleaned, maxLength);
    }

    public static string Truncate(string? value, int maxLength = 120)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.Length <= maxLength)
            return value;

        return value.Substring(0, maxLength) + "...";
    }
}