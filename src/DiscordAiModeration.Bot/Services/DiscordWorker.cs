using Microsoft.Extensions.Hosting;

namespace DiscordAiModeration.Bot.Services;

public sealed class DiscordWorker : BackgroundService
{
    private readonly BotService _botService;

    public DiscordWorker(BotService botService)
    {
        _botService = botService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _botService.StartAsync(stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
