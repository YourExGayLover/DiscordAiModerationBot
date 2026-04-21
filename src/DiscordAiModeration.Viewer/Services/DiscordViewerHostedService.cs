using Discord;
using Discord.WebSocket;
using DiscordAiModeration.Viewer.Models;
using Microsoft.Extensions.Options;

namespace DiscordAiModeration.Viewer.Services;

public sealed class DiscordViewerHostedService : BackgroundService
{
    private readonly DiscordSocketClient _client;
    private readonly DiscordViewerState _state;
    private readonly ILogger<DiscordViewerHostedService> _logger;
    private readonly ViewerOptions _options;
    private readonly TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DiscordViewerHostedService(
        DiscordSocketClient client,
        DiscordViewerState state,
        IOptions<ViewerOptions> options,
        ILogger<DiscordViewerHostedService> logger)
    {
        _client = client;
        _state = state;
        _logger = logger;
        _options = options.Value;

        _client.Log += OnDiscordLogAsync;
        _client.Ready += OnReadyAsync;
        _client.MessageReceived += OnMessageReceivedAsync;
    }

    public Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        return _readyTcs.Task.WaitAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Missing DISCORD_BOT_TOKEN environment variable.");
        }

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _client.StopAsync();
        await _client.LogoutAsync();
        await base.StopAsync(cancellationToken);
    }

    private async Task OnReadyAsync()
    {
        try
        {
            IEnumerable<SocketGuild> guilds = _client.Guilds;
            if (_options.PreferredGuildId is ulong preferredGuildId)
            {
                guilds = guilds.Where(x => x.Id == preferredGuildId);
            }

            foreach (var guild in guilds)
            {
                foreach (var channel in guild.TextChannels.OrderBy(x => x.Position))
                {
                    try
                    {
                        var messages = await channel.GetMessagesAsync(limit: _options.MaxMessagesPerChannel).FlattenAsync();
                        _state.StoreHistoricalMessages(channel.Id, messages);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to read history for #{ChannelName} ({ChannelId}) in guild {GuildName}", channel.Name, channel.Id, guild.Name);
                    }
                }
            }

            _readyTcs.TrySetResult();
            _logger.LogInformation("Discord viewer is ready. Guilds={GuildCount}", _client.Guilds.Count);
        }
        catch (Exception ex)
        {
            _readyTcs.TrySetException(ex);
            _logger.LogError(ex, "Failed during initial Discord viewer sync.");
        }
    }

    private Task OnMessageReceivedAsync(SocketMessage message)
    {
        if (message.Channel is SocketTextChannel)
        {
            _state.StoreLiveMessage(message);
        }

        return Task.CompletedTask;
    }

    private Task OnDiscordLogAsync(LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Debug,
            _ => LogLevel.Information
        };

        _logger.Log(level, message.Exception, "Discord.Net: {Message}", message.Message);
        return Task.CompletedTask;
    }
}
