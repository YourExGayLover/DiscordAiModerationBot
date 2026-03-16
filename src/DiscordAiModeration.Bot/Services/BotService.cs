using Discord;
using Discord.WebSocket;
using DiscordAiModeration.Core.Interfaces;
using DiscordAiModeration.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiscordAiModeration.Bot.Services;

public sealed class BotService
{
    private readonly DiscordSocketClient _discordClient;
    private readonly IDatabase _database;
    private readonly ModerationQueue _moderationQueue;
    private readonly ILogger<BotService> _logger;

    public BotService(
        DiscordSocketClient discordClient,
        IDatabase database,
        ModerationQueue moderationQueue,
        ILogger<BotService> logger)
    {
        _discordClient = discordClient;
        _database = database;
        _moderationQueue = moderationQueue;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _database.InitializeAsync(cancellationToken);

        _discordClient.Log += message =>
        {
            _logger.LogInformation("Discord: {Message}", message);
            return Task.CompletedTask;
        };

        _discordClient.Ready += OnReadyAsync;
        _discordClient.MessageReceived += OnMessageReceivedAsync;
        _discordClient.SlashCommandExecuted += OnSlashCommandExecutedAsync;

        var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Missing required environment variable: DISCORD_BOT_TOKEN");

        await _discordClient.LoginAsync(TokenType.Bot, token);
        await _discordClient.StartAsync();

        _ = Task.Run(() => _moderationQueue.RunAsync(cancellationToken), cancellationToken);
    }

    private async Task OnReadyAsync()
    {
        _logger.LogInformation("Connected as {Username}", _discordClient.CurrentUser.Username);
        await RegisterSlashCommandsAsync();
    }

