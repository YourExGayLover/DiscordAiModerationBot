using System.Text;
using System.Text.Json;
using Discord;
using Discord.WebSocket;
using DiscordAiModeration.Bot.Models;
using DiscordAiModeration.Core.Interfaces;
using DiscordAiModeration.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiscordAiModeration.Bot.Services;

public sealed class BotService
{
    private const int ContextMessageCount = 8;

    private readonly DiscordSocketClient _discordClient;
    private readonly IDatabase _database;
    private readonly ModerationQueue _moderationQueue;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BotService> _logger;

    public BotService(
        DiscordSocketClient discordClient,
        IDatabase database,
        ModerationQueue moderationQueue,
        IHttpClientFactory httpClientFactory,
        ILogger<BotService> logger)
    {
        _discordClient = discordClient;
        _database = database;
        _moderationQueue = moderationQueue;
        _httpClientFactory = httpClientFactory;
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
        _discordClient.InteractionCreated += OnInteractionCreatedAsync;

        var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Missing required environment variable: DISCORD_BOT_TOKEN");
        }

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
                .AddOption("enabled", ApplicationCommandOptionType.Boolean, "True to enable AI moderation", true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("toggle-simple-prompts")
                .WithDescription("Use a smaller, cheaper AI prompt format")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("enabled", ApplicationCommandOptionType.Boolean, "True to use simple prompts", true));

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
                .AddOption("name", ApplicationCommandOptionType.String, "Rule name to remove", true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("export")
                .WithDescription("Export all rules for this server as JSON")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("import")
                .WithDescription("Import rules for this server from a JSON attachment")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("file", ApplicationCommandOptionType.Attachment, "The exported JSON file", true)
                .AddOption("replace-existing", ApplicationCommandOptionType.Boolean, "If true, remove existing rules before import", false))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("seed-catholic-heresy")
                .WithDescription("Install the built-in Catholic heresy rule pack")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("replace-existing", ApplicationCommandOptionType.Boolean, "If true, remove existing rules before seeding", false))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("seed-catholic-morality")
                .WithDescription("Install the built-in Catholic moral teaching rule pack")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("replace-existing", ApplicationCommandOptionType.Boolean, "If true, remove existing rules before seeding", false));

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
                .WithName("override")
                .WithDescription("Approve an alert but replace the rule and reason")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("id", ApplicationCommandOptionType.Integer, "Alert id", true)
                .AddOption("rule", ApplicationCommandOptionType.String, "Correct rule name", true)
                .AddOption("reason", ApplicationCommandOptionType.String, "Correct moderator reason", true)
                .AddOption("notes", ApplicationCommandOptionType.String, "Optional moderator notes", false))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("list")
                .WithDescription("List recent alerts")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("status", ApplicationCommandOptionType.String, "all|pending|approved|rejected", false));

        var rolesCommand = new SlashCommandBuilder()
            .WithName("roles")
            .WithDescription("Role audit tools")
            .AddOption(BuildRoleAuditSubCommand());

        try
        {
            foreach (var guild in _discordClient.Guilds)
            {
                await guild.BulkOverwriteApplicationCommandAsync(new ApplicationCommandProperties[]
                {
                    modConfigCommand.Build(),
                    rulesCommand.Build(),
                    reviewCommand.Build(),
                    rolesCommand.Build()
                });

                _logger.LogInformation(
                    "Slash commands registered for guild {GuildName} ({GuildId})",
                    guild.Name,
                    guild.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register slash commands");
        }
    }

    private async Task OnMessageReceivedAsync(SocketMessage socketMessage)
    {
        if (socketMessage is not SocketUserMessage message)
        {
            return;
        }

        if (message.Author.IsBot)
        {
            return;
        }

        if (message.Channel is not SocketGuildChannel guildChannel)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(message.Content))
        {
            return;
        }

        var guildId = (long)guildChannel.Guild.Id;
        var settings = await _database.GetGuildSettingsAsync(guildId);
        if (settings is null || !settings.AiEnabled)
        {
            return;
        }

        var recentContext = await LoadRecentContextAsync(message, ContextMessageCount);

        await _moderationQueue.EnqueueAsync(new ModerationRequest
        {
            GuildId = guildId,
            ChannelId = (long)message.Channel.Id,
            MessageId = (long)message.Id,
            UserId = (long)message.Author.Id,
            Username = message.Author.Mention,
            ChannelMention = message.Channel is SocketTextChannel textChannel ? textChannel.Mention : $"<#{message.Channel.Id}>",
            Content = message.Content,
            CreatedUtc = DateTime.UtcNow,
            RecentContext = recentContext
        });
    }

    private async Task<IReadOnlyList<ModerationContextMessage>> LoadRecentContextAsync(SocketUserMessage targetMessage, int limit)
    {
        try
        {
            var messages = await targetMessage.Channel
                .GetMessagesAsync(limit + 1)
                .FlattenAsync();

            return messages
                .Where(m => m.Id != targetMessage.Id)
                .Where(m => !m.Author.IsBot)
                .Where(m => !string.IsNullOrWhiteSpace(m.Content))
                .OrderBy(m => m.Timestamp)
                .TakeLast(limit)
                .Select(m => new ModerationContextMessage
                {
                    MessageId = (long)m.Id,
                    UserId = (long)m.Author.Id,
                    AuthorDisplay = BuildAuthorDisplay(m.Author),
                    Content = NormalizeContextContent(m.Content),
                    IsCurrentUser = m.Author.Id == targetMessage.Author.Id
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to load recent context for message {MessageId} in channel {ChannelId}",
                targetMessage.Id,
                targetMessage.Channel.Id);

            return Array.Empty<ModerationContextMessage>();
        }
    }

    private static string BuildAuthorDisplay(IUser author)
    {
        if (!string.IsNullOrWhiteSpace(author.GlobalName))
        {
            return author.GlobalName;
        }

        return author.Username;
    }

    private static string NormalizeContextContent(string content)
    {
        var normalized = content.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 400 ? normalized : normalized[..400] + "...";
    }

    private async Task OnInteractionCreatedAsync(SocketInteraction interaction)
    {
        try
        {
            if (interaction is SocketSlashCommand slashCommand)
            {
                await OnSlashCommandExecutedAsync(slashCommand);
                return;
            }

            if (interaction is SocketMessageComponent component)
            {
                await HandleMessageComponentAsync(component);
                return;
            }

            if (interaction is SocketModal modal)
            {
                await HandleModalAsync(modal);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Interaction processing error");

            try
            {
                switch (interaction)
                {
                    case SocketMessageComponent component when !component.HasResponded:
                        await component.RespondAsync("Something went wrong handling that action.", ephemeral: true);
                        break;
                    case SocketSlashCommand slashCommand when !slashCommand.HasResponded:
                        await slashCommand.RespondAsync("Something went wrong handling that command.", ephemeral: true);
                        break;
                    case SocketModal modal when !modal.HasResponded:
                        await modal.RespondAsync("Something went wrong handling that form.", ephemeral: true);
                        break;
                }
            }
            catch (Exception responseEx)
            {
                _logger.LogError(responseEx, "Failed to send interaction error response");
            }
        }
    }

    private async Task HandleMessageComponentAsync(SocketMessageComponent component)
    {
        var customId = component.Data.CustomId ?? string.Empty;
        _logger.LogInformation(
            "Component interaction received. CustomId={CustomId} UserId={UserId} GuildId={GuildId}",
            customId,
            component.User.Id,
            component.GuildId);

        if (!TryParseReviewAction(customId, component.GuildId, out var action, out var guildId, out var alertId))
        {
            await component.RespondAsync("Unknown review action.", ephemeral: true);
            return;
        }

        if (action.Equals("override", StringComparison.OrdinalIgnoreCase))
        {
            var modal = new ModalBuilder()
                .WithTitle($"Override alert #{alertId}")
                .WithCustomId($"review-override:{guildId}:{alertId}")
                .AddTextInput(
                    label: "Correct rule name",
                    customId: "rule",
                    style: TextInputStyle.Short,
                    placeholder: "Enter the correct rule name",
                    required: true,
                    maxLength: 100)
                .AddTextInput(
                    label: "Correct reason",
                    customId: "reason",
                    style: TextInputStyle.Paragraph,
                    placeholder: "Explain the correct reason for approving this alert",
                    required: true,
                    maxLength: 1000)
                .AddTextInput(
                    label: "Moderator notes (optional)",
                    customId: "notes",
                    style: TextInputStyle.Paragraph,
                    placeholder: "Optional extra notes",
                    required: false,
                    maxLength: 1000);

            await component.RespondWithModalAsync(modal.Build());
            return;
        }

        await component.DeferAsync(ephemeral: true);

        var status = action.Equals("approve", StringComparison.OrdinalIgnoreCase)
            ? "approved"
            : "rejected";

        var updated = await _database.SetAlertFeedbackAsync(
            alertId,
            guildId,
            status,
            null,
            component.User.Id);

        if (!updated)
        {
            await component.FollowupAsync($"Alert #{alertId} was not found.", ephemeral: true);
            return;
        }

        await component.FollowupAsync(
            action.Equals("approve", StringComparison.OrdinalIgnoreCase)
                ? $"Alert #{alertId} marked approved."
                : $"Alert #{alertId} dismissed.",
            ephemeral: true);

        try
        {
            await component.Message.ModifyAsync(properties =>
            {
                properties.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to clear review buttons for alert {AlertId} on message {MessageId}",
                alertId,
                component.Message.Id);
        }
    }

    private async Task HandleModalAsync(SocketModal modal)
    {
        var customId = modal.Data.CustomId ?? string.Empty;
        if (!TryParseOverrideModal(customId, modal.GuildId, out var guildId, out var alertId))
        {
            await modal.RespondAsync("Unknown review form.", ephemeral: true);
            return;
        }

        var fields = modal.Data.Components.ToDictionary(x => x.CustomId, x => x.Value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        var ruleName = fields.GetValueOrDefault("rule", string.Empty);
        var reason = fields.GetValueOrDefault("reason", string.Empty);
        var notes = fields.GetValueOrDefault("notes", string.Empty);

        if (string.IsNullOrWhiteSpace(ruleName) || string.IsNullOrWhiteSpace(reason))
        {
            await modal.RespondAsync("A corrected rule and corrected reason are required.", ephemeral: true);
            return;
        }

        var updated = await _database.SetAlertFeedbackAsync(
            alertId,
            guildId,
            "approved",
            string.IsNullOrWhiteSpace(notes) ? null : notes,
            modal.User.Id,
            ruleName,
            reason);

        if (!updated)
        {
            await modal.RespondAsync($"Alert #{alertId} was not found.", ephemeral: true);
            return;
        }

        await modal.RespondAsync($"Alert #{alertId} approved with corrected rule and reason.", ephemeral: true);

    }

    private static bool TryParseOverrideModal(
        string customId,
        ulong? interactionGuildId,
        out long guildId,
        out long alertId)
    {
        guildId = 0;
        alertId = 0;

        if (string.IsNullOrWhiteSpace(customId))
        {
            return false;
        }

        var parts = customId.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !parts[0].Equals("review-override", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (parts.Length == 2)
        {
            if (interactionGuildId is null || !long.TryParse(parts[1], out alertId))
            {
                return false;
            }

            guildId = (long)interactionGuildId.Value;
            return true;
        }

        return long.TryParse(parts[1], out guildId) && long.TryParse(parts[2], out alertId);
    }

    private static bool TryParseReviewAction(
        string customId,
        ulong? interactionGuildId,
        out string action,
        out long guildId,
        out long alertId)
    {
        action = string.Empty;
        guildId = 0;
        alertId = 0;

        if (string.IsNullOrWhiteSpace(customId))
        {
            return false;
        }

        var parts = customId.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 3 || !parts[0].Equals("review", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        action = parts[1];

        if (!action.Equals("approve", StringComparison.OrdinalIgnoreCase) &&
            !action.Equals("dismiss", StringComparison.OrdinalIgnoreCase) &&
            !action.Equals("reject", StringComparison.OrdinalIgnoreCase) &&
            !action.Equals("override", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (parts.Length == 3)
        {
            if (interactionGuildId is null || !long.TryParse(parts[2], out alertId))
            {
                return false;
            }

            guildId = (long)interactionGuildId.Value;
            return true;
        }

        if (parts.Length >= 4)
        {
            return long.TryParse(parts[2], out guildId) &&
                   long.TryParse(parts[3], out alertId);
        }

        return false;
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
                case "roles":
                    await HandleRolesAsync(command, guildId);
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
            {
                await command.RespondAsync($"Error: {ex.Message}", ephemeral: true);
            }
            else
            {
                await command.FollowupAsync($"Error: {ex.Message}", ephemeral: true);
            }
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
            case "toggle-simple-prompts":
                {
                    var enabled = Convert.ToBoolean(subCommand.Options.First().Value);
                    settings.UseSimplePrompts = enabled;
                    await _database.UpsertGuildSettingsAsync(settings);
                    await command.RespondAsync(
                        enabled
                            ? "Simple prompts enabled.\nThe bot will use a shorter, cheaper moderation request."
                            : "Simple prompts disabled.\nThe bot will use the full moderation request.",
                        ephemeral: true);
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
                        ExamplesJson = JsonSerializer.Serialize(examples)
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
                        {
                            await command.RespondAsync(chunks[i], ephemeral: true);
                        }
                        else
                        {
                            await command.FollowupAsync(chunks[i], ephemeral: true);
                        }
                    }

                    break;
                }
            case "remove":
                {
                    var name = (string)subCommand.Options.First(x => x.Name == "name").Value!;
                    var removed = await _database.RemoveRuleAsync(guildId, name);
                    await command.RespondAsync(
                        removed ? $"Removed **{name}**." : $"Rule **{name}** was not found.",
                        ephemeral: true);
                    break;
                }
            case "export":
                {
                    var rules = await _database.ListRulesAsync(guildId);
                    var export = new RulesExportFile
                    {
                        GuildId = guildId,
                        ExportedUtc = DateTime.UtcNow,
                        Rules = rules.Select(r => new RuleImportItem
                        {
                            Name = r.Name,
                            Description = r.Description,
                            Examples = DeserializeExamples(r.ExamplesJson)
                        }).ToList()
                    };

                    var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
                    var bytes = Encoding.UTF8.GetBytes(json);
                    await using var stream = new MemoryStream(bytes);
                    var attachment = new FileAttachment(stream, $"rules-{guildId}-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
                    await command.RespondWithFileAsync(
                        attachment,
                        text: $"Exported {export.Rules.Count} rule(s).",
                        ephemeral: true);
                    break;
                }
            case "import":
                {
                    await command.DeferAsync(ephemeral: true);
                    var attachmentOption = subCommand.Options.First(x => x.Name == "file");
                    var replaceExisting = (subCommand.Options.FirstOrDefault(x => x.Name == "replace-existing")?.Value as bool?) ?? false;
                    var attachment = (IAttachment)attachmentOption.Value!;

                    if (!attachment.Filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        await command.FollowupAsync("Import file must be a JSON file.", ephemeral: true);
                        return;
                    }

                    var client = _httpClientFactory.CreateClient();
                    var json = await client.GetStringAsync(attachment.Url);
                    RulesExportFile? importFile;
                    try
                    {
                        importFile = JsonSerializer.Deserialize<RulesExportFile>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    }
                    catch (JsonException)
                    {
                        await command.FollowupAsync("That file is not valid JSON for rule import.", ephemeral: true);
                        return;
                    }

                    if (importFile is null || importFile.Rules.Count == 0)
                    {
                        await command.FollowupAsync("The import file did not contain any rules.", ephemeral: true);
                        return;
                    }

                    var importedCount = await UpsertImportedRulesAsync(guildId, importFile.Rules, replaceExisting);
                    if (importedCount == 0)
                    {
                        await command.FollowupAsync("No valid rules were found in the file.", ephemeral: true);
                        return;
                    }

                    await command.FollowupAsync(
                        replaceExisting
                            ? $"Imported {importedCount} rule(s) and replaced existing rules."
                            : $"Imported {importedCount} rule(s).\nExisting rules with matching names were updated.",
                        ephemeral: true);
                    break;
                }
            case "seed-catholic-heresy":
                {
                    var replaceExisting = (subCommand.Options.FirstOrDefault(x => x.Name == "replace-existing")?.Value as bool?) ?? false;
                    var pack = CatholicRulePack.Create(guildId);
                    var importedCount = await UpsertImportedRulesAsync(guildId, pack.Rules, replaceExisting);
                    await command.RespondAsync(
                        replaceExisting
                            ? $"Seeded **{CatholicRulePack.PackName}** with {importedCount} rule(s) and replaced existing rules."
                            : $"Seeded **{CatholicRulePack.PackName}** with {importedCount} rule(s).\nExisting rules with matching names were updated.",
                        ephemeral: true);
                    break;
                }
            case "seed-catholic-morality":
                {
                    var replaceExisting = (subCommand.Options.FirstOrDefault(x => x.Name == "replace-existing")?.Value as bool?) ?? false;
                    var pack = CatholicMoralityRulePack.Create(guildId);
                    var importedCount = await UpsertImportedRulesAsync(guildId, pack.Rules, replaceExisting);
                    await command.RespondAsync(
                        replaceExisting
                            ? $"Seeded **{CatholicMoralityRulePack.PackName}** with {importedCount} rule(s) and replaced existing rules."
                            : $"Seeded **{CatholicMoralityRulePack.PackName}** with {importedCount} rule(s).\nExisting rules with matching names were updated.",
                        ephemeral: true);
                    break;
                }
        }
    }

    private async Task<int> UpsertImportedRulesAsync(long guildId, IEnumerable<RuleImportItem> rules, bool replaceExisting)
    {
        var validRules = rules
            .Where(r => !string.IsNullOrWhiteSpace(r.Name) && !string.IsNullOrWhiteSpace(r.Description))
            .GroupBy(r => r.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (validRules.Count == 0)
        {
            return 0;
        }

        if (replaceExisting)
        {
            await _database.RemoveAllRulesAsync(guildId);
        }

        foreach (var rule in validRules)
        {
            await _database.UpsertRuleAsync(new RuleRecord
            {
                GuildId = guildId,
                Name = rule.Name.Trim(),
                Description = rule.Description.Trim(),
                ExamplesJson = JsonSerializer.Serialize(
                    (rule.Examples ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray())
            });
        }

        return validRules.Count;
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
            case "override":
                {
                    var alertId = Convert.ToInt64(subCommand.Options.First(x => x.Name == "id").Value);
                    var ruleName = (string)subCommand.Options.First(x => x.Name == "rule").Value!;
                    var reason = (string)subCommand.Options.First(x => x.Name == "reason").Value!;
                    var notes = subCommand.Options.FirstOrDefault(x => x.Name == "notes")?.Value as string;
                    var updated = await _database.SetAlertFeedbackAsync(alertId, guildId, "approved", notes, command.User.Id, ruleName, reason);
                    await command.RespondAsync(updated ? $"Alert #{alertId} approved with corrected rule and reason." : $"Alert #{alertId} not found.", ephemeral: true);
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

                    var lines = alerts.Select(a => $"**#{a.Id}** [{a.FeedbackStatus}] Rule={a.RuleName} Confidence={a.Confidence}% User=<@{a.UserId}> Text=\"{Trim(a.MessageContent, 80)}\"");
                    await command.RespondAsync(string.Join("\n", lines), ephemeral: true);
                    break;
                }
        }
    }

    private async Task HandleRolesAsync(SocketSlashCommand command, long guildId)
    {
        var subCommand = command.Data.Options.First();
        switch (subCommand.Name)
        {
            case "audit-exactly-one":
                await HandleRoleAuditExactlyOneAsync(command, guildId, subCommand);
                break;
            default:
                await command.RespondAsync("Unknown roles subcommand.", ephemeral: true);
                break;
        }
    }

    private async Task HandleRoleAuditExactlyOneAsync(SocketSlashCommand command, long guildId, SocketSlashCommandDataOption subCommand)
    {
        await command.DeferAsync(ephemeral: true);

        var selectedRoles = subCommand.Options
            .Where(option => option.Value is IRole)
            .Select(option => (IRole)option.Value!)
            .GroupBy(role => role.Id)
            .Select(group => group.First())
            .ToList();

        if (selectedRoles.Count < 2)
        {
            await command.FollowupAsync("Choose at least two distinct roles.", ephemeral: true);
            return;
        }

        var includeBots = (subCommand.Options.FirstOrDefault(x => x.Name == "include-bots")?.Value as bool?) ?? false;
        var guild = _discordClient.GetGuild((ulong)guildId);
        if (guild is null)
        {
            await command.FollowupAsync("Could not load the server.", ephemeral: true);
            return;
        }

        var allUsers = new List<IGuildUser>();
        await foreach (var batch in guild.GetUsersAsync())
        {
            allUsers.AddRange(batch);
        }

        var selectedRoleIds = selectedRoles.Select(role => role.Id).ToHashSet();
        var violations = new List<string>();
        var noRoleCount = 0;
        var multipleRoleCount = 0;

        foreach (var user in allUsers
                     .Where(user => includeBots || !user.IsBot)
                     .OrderBy(user => user.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var matchedRoles = user.RoleIds
                .Where(selectedRoleIds.Contains)
                .Select(roleId => guild.GetRole(roleId)?.Name ?? roleId.ToString())
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (matchedRoles.Count == 1)
            {
                continue;
            }

            var label = matchedRoles.Count == 0
                ? "No selected roles"
                : $"Multiple selected roles ({matchedRoles.Count}): {string.Join(", ", matchedRoles)}";

            if (matchedRoles.Count == 0)
            {
                noRoleCount++;
            }
            else
            {
                multipleRoleCount++;
            }

            violations.Add($"{user.Mention} - {user.Username}#{user.Discriminator} - {label}");
        }

        var roleSummary = string.Join(", ", selectedRoles.Select(role => role.Mention));
        var audience = includeBots ? "members and bots" : "members";

        if (violations.Count == 0)
        {
            await command.FollowupAsync(
                $"Checked {allUsers.Count(user => includeBots || !user.IsBot)} {audience}.\nEvery checked account has exactly one of these roles: {roleSummary}",
                ephemeral: true);
            return;
        }

        var headerLines = new List<string>
        {
            "Role audit: exactly one selected role required",
            $"Selected roles: {roleSummary}",
            $"Checked accounts: {allUsers.Count(user => includeBots || !user.IsBot)}",
            $"Missing all selected roles: {noRoleCount}",
            $"Having multiple selected roles: {multipleRoleCount}",
            string.Empty
        };

        var outputLines = headerLines.Concat(violations).ToList();
        var chunks = ChunkLines(outputLines, 1900).ToList();
        for (var i = 0; i < chunks.Count; i++)
        {
            await command.FollowupAsync(chunks[i], ephemeral: true);
        }
    }

    private static SlashCommandOptionBuilder BuildRoleAuditSubCommand()
    {
        var subCommand = new SlashCommandOptionBuilder()
            .WithName("audit-exactly-one")
            .WithDescription("List users who have none or more than one of the selected roles")
            .WithType(ApplicationCommandOptionType.SubCommand)
            .AddOption("role1", ApplicationCommandOptionType.Role, "First role", true)
            .AddOption("role2", ApplicationCommandOptionType.Role, "Second role", true)
            .AddOption("role3", ApplicationCommandOptionType.Role, "Third role", false)
            .AddOption("role4", ApplicationCommandOptionType.Role, "Fourth role", false)
            .AddOption("role5", ApplicationCommandOptionType.Role, "Fifth role", false)
            .AddOption("role6", ApplicationCommandOptionType.Role, "Sixth role", false)
            .AddOption("role7", ApplicationCommandOptionType.Role, "Seventh role", false)
            .AddOption("role8", ApplicationCommandOptionType.Role, "Eighth role", false)
            .AddOption("role9", ApplicationCommandOptionType.Role, "Ninth role", false)
            .AddOption("role10", ApplicationCommandOptionType.Role, "Tenth role", false)
            .AddOption("include-bots", ApplicationCommandOptionType.Boolean, "Include bot accounts in the audit", false);

        return subCommand;
    }

    private static List<string> DeserializeExamples(string? examplesJson)
    {
        if (string.IsNullOrWhiteSpace(examplesJson))
        {
            return new List<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(examplesJson) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static string Trim(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";

    private static IEnumerable<string> Chunk(string text, int maxLength)
    {
        for (var i = 0; i < text.Length; i += maxLength)
        {
            yield return text.Substring(i, Math.Min(maxLength, text.Length - i));
        }
    }

    private static IEnumerable<string> ChunkLines(IEnumerable<string> lines, int maxLength)
    {
        var current = new List<string>();
        var currentLength = 0;

        foreach (var line in lines)
        {
            var normalizedLine = line ?? string.Empty;
            var lineLength = normalizedLine.Length + Environment.NewLine.Length;

            if (current.Count > 0 && currentLength + lineLength > maxLength)
            {
                yield return string.Join(Environment.NewLine, current);
                current.Clear();
                currentLength = 0;
            }

            if (normalizedLine.Length > maxLength)
            {
                foreach (var chunk in Chunk(normalizedLine, maxLength))
                {
                    yield return chunk;
                }

                continue;
            }

            current.Add(normalizedLine);
            currentLength += lineLength;
        }

        if (current.Count > 0)
        {
            yield return string.Join(Environment.NewLine, current);
        }
    }
}