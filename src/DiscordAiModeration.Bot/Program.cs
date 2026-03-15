using Discord;
using Discord.WebSocket;
using DiscordAiModeration.Bot.Services;
using DiscordAiModeration.Core.Interfaces;
using DiscordAiModeration.Infrastructure.Data;
using DiscordAiModeration.Infrastructure.Options;
using DiscordAiModeration.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.Configure<OpenAiOptions>(options =>
{
    options.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
    options.Model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-5-mini";
});

builder.Services.Configure<StorageOptions>(options =>
{
    options.SqlitePath = Environment.GetEnvironmentVariable("SQLITE_PATH") ?? "botdata.db";
});

builder.Services.AddSingleton(_ =>
{
    var config = new DiscordSocketConfig
    {
        GatewayIntents = GatewayIntents.Guilds |
                         GatewayIntents.GuildMessages |
                         GatewayIntents.MessageContent,
        AlwaysDownloadUsers = false,
        LogGatewayIntentWarnings = false,
        UseInteractionSnowflakeDate = false
    };

    return new DiscordSocketClient(config);
});

builder.Services.AddHttpClient<IAiModerationService, AiModerationService>();
builder.Services.AddSingleton<IDatabase, SqliteDatabase>();
builder.Services.AddSingleton<ModerationQueue>();
builder.Services.AddSingleton<BotService>();
builder.Services.AddHostedService<DiscordWorker>();

var host = builder.Build();
await host.RunAsync();
