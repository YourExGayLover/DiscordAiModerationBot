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

                LogProgress("=== BACKUP START ===");
                LogProgress($"Source guild: {sourceGuild.Name} ({sourceGuild.Id})");

                var roles = BuildRoleBackup(sourceGuild);
                LogProgress($"Built role backup. Count={roles.Count}");

                var channels = BuildChannelBackup(sourceGuild);
                LogProgress($"Built channel backup. Count={channels.Count}");

                var backup = new GuildConfigurationBackup
                {
                    Version = 4,
                    ExportedAtUtc = DateTime.UtcNow,
                    SourceGuildId = sourceGuild.Id,
                    SourceGuildName = sourceGuild.Name,
                    Roles = roles,
                    Channels = channels
                };

                var fileName = $"{sourceGuild.Id}-{SanitizeFileName(sourceGuild.Name)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
                var fullPath = Path.Combine(_backupDirectory, fileName);

                LogProgress($"Writing backup file to {fullPath}");

                var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(fullPath, json);

                LogProgress($"=== BACKUP COMPLETE === File={fileName}");

                await command.FollowupAsync(
                    $"Backed up **{sourceGuild.Name}** to `{fileName}`.",
                    ephemeral: true);
            }
            catch (Exception ex)
            {
                LogError(ex, "Error while backing up guild configuration.");
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

                LogProgress("=== LOAD START ===");
                LogProgress($"Target guild: {targetGuild.Name} ({targetGuild.Id})");
                LogProgress($"Backup file: {fullPath}");

                var json = await File.ReadAllTextAsync(fullPath);
                var backup = JsonSerializer.Deserialize<GuildConfigurationBackup>(json);

                if (backup == null)
                {
                    LogProgress("Backup deserialization returned null.");
                    await command.FollowupAsync("The backup file could not be read.", ephemeral: true);
                    return;
                }

                LogProgress($"Backup loaded. Source={backup.SourceGuildName} ({backup.SourceGuildId}) Roles={backup.Roles.Count} Channels={backup.Channels.Count}");

                var me = targetGuild.CurrentUser;
                if (me == null || !me.GuildPermissions.ManageRoles || !me.GuildPermissions.ManageChannels)
                {
                    LogProgress("Bot is missing Manage Roles or Manage Channels in the target guild.");
                    await command.FollowupAsync("The bot needs **Manage Roles** and **Manage Channels** in the target server.", ephemeral: true);
                    return;
                }

                var preservedChannelIds = new HashSet<ulong>();
                ulong? preservedParentCategoryId = null;

                if (command.Channel is SocketGuildChannel currentChannel)
                {
                    preservedChannelIds.Add(currentChannel.Id);
                    LogProgress($"Preserving command channel: {currentChannel.Name} ({currentChannel.Id})");

                    if (currentChannel is SocketTextChannel currentText && currentText.Category != null)
                    {
                        preservedParentCategoryId = currentText.Category.Id;
                    }
                    else if (currentChannel is SocketVoiceChannel currentVoice && currentVoice.Category != null)
                    {
                        preservedParentCategoryId = currentVoice.Category.Id;
                    }
                }

                LogProgress("Stage 1: deleting existing channels");
                await DeleteExistingChannelsAsync(targetGuild, preservedChannelIds, preservedParentCategoryId);
                LogProgress("Stage 1 complete");

                LogProgress("Stage 2: deleting existing roles");
                await DeleteExistingRolesAsync(targetGuild, me);
                LogProgress("Stage 2 complete");

                LogProgress("Stage 3: creating roles");
                var roleIdMap = new Dictionary<ulong, ulong>();
                var createdRoles = new List<(ulong OriginalId, ulong NewId, int SourcePosition)>();

                var orderedRoles = backup.Roles.OrderByDescending(x => x.Position).ToList();

                for (var i = 0; i < orderedRoles.Count; i++)
                {
                    var roleBackup = orderedRoles[i];
                    try
                    {
                        LogProgress($"Creating role START [{i + 1}/{orderedRoles.Count}] Name={roleBackup.Name} SourcePosition={roleBackup.Position}");

                        var createdRole = await targetGuild.CreateRoleAsync(
                            roleBackup.Name,
                            new GuildPermissions(roleBackup.PermissionsRawValue),
                            new Color(roleBackup.ColorRawValue),
                            roleBackup.IsHoisted,
                            roleBackup.IsMentionable);

                        roleIdMap[roleBackup.OriginalId] = createdRole.Id;
                        createdRoles.Add((roleBackup.OriginalId, createdRole.Id, roleBackup.Position));

                        LogProgress($"Creating role DONE [{i + 1}/{orderedRoles.Count}] Name={roleBackup.Name} NewRoleId={createdRole.Id}");

                        await Task.Delay(500);
                    }
                    catch (Exception ex)
                    {
                        LogError(ex, $"Creating role FAILED [{i + 1}/{orderedRoles.Count}] Name={roleBackup.Name} SourcePosition={roleBackup.Position}");
                    }
                }

                LogProgress("Stage 4: reordering created roles");
                await ReorderCreatedRolesAsync(targetGuild, createdRoles);
                LogProgress("Stage 4 complete");

                var createdCategoryMap = new Dictionary<ulong, ulong>();
                var createdChannelMap = new Dictionary<ulong, IGuildChannel>();

                LogProgress("Stage 5: creating categories");
                foreach (var categoryBackup in backup.Channels.Where(x => string.Equals(x.ChannelKind, "category", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Position))
                {
                    try
                    {
                        LogProgress($"Creating category START Name={categoryBackup.Name}");

                        var createdCategory = await targetGuild.CreateCategoryChannelAsync(categoryBackup.Name);
                        await createdCategory.ModifyAsync(props => { props.Position = categoryBackup.Position; });

                        createdCategoryMap[categoryBackup.OriginalId] = createdCategory.Id;
                        createdChannelMap[categoryBackup.OriginalId] = createdCategory;

                        LogProgress($"Creating category DONE Name={categoryBackup.Name} NewChannelId={createdCategory.Id}");

                        await Task.Delay(350);
                    }
                    catch (Exception ex)
                    {
                        LogError(ex, $"Failed to create category {categoryBackup.Name}");
                    }
                }

                LogProgress("Stage 6: creating non-category channels");
                foreach (var channelBackup in backup.Channels.Where(x => !string.Equals(x.ChannelKind, "category", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Position))
                {
                    try
                    {
                        IGuildChannel createdChannel = null;
                        LogProgress($"Creating channel START Kind={channelBackup.ChannelKind} Name={channelBackup.Name}");

                        if (string.Equals(channelBackup.ChannelKind, "voice", StringComparison.OrdinalIgnoreCase))
                        {
                            var createdVoice = await targetGuild.CreateVoiceChannelAsync(channelBackup.Name);
                            await createdVoice.ModifyAsync(props =>
                            {
                                if (channelBackup.Bitrate.HasValue) props.Bitrate = channelBackup.Bitrate.Value;
                                if (channelBackup.UserLimit.HasValue) props.UserLimit = channelBackup.UserLimit.Value;
                                props.Position = channelBackup.Position;
                            });
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
                            createdChannel = createdText;
                        }

                        if (createdChannel != null)
                        {
                            createdChannelMap[channelBackup.OriginalId] = createdChannel;
                            LogProgress($"Creating channel DONE Kind={channelBackup.ChannelKind} Name={channelBackup.Name} NewChannelId={createdChannel.Id}");
                        }

                        await Task.Delay(350);
                    }
                    catch (Exception ex)
                    {
                        LogError(ex, $"Failed to create channel {channelBackup.Name}");
                    }
                }

                LogProgress("Stage 7: assigning parent categories");
                foreach (var channelBackup in backup.Channels.Where(x => !string.Equals(x.ChannelKind, "category", StringComparison.OrdinalIgnoreCase)).Where(x => x.ParentCategoryOriginalId.HasValue))
                {
                    try
                    {
                        if (!createdChannelMap.TryGetValue(channelBackup.OriginalId, out var createdChannel)) continue;
                        if (!createdCategoryMap.TryGetValue(channelBackup.ParentCategoryOriginalId.Value, out var parentCategoryId)) continue;

                        LogProgress($"Assigning category START Channel={channelBackup.Name}");

                        if (createdChannel is SocketTextChannel textChannel)
                        {
                            await textChannel.ModifyAsync(props => props.CategoryId = parentCategoryId);
                        }
                        else if (createdChannel is SocketVoiceChannel voiceChannel)
                        {
                            await voiceChannel.ModifyAsync(props => props.CategoryId = parentCategoryId);
                        }

                        LogProgress($"Assigning category DONE Channel={channelBackup.Name}");
                    }
                    catch (Exception ex)
                    {
                        LogError(ex, $"Failed to assign category for channel {channelBackup.Name}");
                    }
                }

                LogProgress("Stage 8: applying permission overwrites");
                foreach (var channelBackup in backup.Channels.OrderBy(x => x.Position))
                {
                    try
                    {
                        if (!createdChannelMap.TryGetValue(channelBackup.OriginalId, out var createdChannel)) continue;

                        await ApplyRolePermissionOverwritesAsync(createdChannel, channelBackup.PermissionOverwrites, targetGuild, roleIdMap);
                        await Task.Delay(250);
                    }
                    catch (Exception ex)
                    {
                        LogError(ex, $"Failed applying overwrites for channel {channelBackup.Name}");
                    }
                }

                LogProgress("=== LOAD COMPLETE ===");

                await command.FollowupAsync(
                    $"Loaded configuration from `{filename}` into **{targetGuild.Name}**. Existing roles and channels were cleared first, except the channel used to run the command.",
                    ephemeral: true);
            }
            catch (Exception ex)
            {
                LogError(ex, "Error while loading guild configuration.");
                await SafeErrorResponseAsync(command, "An error occurred while loading the configuration.");
            }
        }

        private async Task DeleteExistingChannelsAsync(SocketGuild guild, HashSet<ulong> preservedChannelIds, ulong? preservedParentCategoryId)
        {
            var channels = guild.Channels.ToList();

            foreach (var channel in channels.Where(c => c is not SocketCategoryChannel).OrderByDescending(c => c.Position))
            {
                if (preservedChannelIds.Contains(channel.Id))
                {
                    LogProgress($"Preserving command channel {channel.Name} ({channel.Id})");
                    continue;
                }

                try
                {
                    LogProgress($"Deleting channel START {channel.Name} ({channel.Id})");
                    await channel.DeleteAsync();
                    LogProgress($"Deleting channel DONE {channel.Name} ({channel.Id})");
                    await Task.Delay(300);
                }
                catch (Exception ex)
                {
                    LogError(ex, $"Failed to delete channel {channel.Name} ({channel.Id})");
                }
            }

            foreach (var category in channels.OfType<SocketCategoryChannel>().OrderByDescending(c => c.Position))
            {
                if (preservedParentCategoryId.HasValue && category.Id == preservedParentCategoryId.Value)
                {
                    LogProgress($"Preserving category containing the command channel {category.Name} ({category.Id})");
                    continue;
                }

                try
                {
                    LogProgress($"Deleting category START {category.Name} ({category.Id})");
                    await category.DeleteAsync();
                    LogProgress($"Deleting category DONE {category.Name} ({category.Id})");
                    await Task.Delay(300);
                }
                catch (Exception ex)
                {
                    LogError(ex, $"Failed to delete category {category.Name} ({category.Id})");
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
                    LogProgress($"Deleting role START {role.Name} ({role.Id}) Position={role.Position}");
                    await role.DeleteAsync();
                    LogProgress($"Deleting role DONE {role.Name} ({role.Id})");
                    await Task.Delay(300);
                }
                catch (Exception ex)
                {
                    LogError(ex, $"Failed to delete role {role.Name} ({role.Id})");
                }
            }
        }

        private async Task ReorderCreatedRolesAsync(SocketGuild guild, List<(ulong OriginalId, ulong NewId, int SourcePosition)> createdRoles)
        {
            if (createdRoles.Count == 0)
            {
                LogProgress("No created roles to reorder.");
                return;
            }

            try
            {
                var sortedBySource = createdRoles.OrderBy(x => x.SourcePosition).ToList();
                var reorderList = new List<ReorderRoleProperties>();

                for (var i = 0; i < sortedBySource.Count; i++)
                {
                    reorderList.Add(new ReorderRoleProperties(sortedBySource[i].NewId, i + 1));
                }

                LogProgress($"Reordering roles START Count={reorderList.Count}");
                await guild.ReorderRolesAsync(reorderList);
                LogProgress("Reordering roles DONE");
            }
            catch (Exception ex)
            {
                LogError(ex, $"Failed to reorder created roles in guild {guild.Id}");
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
                    LogProgress($"Skipping thread channel during backup {channel.Name} ({channel.Id})");
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

                LogProgress($"Skipping unsupported channel type {channel.GetType().Name} for channel {channel.Name} ({channel.Id})");
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
                if (overwrite.TargetType != PermissionTarget.Role) continue;

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
            for (var i = 0; i < overwrites.Count; i++)
            {
                var overwrite = overwrites[i];
                try
                {
                    if (!roleIdMap.TryGetValue(overwrite.RoleOriginalId, out var mappedRoleId))
                    {
                        LogProgress($"Skipping overwrite [{i + 1}/{overwrites.Count}] on channel {channel.Name} because no mapped role exists for original role {overwrite.RoleOriginalId}");
                        continue;
                    }

                    var role = guild.GetRole(mappedRoleId);
                    if (role == null)
                    {
                        LogProgress($"Skipping overwrite [{i + 1}/{overwrites.Count}] on channel {channel.Name} because mapped role {mappedRoleId} was not found");
                        continue;
                    }

                    LogProgress($"Applying overwrite START [{i + 1}/{overwrites.Count}] Channel={channel.Name} Role={role.Name}");

                    var permissions = new OverwritePermissions(
                        allowValue: overwrite.AllowValue,
                        denyValue: overwrite.DenyValue);

                    await channel.AddPermissionOverwriteAsync(role, permissions);

                    LogProgress($"Applying overwrite DONE [{i + 1}/{overwrites.Count}] Channel={channel.Name} Role={role.Name}");
                }
                catch (Exception ex)
                {
                    LogError(ex, $"Failed to apply overwrite for original role {overwrite.RoleOriginalId} on channel {channel.Name}");
                }
            }
        }

        private void LogProgress(string message)
        {
            var finalMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [GuildConfigurationService] {message}";
            Console.WriteLine(finalMessage);
            _logger.LogInformation("{Message}", message);
        }

        private void LogError(Exception ex, string message)
        {
            var finalMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [GuildConfigurationService] ERROR {message}";
            Console.WriteLine(finalMessage);
            Console.WriteLine(ex.ToString());
            _logger.LogError(ex, "{Message}", message);
        }

        private async Task SafeErrorResponseAsync(SocketSlashCommand command, string message)
        {
            try
            {
                if (command.HasResponded) await command.FollowupAsync(message, ephemeral: true);
                else await command.RespondAsync(message, ephemeral: true);
            }
            catch { }
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
        public string Topic { get; set; }
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
