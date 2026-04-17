using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace DiscordAiModeration.Bot.Services
{
    public class GuildConfigurationService
    {
        private readonly DiscordSocketClient _discordClient;
        private readonly ILogger<GuildConfigurationService> _logger;
        private readonly string _backupDirectory;

        public GuildConfigurationService(
            DiscordSocketClient discordClient,
            ILogger<GuildConfigurationService> logger)
        {
            _discordClient = discordClient;
            _logger = logger;
            _backupDirectory = Path.Combine(AppContext.BaseDirectory, "backups");
            Directory.CreateDirectory(_backupDirectory);
        }

        public async Task BackupConfigurationAsync(SocketSlashCommand command)
        {
            try
            {
                if (!command.GuildId.HasValue)
                {
                    await command.RespondAsync("This command must be used in a server.", ephemeral: true);
                    return;
                }

                var commandGuild = _discordClient.GetGuild(command.GuildId.Value);
                if (commandGuild == null)
                {
                    await command.RespondAsync("Could not resolve the current server.", ephemeral: true);
                    return;
                }

                var invokingUser = commandGuild.GetUser(command.User.Id);
                if (invokingUser == null || !invokingUser.GuildPermissions.Administrator)
                {
                    await command.RespondAsync("You must be an administrator to use this command.", ephemeral: true);
                    return;
                }

                ulong sourceGuildId = commandGuild.Id;

                var sourceServerOption = command.Data.Options.FirstOrDefault(x => x.Name == "source_server_id");
                if (sourceServerOption != null &&
                    sourceServerOption.Value != null &&
                    ulong.TryParse(sourceServerOption.Value.ToString(), out var parsedSourceGuildId))
                {
                    sourceGuildId = parsedSourceGuildId;
                }

                var sourceGuild = _discordClient.GetGuild(sourceGuildId);
                if (sourceGuild == null)
                {
                    await command.RespondAsync("I could not find the source server. Make sure the bot is in that server.", ephemeral: true);
                    return;
                }

                await command.DeferAsync(ephemeral: true);

                _logger.LogInformation("=== BACKUP START ===");
                _logger.LogInformation(
                    "Backup requested by {UserId} from command guild {CommandGuildName} ({CommandGuildId}). Source guild: {SourceGuildName} ({SourceGuildId})",
                    command.User.Id,
                    commandGuild.Name,
                    commandGuild.Id,
                    sourceGuild.Name,
                    sourceGuild.Id);

                _logger.LogInformation("Building role backup for guild {GuildName} ({GuildId})", sourceGuild.Name, sourceGuild.Id);
                var roles = BuildRoleBackup(sourceGuild);
                _logger.LogInformation("Built role backup. Count={RoleCount}", roles.Count);

                _logger.LogInformation("Building channel backup for guild {GuildName} ({GuildId})", sourceGuild.Name, sourceGuild.Id);
                var channels = BuildChannelBackup(sourceGuild);
                _logger.LogInformation("Built channel backup. Count={ChannelCount}", channels.Count);

                var backup = new GuildConfigurationBackup
                {
                    Version = 3,
                    ExportedAtUtc = DateTime.UtcNow,
                    SourceGuildId = sourceGuild.Id,
                    SourceGuildName = sourceGuild.Name,
                    Roles = roles,
                    Channels = channels
                };

                var fileName = $"{sourceGuild.Id}-{SanitizeFileName(sourceGuild.Name)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
                var fullPath = Path.Combine(_backupDirectory, fileName);

                _logger.LogInformation("Writing backup file to {FullPath}", fullPath);

                var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(fullPath, json);

                _logger.LogInformation("=== BACKUP COMPLETE === File={FileName}", fileName);

                await command.FollowupAsync(
                    $"Backed up **{sourceGuild.Name}** to `{fileName}`.",
                    ephemeral: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while backing up guild configuration.");
                await SafeErrorResponseAsync(command, "An error occurred while backing up the configuration.");
            }
        }

        public async Task LoadConfigurationAsync(SocketSlashCommand command)
        {
            try
            {
                if (!command.GuildId.HasValue)
                {
                    await command.RespondAsync("This command must be used in a server.", ephemeral: true);
                    return;
                }

                var targetGuild = _discordClient.GetGuild(command.GuildId.Value);
                if (targetGuild == null)
                {
                    await command.RespondAsync("Could not resolve the current server.", ephemeral: true);
                    return;
                }

                var invokingUser = targetGuild.GetUser(command.User.Id);
                if (invokingUser == null || !invokingUser.GuildPermissions.Administrator)
                {
                    await command.RespondAsync("You must be an administrator to use this command.", ephemeral: true);
                    return;
                }

                var filename = command.Data.Options
                    .FirstOrDefault(x => x.Name == "filename")
                    ?.Value?
                    .ToString();

                if (string.IsNullOrWhiteSpace(filename))
                {
                    await command.RespondAsync("You must provide a filename.", ephemeral: true);
                    return;
                }

                if (filename.Contains("..") || Path.IsPathRooted(filename))
                {
                    await command.RespondAsync("Invalid filename.", ephemeral: true);
                    return;
                }

                var fullPath = Path.Combine(_backupDirectory, filename);
                if (!File.Exists(fullPath))
                {
                    await command.RespondAsync($"Backup file not found: `{filename}`", ephemeral: true);
                    return;
                }

                await command.DeferAsync(ephemeral: true);

                _logger.LogInformation("=== LOAD START ===");
                _logger.LogInformation(
                    "Load requested by {UserId} into guild {GuildName} ({GuildId}) from file {FileName}",
                    command.User.Id,
                    targetGuild.Name,
                    targetGuild.Id,
                    filename);

                _logger.LogInformation("Reading backup file {FullPath}", fullPath);
                var json = await File.ReadAllTextAsync(fullPath);

                _logger.LogInformation("Deserializing backup file {FileName}", filename);
                var backup = JsonSerializer.Deserialize<GuildConfigurationBackup>(json);

                if (backup == null)
                {
                    _logger.LogWarning("Backup file {FileName} could not be deserialized.", filename);
                    await command.FollowupAsync("The backup file could not be read.", ephemeral: true);
                    return;
                }

                _logger.LogInformation(
                    "Backup loaded. SourceGuild={SourceGuildName} ({SourceGuildId}) Roles={RoleCount} Channels={ChannelCount}",
                    backup.SourceGuildName,
                    backup.SourceGuildId,
                    backup.Roles.Count,
                    backup.Channels.Count);

                var me = targetGuild.CurrentUser;
                if (me == null ||
                    !me.GuildPermissions.ManageRoles ||
                    !me.GuildPermissions.ManageChannels)
                {
                    _logger.LogWarning(
                        "Bot lacks required permissions in guild {GuildName} ({GuildId}). ManageRoles={ManageRoles} ManageChannels={ManageChannels}",
                        targetGuild.Name,
                        targetGuild.Id,
                        me?.GuildPermissions.ManageRoles,
                        me?.GuildPermissions.ManageChannels);

                    await command.FollowupAsync(
                        "The bot needs **Manage Roles** and **Manage Channels** in the target server.",
                        ephemeral: true);
                    return;
                }

                var preservedChannelIds = new HashSet<ulong>();
                ulong? preservedParentCategoryId = null;

                if (command.Channel is SocketGuildChannel currentChannel)
                {
                    preservedChannelIds.Add(currentChannel.Id);
                    _logger.LogInformation(
                        "Preserving command channel {ChannelName} ({ChannelId})",
                        currentChannel.Name,
                        currentChannel.Id);

                    if (currentChannel is SocketTextChannel currentText && currentText.Category != null)
                    {
                        preservedParentCategoryId = currentText.Category.Id;
                        _logger.LogInformation(
                            "Preserving parent category {CategoryName} ({CategoryId}) because it contains the command channel",
                            currentText.Category.Name,
                            currentText.Category.Id);
                    }
                    else if (currentChannel is SocketVoiceChannel currentVoice && currentVoice.Category != null)
                    {
                        preservedParentCategoryId = currentVoice.Category.Id;
                        _logger.LogInformation(
                            "Preserving parent category {CategoryName} ({CategoryId}) because it contains the command channel",
                            currentVoice.Category.Name,
                            currentVoice.Category.Id);
                    }
                }

                _logger.LogInformation("Stage 1: deleting existing channels");
                await DeleteExistingChannelsAsync(targetGuild, preservedChannelIds, preservedParentCategoryId);
                _logger.LogInformation("Stage 1 complete");

                _logger.LogInformation("Stage 2: deleting existing roles");
                await DeleteExistingRolesAsync(targetGuild, me);
                _logger.LogInformation("Stage 2 complete");

                _logger.LogInformation("Stage 3: creating roles");
                var roleIdMap = new Dictionary<ulong, ulong>();
                var createdRoles = new List<(ulong OriginalId, ulong NewId, int SourcePosition)>();

                var totalRoles = backup.Roles.Count;
                var roleIndex = 0;

                foreach (var roleBackup in backup.Roles.OrderByDescending(x => x.Position))
                {
                    roleIndex++;

                    try
                    {
                        _logger.LogInformation(
                            "Creating role START [{Index}/{Total}] Name={RoleName} SourcePosition={SourcePosition} Permissions={Permissions}",
                            roleIndex,
                            totalRoles,
                            roleBackup.Name,
                            roleBackup.Position,
                            roleBackup.PermissionsRawValue);

                        var createdRole = await targetGuild.CreateRoleAsync(
                            roleBackup.Name,
                            new GuildPermissions(roleBackup.PermissionsRawValue),
                            new Color(roleBackup.ColorRawValue),
                            roleBackup.IsHoisted,
                            roleBackup.IsMentionable);

                        roleIdMap[roleBackup.OriginalId] = createdRole.Id;
                        createdRoles.Add((roleBackup.OriginalId, createdRole.Id, roleBackup.Position));

                        _logger.LogInformation(
                            "Creating role DONE [{Index}/{Total}] Name={RoleName} NewRoleId={NewRoleId}",
                            roleIndex,
                            totalRoles,
                            roleBackup.Name,
                            createdRole.Id);

                        await Task.Delay(750);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Creating role FAILED [{Index}/{Total}] Name={RoleName} SourcePosition={SourcePosition}",
                            roleIndex,
                            totalRoles,
                            roleBackup.Name,
                            roleBackup.Position);
                    }
                }
                _logger.LogInformation("Stage 3 complete. CreatedRoles={CreatedRoleCount}", createdRoles.Count);

                _logger.LogInformation("Stage 4: reordering created roles");
                await ReorderCreatedRolesAsync(targetGuild, createdRoles);
                _logger.LogInformation("Stage 4 complete");

                var createdCategoryMap = new Dictionary<ulong, ulong>();
                var createdChannelMap = new Dictionary<ulong, IGuildChannel>();

                _logger.LogInformation("Stage 5: creating categories");
                foreach (var categoryBackup in backup.Channels
                             .Where(x => string.Equals(x.ChannelKind, "category", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(x => x.Position))
                {
                    try
                    {
                        _logger.LogInformation(
                            "Creating category START Name={ChannelName} Position={Position}",
                            categoryBackup.Name,
                            categoryBackup.Position);

                        var createdCategory = await targetGuild.CreateCategoryChannelAsync(categoryBackup.Name);

                        await createdCategory.ModifyAsync(props =>
                        {
                            props.Position = categoryBackup.Position;
                        });

                        await ApplyRolePermissionOverwritesAsync(
                            createdCategory,
                            categoryBackup.PermissionOverwrites,
                            targetGuild,
                            roleIdMap);

                        createdCategoryMap[categoryBackup.OriginalId] = createdCategory.Id;
                        createdChannelMap[categoryBackup.OriginalId] = createdCategory;

                        _logger.LogInformation(
                            "Creating category DONE Name={ChannelName} NewChannelId={NewChannelId}",
                            categoryBackup.Name,
                            createdCategory.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to create category {ChannelName}", categoryBackup.Name);
                    }
                }

                _logger.LogInformation("Stage 5 complete. CreatedCategories={CreatedCategoryCount}", createdCategoryMap.Count);

                _logger.LogInformation("Stage 6: creating non-category channels");
                foreach (var channelBackup in backup.Channels
                             .Where(x => !string.Equals(x.ChannelKind, "category", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(x => x.Position))
                {
                    try
                    {
                        IGuildChannel? createdChannel = null;

                        _logger.LogInformation(
                            "Creating channel START Kind={ChannelKind} Name={ChannelName} Position={Position}",
                            channelBackup.ChannelKind,
                            channelBackup.Name,
                            channelBackup.Position);

                        if (string.Equals(channelBackup.ChannelKind, "voice", StringComparison.OrdinalIgnoreCase))
                        {
                            var createdVoice = await targetGuild.CreateVoiceChannelAsync(channelBackup.Name);

                            await createdVoice.ModifyAsync(props =>
                            {
                                if (channelBackup.Bitrate.HasValue)
                                {
                                    props.Bitrate = channelBackup.Bitrate.Value;
                                }

                                if (channelBackup.UserLimit.HasValue)
                                {
                                    props.UserLimit = channelBackup.UserLimit.Value;
                                }

                                props.Position = channelBackup.Position;
                            });

                            await ApplyRolePermissionOverwritesAsync(
                                createdVoice,
                                channelBackup.PermissionOverwrites,
                                targetGuild,
                                roleIdMap);

                            createdChannel = createdVoice;
                        }
                        else if (string.Equals(channelBackup.ChannelKind, "text", StringComparison.OrdinalIgnoreCase))
                        {
                            var createdText = await targetGuild.CreateTextChannelAsync(channelBackup.Name);

                            await createdText.ModifyAsync(props =>
                            {
                                props.Topic = channelBackup.Topic;
                                props.SlowModeInterval = channelBackup.SlowModeSeconds;
                                props.Position = channelBackup.Position;
                            });

                            await ApplyRolePermissionOverwritesAsync(
                                createdText,
                                channelBackup.PermissionOverwrites,
                                targetGuild,
                                roleIdMap);

                            createdChannel = createdText;
                        }
                        else
                        {
                            _logger.LogInformation(
                                "Skipping unsupported saved channel kind {ChannelKind} for channel {ChannelName}",
                                channelBackup.ChannelKind,
                                channelBackup.Name);
                        }

                        if (createdChannel != null)
                        {
                            createdChannelMap[channelBackup.OriginalId] = createdChannel;

                            _logger.LogInformation(
                                "Creating channel DONE Kind={ChannelKind} Name={ChannelName} NewChannelId={NewChannelId}",
                                channelBackup.ChannelKind,
                                channelBackup.Name,
                                createdChannel.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to create channel {ChannelName}", channelBackup.Name);
                    }
                }

                _logger.LogInformation("Stage 6 complete. CreatedChannels={CreatedChannelCount}", createdChannelMap.Count);

                _logger.LogInformation("Stage 7: assigning parent categories");
                foreach (var channelBackup in backup.Channels
                             .Where(x => !string.Equals(x.ChannelKind, "category", StringComparison.OrdinalIgnoreCase))
                             .Where(x => x.ParentCategoryOriginalId.HasValue))
                {
                    try
                    {
                        if (!createdChannelMap.TryGetValue(channelBackup.OriginalId, out var createdChannel))
                        {
                            _logger.LogInformation(
                                "Skipping category assignment for channel {ChannelName} because the channel was not created",
                                channelBackup.Name);
                            continue;
                        }

                        if (!createdCategoryMap.TryGetValue(channelBackup.ParentCategoryOriginalId!.Value, out var parentCategoryId))
                        {
                            _logger.LogInformation(
                                "Skipping category assignment for channel {ChannelName} because the parent category was not created",
                                channelBackup.Name);
                            continue;
                        }

                        _logger.LogInformation(
                            "Assigning category START ChannelName={ChannelName} ParentCategoryId={ParentCategoryId}",
                            channelBackup.Name,
                            parentCategoryId);

                        if (createdChannel is SocketTextChannel textChannel)
                        {
                            await textChannel.ModifyAsync(props => props.CategoryId = parentCategoryId);
                        }
                        else if (createdChannel is SocketVoiceChannel voiceChannel)
                        {
                            await voiceChannel.ModifyAsync(props => props.CategoryId = parentCategoryId);
                        }

                        _logger.LogInformation(
                            "Assigning category DONE ChannelName={ChannelName} ParentCategoryId={ParentCategoryId}",
                            channelBackup.Name,
                            parentCategoryId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to assign category for channel {ChannelName}", channelBackup.Name);
                    }
                }

                _logger.LogInformation("Stage 7 complete");
                _logger.LogInformation("=== LOAD COMPLETE === TargetGuild={GuildName} ({GuildId})", targetGuild.Name, targetGuild.Id);

                await command.FollowupAsync(
                    $"Loaded configuration from `{filename}` into **{targetGuild.Name}**. Existing roles and channels were cleared first, except the channel used to run the command.",
                    ephemeral: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while loading guild configuration.");
                await SafeErrorResponseAsync(command, "An error occurred while loading the configuration.");
            }
        }

        private async Task DeleteExistingChannelsAsync(
            SocketGuild guild,
            HashSet<ulong> preservedChannelIds,
            ulong? preservedParentCategoryId)
        {
            var channels = guild.Channels.ToList();

            var nonCategoryChannels = channels
                .Where(c => c is not SocketCategoryChannel)
                .OrderByDescending(c => c.Position)
                .ToList();

            _logger.LogInformation("Deleting non-category channels. Count={Count}", nonCategoryChannels.Count);

            var channelIndex = 0;
            foreach (var channel in nonCategoryChannels)
            {
                channelIndex++;

                if (preservedChannelIds.Contains(channel.Id))
                {
                    _logger.LogInformation(
                        "Preserving current command channel {ChannelName} ({ChannelId}) [{Index}/{Total}]",
                        channel.Name,
                        channel.Id,
                        channelIndex,
                        nonCategoryChannels.Count);
                    continue;
                }

                try
                {
                    _logger.LogInformation(
                        "Deleting channel START {ChannelName} ({ChannelId}) [{Index}/{Total}]",
                        channel.Name,
                        channel.Id,
                        channelIndex,
                        nonCategoryChannels.Count);

                    await channel.DeleteAsync();

                    _logger.LogInformation(
                        "Deleting channel DONE {ChannelName} ({ChannelId}) [{Index}/{Total}]",
                        channel.Name,
                        channel.Id,
                        channelIndex,
                        nonCategoryChannels.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete channel {ChannelName} ({ChannelId})", channel.Name, channel.Id);
                }
            }

            var categories = channels
                .OfType<SocketCategoryChannel>()
                .OrderByDescending(c => c.Position)
                .ToList();

            _logger.LogInformation("Deleting categories. Count={Count}", categories.Count);

            var categoryIndex = 0;
            foreach (var category in categories)
            {
                categoryIndex++;

                if (preservedParentCategoryId.HasValue && category.Id == preservedParentCategoryId.Value)
                {
                    _logger.LogInformation(
                        "Preserving category containing the command channel {ChannelName} ({ChannelId}) [{Index}/{Total}]",
                        category.Name,
                        category.Id,
                        categoryIndex,
                        categories.Count);
                    continue;
                }

                try
                {
                    _logger.LogInformation(
                        "Deleting category START {ChannelName} ({ChannelId}) [{Index}/{Total}]",
                        category.Name,
                        category.Id,
                        categoryIndex,
                        categories.Count);

                    await category.DeleteAsync();

                    _logger.LogInformation(
                        "Deleting category DONE {ChannelName} ({ChannelId}) [{Index}/{Total}]",
                        category.Name,
                        category.Id,
                        categoryIndex,
                        categories.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete category {ChannelName} ({ChannelId})", category.Name, category.Id);
                }
            }
        }

        private async Task DeleteExistingRolesAsync(SocketGuild guild, SocketGuildUser me)
        {
            var protectedRoleIds = new HashSet<ulong>(me.Roles.Select(r => r.Id))
            {
                guild.EveryoneRole.Id
            };

            var rolesToDelete = guild.Roles
                .Where(role => !protectedRoleIds.Contains(role.Id))
                .Where(role => !role.IsManaged)
                .Where(role => role.Position < me.Hierarchy)
                .OrderByDescending(role => role.Position)
                .ToList();

            _logger.LogInformation("Deleting roles. Count={Count}", rolesToDelete.Count);

            var index = 0;
            foreach (var role in rolesToDelete)
            {
                index++;

                try
                {
                    _logger.LogInformation(
                        "Deleting role START {RoleName} ({RoleId}) Position={Position} [{Index}/{Total}]",
                        role.Name,
                        role.Id,
                        role.Position,
                        index,
                        rolesToDelete.Count);

                    await role.DeleteAsync();

                    _logger.LogInformation(
                        "Deleting role DONE {RoleName} ({RoleId}) [{Index}/{Total}]",
                        role.Name,
                        role.Id,
                        index,
                        rolesToDelete.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete role {RoleName} ({RoleId})", role.Name, role.Id);
                }
            }
        }

        private async Task ReorderCreatedRolesAsync(
            SocketGuild guild,
            List<(ulong OriginalId, ulong NewId, int SourcePosition)> createdRoles)
        {
            if (createdRoles.Count == 0)
            {
                _logger.LogInformation("No created roles to reorder.");
                return;
            }

            try
            {
                _logger.LogInformation("Reordering created roles. Count={Count}", createdRoles.Count);

                var sortedBySource = createdRoles
                    .OrderBy(x => x.SourcePosition)
                    .ToList();

                var reorderList = new List<ReorderRoleProperties>();

                for (var i = 0; i < sortedBySource.Count; i++)
                {
                    _logger.LogInformation(
                        "Role reorder target Index={Index} NewRoleId={NewRoleId} SourcePosition={SourcePosition}",
                        i + 1,
                        sortedBySource[i].NewId,
                        sortedBySource[i].SourcePosition);

                    reorderList.Add(new ReorderRoleProperties(sortedBySource[i].NewId, i + 1));
                }

                await guild.ReorderRolesAsync(reorderList);

                _logger.LogInformation("Role reorder complete.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to reorder created roles in guild {GuildId}", guild.Id);
            }
        }

        private List<RoleConfigurationBackup> BuildRoleBackup(SocketGuild guild)
        {
            var roles = guild.Roles
                .Where(r => r.Id != guild.EveryoneRole.Id && !r.IsManaged)
                .OrderByDescending(r => r.Position)
                .Select(r =>
                {
                    _logger.LogInformation(
                        "Backing up role Name={RoleName} RoleId={RoleId} Position={Position}",
                        r.Name,
                        r.Id,
                        r.Position);

                    return new RoleConfigurationBackup
                    {
                        OriginalId = r.Id,
                        Name = r.Name,
                        ColorRawValue = r.Color.RawValue,
                        IsHoisted = r.IsHoisted,
                        IsMentionable = r.IsMentionable,
                        PermissionsRawValue = r.Permissions.RawValue,
                        Position = r.Position
                    };
                })
                .ToList();

            return roles;
        }

        private List<ChannelConfigurationBackup> BuildChannelBackup(SocketGuild guild)
        {
            var channels = new List<ChannelConfigurationBackup>();
            var orderedChannels = guild.Channels.OrderBy(c => c.Position).ToList();

            _logger.LogInformation("Starting channel backup. ChannelCount={Count}", orderedChannels.Count);

            var index = 0;
            foreach (var channel in orderedChannels)
            {
                index++;

                if (channel is SocketThreadChannel)
                {
                    _logger.LogInformation(
                        "Skipping thread channel during backup {ChannelName} ({ChannelId}) [{Index}/{Total}]",
                        channel.Name,
                        channel.Id,
                        index,
                        orderedChannels.Count);
                    continue;
                }

                if (channel is SocketCategoryChannel categoryChannel)
                {
                    _logger.LogInformation(
                        "Backing up category {ChannelName} ({ChannelId}) Position={Position} [{Index}/{Total}]",
                        categoryChannel.Name,
                        categoryChannel.Id,
                        categoryChannel.Position,
                        index,
                        orderedChannels.Count);

                    channels.Add(new ChannelConfigurationBackup
                    {
                        OriginalId = categoryChannel.Id,
                        Name = categoryChannel.Name,
                        ChannelKind = "category",
                        Position = categoryChannel.Position,
                        PermissionOverwrites = BuildPermissionOverwrites(categoryChannel)
                    });

                    continue;
                }

                if (channel is SocketVoiceChannel voiceChannel)
                {
                    _logger.LogInformation(
                        "Backing up voice channel {ChannelName} ({ChannelId}) Position={Position} ParentCategoryId={ParentCategoryId} [{Index}/{Total}]",
                        voiceChannel.Name,
                        voiceChannel.Id,
                        voiceChannel.Position,
                        voiceChannel.Category?.Id,
                        index,
                        orderedChannels.Count);

                    channels.Add(new ChannelConfigurationBackup
                    {
                        OriginalId = voiceChannel.Id,
                        Name = voiceChannel.Name,
                        ChannelKind = "voice",
                        Position = voiceChannel.Position,
                        ParentCategoryOriginalId = voiceChannel.Category?.Id,
                        Bitrate = voiceChannel.Bitrate,
                        UserLimit = voiceChannel.UserLimit,
                        PermissionOverwrites = BuildPermissionOverwrites(voiceChannel)
                    });

                    continue;
                }

                if (channel is SocketTextChannel textChannel)
                {
                    _logger.LogInformation(
                        "Backing up text channel {ChannelName} ({ChannelId}) Position={Position} ParentCategoryId={ParentCategoryId} [{Index}/{Total}]",
                        textChannel.Name,
                        textChannel.Id,
                        textChannel.Position,
                        textChannel.Category?.Id,
                        index,
                        orderedChannels.Count);

                    channels.Add(new ChannelConfigurationBackup
                    {
                        OriginalId = textChannel.Id,
                        Name = textChannel.Name,
                        ChannelKind = "text",
                        Position = textChannel.Position,
                        ParentCategoryOriginalId = textChannel.Category?.Id,
                        Topic = textChannel.Topic,
                        IsNsfw = textChannel.IsNsfw,
                        SlowModeSeconds = textChannel.SlowModeInterval,
                        PermissionOverwrites = BuildPermissionOverwrites(textChannel)
                    });

                    continue;
                }

                _logger.LogInformation(
                    "Skipping unsupported channel type {ChannelType} for channel {ChannelName} ({ChannelId}) [{Index}/{Total}]",
                    channel.GetType().Name,
                    channel.Name,
                    channel.Id,
                    index,
                    orderedChannels.Count);
            }

            return channels;
        }

        private List<PermissionOverwriteBackup> BuildPermissionOverwrites(SocketGuildChannel channel)
        {
            var list = new List<PermissionOverwriteBackup>();

            if (channel is SocketThreadChannel)
            {
                _logger.LogInformation(
                    "Skipping permission overwrites for thread channel {ChannelName} ({ChannelId})",
                    channel.Name,
                    channel.Id);
                return list;
            }

            var overwrites = channel.PermissionOverwrites.ToList();
            _logger.LogInformation(
                "Reading permission overwrites for channel {ChannelName} ({ChannelId}). OverwriteCount={Count}",
                channel.Name,
                channel.Id,
                overwrites.Count);

            foreach (var overwrite in overwrites)
            {
                if (overwrite.TargetType != PermissionTarget.Role)
                {
                    continue;
                }

                list.Add(new PermissionOverwriteBackup
                {
                    RoleOriginalId = overwrite.TargetId,
                    AllowValue = overwrite.Permissions.AllowValue,
                    DenyValue = overwrite.Permissions.DenyValue
                });
            }

            _logger.LogInformation(
                "Role-based permission overwrites saved for channel {ChannelName} ({ChannelId}). SavedCount={Count}",
                channel.Name,
                channel.Id,
                list.Count);

            return list;
        }

        private async Task ApplyRolePermissionOverwritesAsync(
            IGuildChannel channel,
            List<PermissionOverwriteBackup> overwrites,
            SocketGuild guild,
            Dictionary<ulong, ulong> roleIdMap)
        {
            if (overwrites.Count == 0)
            {
                _logger.LogInformation(
                    "No permission overwrites to apply for channel {ChannelName} ({ChannelId})",
                    channel.Name,
                    channel.Id);
                return;
            }

            _logger.LogInformation(
                "Applying permission overwrites for channel {ChannelName} ({ChannelId}). OverwriteCount={Count}",
                channel.Name,
                channel.Id,
                overwrites.Count);

            var index = 0;
            foreach (var overwrite in overwrites)
            {
                index++;

                try
                {
                    if (!roleIdMap.TryGetValue(overwrite.RoleOriginalId, out var mappedRoleId))
                    {
                        _logger.LogInformation(
                            "Skipping overwrite {Index}/{Total} for original role {OriginalRoleId} because no mapped role exists",
                            index,
                            overwrites.Count,
                            overwrite.RoleOriginalId);
                        continue;
                    }

                    var role = guild.GetRole(mappedRoleId);
                    if (role == null)
                    {
                        _logger.LogInformation(
                            "Skipping overwrite {Index}/{Total} because mapped role {MappedRoleId} was not found",
                            index,
                            overwrites.Count,
                            mappedRoleId);
                        continue;
                    }

                    _logger.LogInformation(
                        "Applying overwrite START Channel={ChannelName} Role={RoleName} ({RoleId}) [{Index}/{Total}]",
                        channel.Name,
                        role.Name,
                        role.Id,
                        index,
                        overwrites.Count);

                    var permissions = new OverwritePermissions(
                        allowValue: overwrite.AllowValue,
                        denyValue: overwrite.DenyValue);

                    await channel.AddPermissionOverwriteAsync(role, permissions);

                    _logger.LogInformation(
                        "Applying overwrite DONE Channel={ChannelName} Role={RoleName} ({RoleId}) [{Index}/{Total}]",
                        channel.Name,
                        role.Name,
                        role.Id,
                        index,
                        overwrites.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to apply overwrite for role {RoleId}", overwrite.RoleOriginalId);
                }
            }
        }

        private async Task SafeErrorResponseAsync(SocketSlashCommand command, string message)
        {
            try
            {
                if (command.HasResponded)
                {
                    await command.FollowupAsync(message, ephemeral: true);
                }
                else
                {
                    await command.RespondAsync(message, ephemeral: true);
                }
            }
            catch
            {
            }
        }

        private static string SanitizeFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var cleaned = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(cleaned) ? "guild" : cleaned;
        }
    }

    public class GuildConfigurationBackup
    {
        public int Version { get; set; }
        public DateTime ExportedAtUtc { get; set; }
        public ulong SourceGuildId { get; set; }
        public string SourceGuildName { get; set; } = string.Empty;
        public List<RoleConfigurationBackup> Roles { get; set; } = new();
        public List<ChannelConfigurationBackup> Channels { get; set; } = new();
    }

    public class RoleConfigurationBackup
    {
        public ulong OriginalId { get; set; }
        public string Name { get; set; } = string.Empty;
        public uint ColorRawValue { get; set; }
        public bool IsHoisted { get; set; }
        public bool IsMentionable { get; set; }
        public ulong PermissionsRawValue { get; set; }
        public int Position { get; set; }
    }

    public class ChannelConfigurationBackup
    {
        public ulong OriginalId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ChannelKind { get; set; } = string.Empty;
        public int Position { get; set; }
        public ulong? ParentCategoryOriginalId { get; set; }
        public string? Topic { get; set; }
        public bool IsNsfw { get; set; }
        public int SlowModeSeconds { get; set; }
        public int? Bitrate { get; set; }
        public int? UserLimit { get; set; }
        public List<PermissionOverwriteBackup> PermissionOverwrites { get; set; } = new();
    }

    public class PermissionOverwriteBackup
    {
        public ulong RoleOriginalId { get; set; }
        public ulong AllowValue { get; set; }
        public ulong DenyValue { get; set; }
    }
}