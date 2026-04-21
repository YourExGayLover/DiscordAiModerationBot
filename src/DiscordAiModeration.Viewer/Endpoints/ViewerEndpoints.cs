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

        app.MapGet("/api/channels/{channelId}/messages", (string channelId, DiscordViewerState state) =>
        {
            if (!ulong.TryParse(channelId, out var parsedChannelId))
            {
                return Results.BadRequest("Invalid channel id.");
            }

            return Results.Ok(state.GetMessages(parsedChannelId));
        });

        return app;
    }
}