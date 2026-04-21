using System.Text.RegularExpressions;

namespace DiscordAiModeration.Viewer.Services;

public static class DiscordContentFormatter
{
    public static string Format(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var text = input;

        // Remove block quote markers
        text = Regex.Replace(text, @"^>\s?", "", RegexOptions.Multiline);

        // Bold **text** -> text
        text = Regex.Replace(text, @"\*\*(.*?)\*\*", "$1");

        // Italic *text* -> text
        text = Regex.Replace(text, @"\*(.*?)\*", "$1");

        // User mention <@123>
        text = Regex.Replace(text, @"<@!?(\d+)>", "@$1");

        // Role mention <@&123>
        text = Regex.Replace(text, @"<@&(\d+)>", "@role:$1");

        // Channel mention <#123>
        text = Regex.Replace(text, @"<#(\d+)>", "#$1");

        // Code blocks `text`
        text = Regex.Replace(text, @"`(.*?)`", "$1");

        // Discord timestamps <t:123456:R>
        text = Regex.Replace(text, @"<t:(\d+):R>", match =>
        {
            var unix = long.Parse(match.Groups[1].Value);
            var date = DateTimeOffset.FromUnixTimeSeconds(unix);
            var diff = DateTimeOffset.UtcNow - date;

            if (diff.TotalMinutes < 1) return "just now";
            if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes} min ago";
            if (diff.TotalDays < 1) return $"{(int)diff.TotalHours} hours ago";

            return $"{(int)diff.TotalDays} days ago";
        });

        return text.Trim();
    }
}