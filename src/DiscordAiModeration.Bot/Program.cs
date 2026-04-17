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
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
    options.SingleLine = true;
});

// Use standard .NET logging configuration from appsettings / environment variables.
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

builder.Services.Configure<AiProviderOptions>(options =>
{
    options.Provider = Environment.GetEnvironmentVariable("AI_PROVIDER") ?? "openai";
    options.OpenAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
    options.OpenAiModel = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-5-mini";
    options.OllamaBaseUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://localhost:11434";
    options.OllamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "llama3.2";
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
                         GatewayIntents.MessageContent |
                         GatewayIntents.GuildMembers,
        AlwaysDownloadUsers = false,
        LogGatewayIntentWarnings = false,
        UseInteractionSnowflakeDate = false
    };

    return new DiscordSocketClient(config);
});

builder.Services.AddHttpClient<OpenAiModerationService>();
builder.Services.AddHttpClient<OllamaModerationService>();

builder.Services.AddTransient<IAiModerationService, AiModerationService>();
builder.Services.AddSingleton<IDatabase, SqliteDatabase>();
builder.Services.AddSingleton<ModerationQueue>();
builder.Services.AddSingleton<BotService>();

builder.Services.AddSingleton<GuildConfigurationService>();
builder.Services.AddHostedService<DiscordWorker>();

var host = builder.Build();

var startupLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
startupLogger.LogInformation(
    "Discord AI Moderation Bot starting. Provider={Provider} OpenAiModel={OpenAiModel} OllamaModel={OllamaModel}",
    Environment.GetEnvironmentVariable("AI_PROVIDER") ?? "openai",
    Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-5-mini",
    Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "llama3.2");

await host.RunAsync();
