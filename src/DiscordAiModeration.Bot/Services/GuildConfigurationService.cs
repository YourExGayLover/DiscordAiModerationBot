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
                if (sourceServerOption?.Value != null &&
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

                LogInfo("=== BACKUP START ===");
                LogInfo($"Source guild: {sourceGuild.Name} ({sourceGuild.Id})");

                var backup = new GuildConfigurationBackup
                {
                    Version = 4,
                    ExportedAtUtc = DateTime.UtcNow,
                    SourceGuildId = sourceGuild.Id,
                    SourceGuildName = sourceGuild.Name,
                    Roles = BuildRoleBackup(sourceGuild),
                    Channels = BuildChannelBackup(sourceGuild)
                };

                var fileName = $"{sourceGuild.Id}-{SanitizeFileName(sourceGuild.Name)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
                var fullPath = Path.Combine(_backupDirectory, fileName);
                var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(fullPath, json);

                LogInfo($"=== BACKUP COMPLETE === File={fileName}");
                await SafeFollowupAsync(command, $"Backed up **{sourceGuild.Name}** to `{fileName}`.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while backing up guild configuration.");
                Console.WriteLine($"[GuildConfigurationService] BACKUP ERROR: {ex}");
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

                var filename = command.Data.Options.FirstOrDefault(x => x.Name == "filename")?.Value?.ToString();
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
                LogInfo("=== LOAD START ===");
                LogInfo($"Target guild: {targetGuild.Name} ({targetGuild.Id})");
                LogInfo($"Backup file: {fullPath}");

                var json = await File.ReadAllTextAsync(fullPath);
                var backup = JsonSerializer.Deserialize<GuildConfigurationBackup>(json);
                if (backup == null)
                {
                    await SafeFollowupAsync(command, "The backup file could not be read.");
                    return;
                }

                LogInfo($"Backup loaded. Source={backup.SourceGuildName} ({backup.SourceGuildId}) Roles={backup.Roles.Count} Channels={backup.Channels.Count}");

                var me = targetGuild.CurrentUser;
                if (me == null || !me.GuildPermissions.ManageRoles || !me.GuildPermissions.ManageChannels)
                {
                    await SafeFollowupAsync(command, "The bot needs Manage Roles and Manage Channels in the target server.");
                    return;
                }

                var preservedChannelIds = new HashSet<ulong>();
                ulong? preservedParentCategoryId = null;
                if (command.Channel is SocketGuildChannel currentChannel)
                {
                    preservedChannelIds.Add(currentChannel.Id);
                    LogInfo($"Preserving command channel: {currentChannel.Name} ({currentChannel.Id})");

                    if (currentChannel is SocketTextChannel currentText && currentText.Category != null)
                    {
                        preservedParentCategoryId = currentText.Category.Id;
                    }
                    else if (currentChannel is SocketVoiceChannel currentVoice && currentVoice.Category != null)
                    {
                        preservedParentCategoryId = currentVoice.Category.Id;
                    }
                }

                LogInfo("Stage 1: deleting existing channels");
                await DeleteExistingChannelsAsync(targetGuild, preservedChannelIds, preservedParentCategoryId);
                LogInfo("Stage 1 complete");

                LogInfo("Stage 2: deleting existing roles");
                await DeleteExistingRolesAsync(targetGuild, me);
                LogInfo("Stage 2 complete");

                LogInfo("Stage 3: creating roles");
                var roleIdMap = new Dictionary<ulong, ulong>();
                var createdRoles = new List<(RoleConfigurationBackup Backup, ulong NewId)>();

                var orderedRoles = backup.Roles
                    .OrderBy(r => HasAdministratorPermission(r) ? 1 : 0)
                    .ThenByDescending(r => r.Position)
                    .ToList();

                for (var i = 0; i < orderedRoles.Count; i++)
                {
                    var roleBackup = orderedRoles[i];
                    var createdRole = await CreateRoleWithRetriesAsync(targetGuild, roleBackup, i + 1, orderedRoles.Count);
                    if (createdRole == null)
                    {
                        await SendChannelNoticeAsync(command, $"Load stopped because role **{roleBackup.Name}** could not be created after multiple retries.");
                        return;
                    }

                    roleIdMap[roleBackup.OriginalId] = createdRole.Id;
                    createdRoles.Add((roleBackup, createdRole.Id));
                }

                LogInfo("Stage 4: reordering roles");
                await ReorderCreatedRolesAsync(targetGuild, createdRoles);
                LogInfo("Stage 4 complete");

                LogInfo("Stage 5: applying role properties");
                await ApplyCreatedRolePropertiesAsync(targetGuild, createdRoles);
                LogInfo("Stage 5 complete");

                var createdCategoryMap = new Dictionary<ulong, ulong>();
                var createdChannelMap = new Dictionary<ulong, IGuildChannel>();

                LogInfo("Stage 6: creating categories");
                foreach (var categoryBackup in backup.Channels.Where(x => x.ChannelKind == "category").OrderBy(x => x.Position))
                {
                    try
                    {
                        var createdCategory = await RunWithHeartbeatAndTimeoutAsync(
                            () => targetGuild.CreateCategoryChannelAsync(categoryBackup.Name),
                            $"CreateCategory:{categoryBackup.Name}",
                            10000,
                            120000);

                        await createdCategory.ModifyAsync(props => props.Position = categoryBackup.Position);
                        await ApplyRolePermissionOverwritesAsync(createdCategory, categoryBackup.PermissionOverwrites, targetGuild, roleIdMap);
                        createdCategoryMap[categoryBackup.OriginalId] = createdCategory.Id;
                        createdChannelMap[categoryBackup.OriginalId] = createdCategory;
                        LogInfo($"Creating category DONE Name={categoryBackup.Name} NewChannelId={createdCategory.Id}");
                        await Task.Delay(2000);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to create category {ChannelName}", categoryBackup.Name);
                        Console.WriteLine($"[GuildConfigurationService] Failed to create category {categoryBackup.Name}: {ex}");
                    }
                }

                LogInfo("Stage 7: creating non-category channels");
                foreach (var channelBackup in backup.Channels.Where(x => x.ChannelKind != "category").OrderBy(x => x.Position))
                {
                    try
                    {
                        IGuildChannel? createdChannel = null;

                        if (channelBackup.ChannelKind == "voice")
                        {
                            var createdVoice = await RunWithHeartbeatAndTimeoutAsync(
                                () => targetGuild.CreateVoiceChannelAsync(channelBackup.Name),
                                $"CreateVoice:{channelBackup.Name}",
                                10000,
                                120000);

                            await createdVoice.ModifyAsync(props =>
                            {
                                if (channelBackup.Bitrate.HasValue) props.Bitrate = channelBackup.Bitrate.Value;
                                if (channelBackup.UserLimit.HasValue) props.UserLimit = channelBackup.UserLimit.Value;
                                props.Position = channelBackup.Position;
                            });

                            await ApplyRolePermissionOverwritesAsync(createdVoice, channelBackup.PermissionOverwrites, targetGuild, roleIdMap);
                            createdChannel = createdVoice;
                        }
                        else if (channelBackup.ChannelKind == "text")
                        {
                            var createdText = await RunWithHeartbeatAndTimeoutAsync(
                                () => targetGuild.CreateTextChannelAsync(channelBackup.Name),
                                $"CreateText:{channelBackup.Name}",
                                10000,
                                120000);

                            await createdText.ModifyAsync(props =>
                            {
                                props.Topic = channelBackup.Topic;
                                props.SlowModeInterval = channelBackup.SlowModeSeconds;
                                props.Position = channelBackup.Position;
                            });

                            await ApplyRolePermissionOverwritesAsync(createdText, channelBackup.PermissionOverwrites, targetGuild, roleIdMap);
                            createdChannel = createdText;
                        }

                        if (createdChannel != null)
                        {
                            createdChannelMap[channelBackup.OriginalId] = createdChannel;
                            LogInfo($"Creating channel DONE Kind={channelBackup.ChannelKind} Name={channelBackup.Name} NewChannelId={createdChannel.Id}");
                            await Task.Delay(1500);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to create channel {ChannelName}", channelBackup.Name);
                        Console.WriteLine($"[GuildConfigurationService] Failed to create channel {channelBackup.Name}: {ex}");
                    }
                }

                LogInfo("Stage 8: assigning parent categories");
                foreach (var channelBackup in backup.Channels.Where(x => x.ChannelKind != "category" && x.ParentCategoryOriginalId.HasValue))
                {
                    try
                    {
                        if (!createdChannelMap.TryGetValue(channelBackup.OriginalId, out var createdChannel))
                        {
                            continue;
                        }

                        if (!createdCategoryMap.TryGetValue(channelBackup.ParentCategoryOriginalId!.Value, out var parentCategoryId))
                        {
                            continue;
                        }

                        if (createdChannel is SocketTextChannel textChannel)
                        {
                            await textChannel.ModifyAsync(props => props.CategoryId = parentCategoryId);
                        }
                        else if (createdChannel is SocketVoiceChannel voiceChannel)
                        {
                            await voiceChannel.ModifyAsync(props => props.CategoryId = parentCategoryId);
                        }

                        await Task.Delay(750);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to assign category for channel {ChannelName}", channelBackup.Name);
                        Console.WriteLine($"[GuildConfigurationService] Failed to assign category for channel {channelBackup.Name}: {ex}");
                    }
                }

                LogInfo("=== LOAD COMPLETE ===");
                await SendChannelNoticeAsync(command, $"Configuration load finished for **{targetGuild.Name}** from `{filename}`.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while loading guild configuration.");
                Console.WriteLine($"[GuildConfigurationService] LOAD ERROR: {ex}");
                await SafeErrorResponseAsync(command, "An error occurred while loading the configuration.");
            }
        }

        private async Task SendChannelNoticeAsync(SocketSlashCommand command, string text)
        {
            try
            {
                if (command.Channel is IMessageChannel messageChannel)
                {
                    await messageChannel.SendMessageAsync(text);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send channel notice");
                Console.WriteLine($"[GuildConfigurationService] Failed to send channel notice: {ex}");
            }
        }

        private async Task SafeFollowupAsync(SocketSlashCommand command, string text)
        {
            try
            {
                if (!command.HasResponded)
                {
                    await command.RespondAsync(text, ephemeral: true);
                    return;
                }

                await command.FollowupAsync(text, ephemeral: true);
            }
            catch
            {
                await SendChannelNoticeAsync(command, text);
            }
        }

        private async Task DeleteExistingChannelsAsync(SocketGuild guild, HashSet<ulong> preservedChannelIds, ulong? preservedParentCategoryId)
        {
            var channels = guild.Channels.ToList();

            foreach (var channel in channels.Where(c => c is not SocketCategoryChannel).OrderByDescending(c => c.Position))
            {
                if (preservedChannelIds.Contains(channel.Id))
                {
                    LogInfo($"Preserving command channel {channel.Name} ({channel.Id})");
                    continue;
                }

                try
                {
                    LogInfo($"Deleting channel START {channel.Name} ({channel.Id}) Position={channel.Position}");
                    await channel.DeleteAsync();
                    LogInfo($"Deleting channel DONE {channel.Name} ({channel.Id})");
                    await Task.Delay(300);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete channel {ChannelName} ({ChannelId})", channel.Name, channel.Id);
                    Console.WriteLine($"[GuildConfigurationService] Failed to delete channel {channel.Name}: {ex}");
                }
            }

            foreach (var category in channels.OfType<SocketCategoryChannel>().OrderByDescending(c => c.Position))
            {
                if (preservedParentCategoryId.HasValue && category.Id == preservedParentCategoryId.Value)
                {
                    LogInfo($"Preserving category containing the command channel {category.Name} ({category.Id})");
                    continue;
                }

                try
                {
                    LogInfo($"Deleting category START {category.Name} ({category.Id}) Position={category.Position}");
                    await category.DeleteAsync();
                    LogInfo($"Deleting category DONE {category.Name} ({category.Id})");
                    await Task.Delay(300);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete category {ChannelName} ({ChannelId})", category.Name, category.Id);
                    Console.WriteLine($"[GuildConfigurationService] Failed to delete category {category.Name}: {ex}");
                }
            }
        }

        private async Task DeleteExistingRolesAsync(SocketGuild guild, SocketGuildUser me)
        {
            var protectedRoleIds = new HashSet<ulong>(me.Roles.Select(r => r.Id)) { guild.EveryoneRole.Id };

            var rolesToDelete = guild.Roles
                .Where(role => !protectedRoleIds.Contains(role.Id))
                .Where(role => !role.IsManaged)
                .Where(role => role.Position < me.Hierarchy)
                .OrderByDescending(role => role.Position)
                .ToList();

            foreach (var role in rolesToDelete)
            {
                try
                {
                    LogInfo($"Deleting role START {role.Name} ({role.Id}) Position={role.Position}");
                    await role.DeleteAsync();
                    LogInfo($"Deleting role DONE {role.Name} ({role.Id})");
                    await Task.Delay(300);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete role {RoleName} ({RoleId})", role.Name, role.Id);
                    Console.WriteLine($"[GuildConfigurationService] Failed to delete role {role.Name}: {ex}");
                }
            }
        }

        private async Task<IRole?> CreateRoleWithRetriesAsync(SocketGuild guild, RoleConfigurationBackup roleBackup, int roleIndex, int totalRoles)
        {
            const int maxAttempts = 5;
            const int waitBetweenAttemptsMs = 15000;
            const int heartbeatMs = 10000;
            const int perAttemptTimeoutMs = 120000;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    LogInfo($"Creating role START [{roleIndex}/{totalRoles}] Attempt={attempt}/{maxAttempts} Name={roleBackup.Name} SourcePosition={roleBackup.Position} IsAdmin={HasAdministratorPermission(roleBackup)}");

                    var createdRole = await RunWithHeartbeatAndTimeoutAsync(
                        () => guild.CreateRoleAsync(roleBackup.Name),
                        $"CreateRole:{roleBackup.Name}:Attempt{attempt}",
                        heartbeatMs,
                        perAttemptTimeoutMs);

                    LogInfo($"Creating role DONE [{roleIndex}/{totalRoles}] Attempt={attempt}/{maxAttempts} Name={roleBackup.Name} NewRoleId={createdRole.Id}");
                    await Task.Delay(waitBetweenAttemptsMs);
                    return createdRole;
                }
                catch (TimeoutException ex)
                {
                    _logger.LogWarning(ex, "Creating role TIMED OUT [{Index}/{Total}] Attempt={Attempt}/{MaxAttempts} Name={RoleName}", roleIndex, totalRoles, attempt, maxAttempts, roleBackup.Name);
                    Console.WriteLine($"[GuildConfigurationService] Creating role TIMED OUT [{roleIndex}/{totalRoles}] Attempt={attempt}/{maxAttempts} {roleBackup.Name}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Creating role FAILED [{Index}/{Total}] Attempt={Attempt}/{MaxAttempts} Name={RoleName}", roleIndex, totalRoles, attempt, maxAttempts, roleBackup.Name);
                    Console.WriteLine($"[GuildConfigurationService] Creating role FAILED [{roleIndex}/{totalRoles}] Attempt={attempt}/{maxAttempts} {roleBackup.Name}: {ex}");
                }

                if (attempt < maxAttempts)
                {
                    LogInfo($"Retrying role create after wait. Name={roleBackup.Name} NextAttempt={attempt + 1}/{maxAttempts}");
                    await Task.Delay(waitBetweenAttemptsMs);
                }
            }

            LogInfo($"Creating role GAVE UP Name={roleBackup.Name}");
            return null;
        }

        private async Task ReorderCreatedRolesAsync(SocketGuild guild, List<(RoleConfigurationBackup Backup, ulong NewId)> createdRoles)
        {
            if (createdRoles.Count == 0)
            {
                LogInfo("No created roles to reorder.");
                return;
            }

            try
            {
                var sortedBySource = createdRoles.OrderBy(x => x.Backup.Position).ToList();
                var reorderList = new List<ReorderRoleProperties>();

                for (var i = 0; i < sortedBySource.Count; i++)
                {
                    reorderList.Add(new ReorderRoleProperties(sortedBySource[i].NewId, i + 1));
                }

                await guild.ReorderRolesAsync(reorderList);
                LogInfo("Role reorder complete.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to reorder created roles in guild {GuildId}", guild.Id);
                Console.WriteLine($"[GuildConfigurationService] Failed to reorder created roles: {ex}");
            }
        }

        private async Task ApplyCreatedRolePropertiesAsync(SocketGuild guild, List<(RoleConfigurationBackup Backup, ulong NewId)> createdRoles)
        {
            var orderedRolesForModify = createdRoles
                .OrderBy(r => HasAdministratorPermission(r.Backup) ? 1 : 0)
                .ThenByDescending(r => r.Backup.Position)
                .ToList();

            for (var index = 0; index < orderedRolesForModify.Count; index++)
            {
                var item = orderedRolesForModify[index];
                try
                {
                    var role = guild.GetRole(item.NewId);
                    if (role == null)
                    {
                        continue;
                    }

                    await RunWithHeartbeatAndTimeoutAsync(
                        () => role.ModifyAsync((Action<RoleProperties>)(props =>
                        {
                            props.Color = new Color(item.Backup.ColorRawValue);
                            props.Hoist = item.Backup.IsHoisted;
                            props.Mentionable = item.Backup.IsMentionable;
                            props.Permissions = new GuildPermissions(item.Backup.PermissionsRawValue);
                        })),
                        $"ModifyRole:{item.Backup.Name}",
                        10000,
                        120000);

                    LogInfo($"Applying role properties DONE [{index + 1}/{orderedRolesForModify.Count}] Name={item.Backup.Name}");
                    await Task.Delay(8000);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to apply properties for role {RoleName}", item.Backup.Name);
                    Console.WriteLine($"[GuildConfigurationService] Applying role properties FAILED [{index + 1}/{orderedRolesForModify.Count}] {item.Backup.Name}: {ex}");
                }
            }
        }

        private List<RoleConfigurationBackup> BuildRoleBackup(SocketGuild guild)
        {
            return guild.Roles
                .Where(r => r.Id != guild.EveryoneRole.Id && !r.IsManaged)
                .OrderByDescending(r => r.Position)
                .Select(r => new RoleConfigurationBackup
                {
                    OriginalId = r.Id,
                    Name = r.Name,
                    ColorRawValue = r.Color.RawValue,
                    IsHoisted = r.IsHoisted,
                    IsMentionable = r.IsMentionable,
                    PermissionsRawValue = r.Permissions.RawValue,
                    Position = r.Position
                })
                .ToList();
        }

        private List<ChannelConfigurationBackup> BuildChannelBackup(SocketGuild guild)
        {
            var channels = new List<ChannelConfigurationBackup>();

            foreach (var channel in guild.Channels.OrderBy(c => c.Position))
            {
                if (channel is SocketThreadChannel)
                {
                    LogInfo($"Skipping thread channel during backup {channel.Name} ({channel.Id})");
                    continue;
                }

                if (channel is SocketCategoryChannel categoryChannel)
                {
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

                LogInfo($"Skipping unsupported channel type {channel.GetType().Name} for channel {channel.Name} ({channel.Id})");
            }

            return channels;
        }

        private List<PermissionOverwriteBackup> BuildPermissionOverwrites(SocketGuildChannel channel)
        {
            var list = new List<PermissionOverwriteBackup>();
            if (channel is SocketThreadChannel)
            {
                return list;
            }

            foreach (var overwrite in channel.PermissionOverwrites)
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

            return list;
        }

        private async Task ApplyRolePermissionOverwritesAsync(IGuildChannel channel, List<PermissionOverwriteBackup> overwrites, SocketGuild guild, Dictionary<ulong, ulong> roleIdMap)
        {
            if (overwrites.Count == 0)
            {
                return;
            }

            foreach (var overwrite in overwrites)
            {
                try
                {
                    if (!roleIdMap.TryGetValue(overwrite.RoleOriginalId, out var mappedRoleId))
                    {
                        continue;
                    }

                    var role = guild.GetRole(mappedRoleId);
                    if (role == null)
                    {
                        continue;
                    }

                    var permissions = new OverwritePermissions(overwrite.AllowValue, overwrite.DenyValue);
                    await channel.AddPermissionOverwriteAsync(role, permissions);
                    await Task.Delay(250);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to apply overwrite for role {RoleId}", overwrite.RoleOriginalId);
                    Console.WriteLine($"[GuildConfigurationService] Failed to apply overwrite for original role {overwrite.RoleOriginalId}: {ex}");
                }
            }
        }

        private async Task<T> RunWithHeartbeatAndTimeoutAsync<T>(Func<Task<T>> action, string operationName, int heartbeatMs, int timeoutMs)
        {
            var task = action();
            var startedUtc = DateTime.UtcNow;

            while (!task.IsCompleted)
            {
                var completed = await Task.WhenAny(task, Task.Delay(heartbeatMs));
                if (completed == task)
                {
                    break;
                }

                var elapsed = DateTime.UtcNow - startedUtc;
                LogInfo($"Still waiting for operation: {operationName} Elapsed={elapsed.TotalSeconds:F0}s");
                if (elapsed.TotalMilliseconds >= timeoutMs)
                {
                    throw new TimeoutException($"TIMEOUT waiting for operation: {operationName} after {elapsed.TotalSeconds:F0}s");
                }
            }

            return await task;
        }

        private async Task RunWithHeartbeatAndTimeoutAsync(Func<Task> action, string operationName, int heartbeatMs, int timeoutMs)
        {
            var task = action();
            var startedUtc = DateTime.UtcNow;

            while (!task.IsCompleted)
            {
                var completed = await Task.WhenAny(task, Task.Delay(heartbeatMs));
                if (completed == task)
                {
                    break;
                }

                var elapsed = DateTime.UtcNow - startedUtc;
                LogInfo($"Still waiting for operation: {operationName} Elapsed={elapsed.TotalSeconds:F0}s");
                if (elapsed.TotalMilliseconds >= timeoutMs)
                {
                    throw new TimeoutException($"TIMEOUT waiting for operation: {operationName} after {elapsed.TotalSeconds:F0}s");
                }
            }

            await task;
        }

        private bool HasAdministratorPermission(RoleConfigurationBackup role)
        {
            return new GuildPermissions(role.PermissionsRawValue).Administrator;
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
                await SendChannelNoticeAsync(command, message);
            }
        }

        private void LogInfo(string message)
        {
            _logger.LogInformation(message);
            Console.WriteLine($"[GuildConfigurationService] {message}");
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
