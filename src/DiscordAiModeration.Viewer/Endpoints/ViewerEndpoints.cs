using Discord;
using Discord.WebSocket;
using DiscordAiModeration.Viewer.Services;

namespace DiscordAiModeration.Viewer.Endpoints;

public static class ViewerEndpoints
{
    public static IEndpointRouteBuilder MapViewerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/status", (DiscordSocketClient client) => Results.Ok(new
        {
            isLoggedIn = client.LoginState == LoginState.LoggedIn,
            connectionState = client.ConnectionState.ToString(),
            guildCount = client.Guilds.Count,
            currentUser = client.CurrentUser?.Username
        }));

        app.MapGet("/api/guilds", (DiscordSocketClient client, DiscordViewerState state) =>
            Results.Ok(state.GetGuilds(client)));

        app.MapGet("/api/channels", (string? guildId, DiscordSocketClient client, DiscordViewerState state) =>
        {
            ulong? parsedGuildId = null;

            if (!string.IsNullOrWhiteSpace(guildId) && ulong.TryParse(guildId, out var parsed))
            {
                parsedGuildId = parsed;
            }

            return Results.Ok(state.GetChannels(client, parsedGuildId));
        });

        app.MapGet("/api/channels/{channelId}/messages", async (string channelId, string? beforeMessageId, DiscordViewerState state) =>
        {
            if (!ulong.TryParse(channelId, out var parsedChannelId))
            {
                return Results.BadRequest("Invalid channel id.");
            }

            ulong? parsedBeforeMessageId = null;
            if (!string.IsNullOrWhiteSpace(beforeMessageId) && ulong.TryParse(beforeMessageId, out var parsedBefore))
            {
                parsedBeforeMessageId = parsedBefore;
            }

            return Results.Ok(await state.GetMessagePageAsync(parsedChannelId, parsedBeforeMessageId));
        });

        app.MapGet("/api/voice", (string? guildId, DiscordSocketClient client, DiscordViewerState state) =>
        {
            ulong? parsedGuildId = null;

            if (!string.IsNullOrWhiteSpace(guildId) && ulong.TryParse(guildId, out var parsed))
            {
                parsedGuildId = parsed;
            }

            return Results.Ok(state.GetVoiceSnapshot(client, parsedGuildId));
        });

        app.MapGet("/api/profile", (string? guildId, string? userId, DiscordSocketClient client, DiscordViewerState state) =>
        {
            if (!ulong.TryParse(guildId, out var parsedGuildId))
            {
                return Results.BadRequest("Invalid guild id.");
            }

            if (!ulong.TryParse(userId, out var parsedUserId))
            {
                return Results.BadRequest("Invalid user id.");
            }

            var profile = state.GetUserProfile(client, parsedGuildId, parsedUserId);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        });

        return app;
    }
}
