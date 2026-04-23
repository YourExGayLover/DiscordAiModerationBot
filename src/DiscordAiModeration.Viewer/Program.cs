using Discord;
using Discord.WebSocket;
using DiscordAiModeration.Viewer.Endpoints;
using DiscordAiModeration.Viewer.Models;
using DiscordAiModeration.Viewer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
    options.SingleLine = true;
});

builder.Services.Configure<ViewerOptions>(builder.Configuration.GetSection(ViewerOptions.SectionName));

builder.Services.AddSingleton(_ =>
{
    var config = new DiscordSocketConfig
    {
        GatewayIntents = GatewayIntents.Guilds
                            | GatewayIntents.GuildMessages
                            | GatewayIntents.MessageContent
                            | GatewayIntents.GuildVoiceStates,
        AlwaysDownloadUsers = false,
        LogGatewayIntentWarnings = false
    };

    return new DiscordSocketClient(config);
});

builder.Services.AddSingleton<DiscordViewerState>();
builder.Services.AddSingleton<DiscordViewerHostedService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DiscordViewerHostedService>());

builder.Services.AddRouting();

var app = builder.Build();

var viewerOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ViewerOptions>>().Value;
app.Urls.Add($"http://localhost:{viewerOptions.Port}");

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapViewerEndpoints();

using (var scope = app.Services.CreateScope())
{
    var hostedService = scope.ServiceProvider.GetRequiredService<DiscordViewerHostedService>();
    var startupTimeout = TimeSpan.FromSeconds(45);
    using var cts = new CancellationTokenSource(startupTimeout);

    try
    {
        await hostedService.WaitUntilReadyAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        app.Logger.LogWarning("Discord viewer did not finish syncing within {TimeoutSeconds} seconds. The web UI will still start.", startupTimeout.TotalSeconds);
    }
}

app.Run();
