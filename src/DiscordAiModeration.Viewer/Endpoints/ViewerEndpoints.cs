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

        app.MapGet("/api/channels", (ulong? guildId, DiscordSocketClient client, DiscordViewerState state) =>
            Results.Ok(state.GetChannels(client, guildId)));

        app.MapGet("/api/channels/{channelId}/messages", (ulong channelId, DiscordViewerState state) =>
            Results.Ok(state.GetMessages(channelId)));

        return app;
    }
}