    private async Task RegisterSlashCommandsAsync()
    {
        var modConfigCommand = new SlashCommandBuilder()
            .WithName("modconfig")
            .WithDescription("Configure moderation bot settings")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("set-alert-channel")
                .WithDescription("Set the channel where alerts are posted")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("channel", ApplicationCommandOptionType.Channel, "Text channel for alerts", true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("set-ping-role")
                .WithDescription("Set the role to ping when a possible violation is detected")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("role", ApplicationCommandOptionType.Role, "Role to ping", true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("set-threshold")
                .WithDescription("Set the minimum confidence threshold 0-100")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("percent", ApplicationCommandOptionType.Integer, "Threshold percentage", true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("toggle-ai")
                .WithDescription("Enable or disable AI moderation")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("enabled", ApplicationCommandOptionType.Boolean, "True to enable AI moderation", true));

        var rulesCommand = new SlashCommandBuilder()
            .WithName("rules")
            .WithDescription("Manage server rules used by the AI moderation system")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("add")
                .WithDescription("Add or update a rule")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("name", ApplicationCommandOptionType.String, "Rule name, for example Harassment", true)
                .AddOption("description", ApplicationCommandOptionType.String, "What the rule means", true)
                .AddOption("examples", ApplicationCommandOptionType.String, "Optional examples separated by ||", false))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("list")
                .WithDescription("List rules")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("remove")
                .WithDescription("Remove a rule")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("name", ApplicationCommandOptionType.String, "Rule name to remove", true));

        var reviewCommand = new SlashCommandBuilder()
            .WithName("review")
            .WithDescription("Review alerts and provide feedback")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("approve")
                .WithDescription("Mark an alert as a correct detection")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("id", ApplicationCommandOptionType.Integer, "Alert id", true)
                .AddOption("notes", ApplicationCommandOptionType.String, "Optional moderator notes", false))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("reject")
                .WithDescription("Mark an alert as a false positive")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("id", ApplicationCommandOptionType.Integer, "Alert id", true)
                .AddOption("notes", ApplicationCommandOptionType.String, "Optional moderator notes", false))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("list")
                .WithDescription("List recent alerts")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("status", ApplicationCommandOptionType.String, "all|pending|approved|rejected", false));

        try
        {
            await _discordClient.BulkOverwriteGlobalApplicationCommandsAsync(new ApplicationCommandProperties[]
            {
                modConfigCommand.Build(),
                rulesCommand.Build(),
                reviewCommand.Build()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register slash commands");
        }
    }

    private async Task OnMessageReceivedAsync(SocketMessage socketMessage)
    {
        if (socketMessage is not SocketUserMessage message)
            return;

        if (message.Author.IsBot)
            return;

        if (message.Channel is not SocketGuildChannel guildChannel)
            return;

        if (string.IsNullOrWhiteSpace(message.Content))
            return;

        var guildId = (long)guildChannel.Guild.Id;
        var settings = await _database.GetGuildSettingsAsync(guildId);
        if (settings is null || !settings.AiEnabled)
            return;

        await _moderationQueue.EnqueueAsync(new ModerationRequest
        {
            GuildId = guildId,
            ChannelId = (long)message.Channel.Id,
            MessageId = (long)message.Id,
            UserId = (long)message.Author.Id,
            Username = message.Author.Mention,
            ChannelMention = message.Channel is SocketTextChannel textChannel ? textChannel.Mention : $"<#{message.Channel.Id}>",
            Content = message.Content,
            CreatedUtc = DateTime.UtcNow
        });
    }

    private async Task OnSlashCommandExecutedAsync(SocketSlashCommand command)
    {
        try
        {
            if (command.GuildId is null)
            {
                await command.RespondAsync("This command only works in a server.", ephemeral: true);
                return;
            }

            var guildId = (long)command.GuildId.Value;
            switch (command.Data.Name)
            {
                case "modconfig":
                    await HandleModConfigAsync(command, guildId);
                    break;
                case "rules":
                    await HandleRulesAsync(command, guildId);
                    break;
                case "review":
                    await HandleReviewAsync(command, guildId);
                    break;
                default:
                    await command.RespondAsync("Unknown command.", ephemeral: true);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Slash command processing error");
            if (!command.HasResponded)
                await command.RespondAsync($"Error: {ex.Message}", ephemeral: true);
            else
                await command.FollowupAsync($"Error: {ex.Message}", ephemeral: true);
        }
    }

    private async Task HandleModConfigAsync(SocketSlashCommand command, long guildId)
    {
        var subCommand = command.Data.Options.First();
        var settings = await _database.GetGuildSettingsAsync(guildId) ?? new GuildSettings { GuildId = guildId };

        switch (subCommand.Name)
        {
            case "set-alert-channel":
            {
                var channel = (IChannel)subCommand.Options.First().Value!;
                settings.AlertChannelId = (long)channel.Id;
                await _database.UpsertGuildSettingsAsync(settings);
                await command.RespondAsync($"Alert channel set to <#{channel.Id}>.", ephemeral: true);
                break;
            }
            case "set-ping-role":
            {
                var role = (IRole)subCommand.Options.First().Value!;
                settings.PingRoleId = (long)role.Id;
                await _database.UpsertGuildSettingsAsync(settings);
                await command.RespondAsync($"Ping role set to <@&{role.Id}>.", ephemeral: true);
                break;
            }
            case "set-threshold":
            {
                var threshold = Convert.ToInt32(subCommand.Options.First().Value);
                settings.ConfidenceThreshold = Math.Clamp(threshold, 0, 100);
                await _database.UpsertGuildSettingsAsync(settings);
                await command.RespondAsync($"Confidence threshold set to {settings.ConfidenceThreshold}%.", ephemeral: true);
                break;
            }
            case "toggle-ai":
            {
                var enabled = Convert.ToBoolean(subCommand.Options.First().Value);
                settings.AiEnabled = enabled;
                await _database.UpsertGuildSettingsAsync(settings);
                await command.RespondAsync($"AI moderation {(enabled ? "enabled" : "disabled")}.", ephemeral: true);
                break;
            }
        }
    }

    private async Task HandleRulesAsync(SocketSlashCommand command, long guildId)
    {
        var subCommand = command.Data.Options.First();

        switch (subCommand.Name)
        {
            case "add":
            {
                var name = (string)subCommand.Options.First(x => x.Name == "name").Value!;
                var description = (string)subCommand.Options.First(x => x.Name == "description").Value!;
                var rawExamples = subCommand.Options.FirstOrDefault(x => x.Name == "examples")?.Value as string;
                var examples = string.IsNullOrWhiteSpace(rawExamples)
                    ? Array.Empty<string>()
                    : rawExamples.Split("||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                await _database.UpsertRuleAsync(new RuleRecord
                {
                    GuildId = guildId,
                    Name = name,
                    Description = description,
                    ExamplesJson = System.Text.Json.JsonSerializer.Serialize(examples)
                });

                await command.RespondAsync($"Saved rule **{name}**.", ephemeral: true);
                break;
            }
            case "list":
            {
                var rules = await _database.ListRulesAsync(guildId);
                if (rules.Count == 0)
                {
                    await command.RespondAsync("No rules configured yet.", ephemeral: true);
                    return;
                }

                var content = string.Join("\n\n", rules.Select(r => $"**{r.Name}**\n{r.Description}"));
                var chunks = Chunk(content, 1900).ToList();

                for (var i = 0; i < chunks.Count; i++)
                {
                    if (i == 0)
                        await command.RespondAsync(chunks[i], ephemeral: true);
                    else
                        await command.FollowupAsync(chunks[i], ephemeral: true);
                }

                break;
            }
            case "remove":
            {
                var name = (string)subCommand.Options.First(x => x.Name == "name").Value!;
                var removed = await _database.RemoveRuleAsync(guildId, name);
                await command.RespondAsync(removed ? $"Removed **{name}**." : $"Rule **{name}** was not found.", ephemeral: true);
                break;
            }
        }
    }

    private async Task HandleReviewAsync(SocketSlashCommand command, long guildId)
    {
        var subCommand = command.Data.Options.First();

        switch (subCommand.Name)
        {
            case "approve":
            {
                var alertId = Convert.ToInt64(subCommand.Options.First(x => x.Name == "id").Value);
                var notes = subCommand.Options.FirstOrDefault(x => x.Name == "notes")?.Value as string;
                var updated = await _database.SetAlertFeedbackAsync(alertId, guildId, "approved", notes, command.User.Id);
                await command.RespondAsync(updated ? $"Alert #{alertId} marked approved." : $"Alert #{alertId} not found.", ephemeral: true);
                break;
            }
            case "reject":
            {
                var alertId = Convert.ToInt64(subCommand.Options.First(x => x.Name == "id").Value);
                var notes = subCommand.Options.FirstOrDefault(x => x.Name == "notes")?.Value as string;
                var updated = await _database.SetAlertFeedbackAsync(alertId, guildId, "rejected", notes, command.User.Id);
                await command.RespondAsync(updated ? $"Alert #{alertId} marked rejected." : $"Alert #{alertId} not found.", ephemeral: true);
                break;
            }
            case "list":
            {
                var status = (subCommand.Options.FirstOrDefault(x => x.Name == "status")?.Value as string) ?? "all";
                var alerts = await _database.ListAlertsAsync(guildId, status, 15);
                if (alerts.Count == 0)
                {
                    await command.RespondAsync("No alerts found.", ephemeral: true);
                    return;
                }

                var lines = alerts.Select(a =>
                    $"**#{a.Id}** [{a.FeedbackStatus}] Rule={a.RuleName} Confidence={a.Confidence}% User=<@{a.UserId}> Text=\"{Trim(a.MessageContent, 80)}\"");
                await command.RespondAsync(string.Join("\n", lines), ephemeral: true);
                break;
            }
        }
    }

    private static string Trim(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";
    private static IEnumerable<string> Chunk(string text, int maxLength)
    {
        for (var i = 0; i < text.Length; i += maxLength)
            yield return text.Substring(i, Math.Min(maxLength, text.Length - i));
    }

}
