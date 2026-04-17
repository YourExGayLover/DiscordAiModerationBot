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

                var backup = new GuildConfigurationBackup
                {
                    Version = 1,
                    ExportedAtUtc = DateTime.UtcNow,
                    SourceGuildId = sourceGuild.Id,
                    SourceGuildName = sourceGuild.Name,
                    Roles = BuildRoleBackup(sourceGuild),
                    Channels = BuildChannelBackup(sourceGuild)
                };

                var fileName = $"{sourceGuild.Id}-{SanitizeFileName(sourceGuild.Name)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
                var fullPath = Path.Combine(_backupDirectory, fileName);

                var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(fullPath, json);

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

                var json = await File.ReadAllTextAsync(fullPath);
                var backup = JsonSerializer.Deserialize<GuildConfigurationBackup>(json);

                if (backup == null)
                {
                    await command.FollowupAsync("The backup file could not be read.", ephemeral: true);
                    return;
                }

                var me = targetGuild.CurrentUser;
                if (me == null ||
                    !me.GuildPermissions.ManageRoles ||
                    !me.GuildPermissions.ManageChannels)
                {
                    await command.FollowupAsync(
                        "The bot needs **Manage Roles** and **Manage Channels** in the target server.",
                        ephemeral: true);
                    return;
                }

                var roleIdMap = new Dictionary<ulong, ulong>
                {
                    [backup.SourceGuildId] = targetGuild.EveryoneRole.Id
                };

                var createdRoles = new List<(ulong RoleId, int Position)>();

                foreach (var roleBackup in backup.Roles.OrderBy(x => x.Position))
                {
                    try
                    {
                        var createdRole = await targetGuild.CreateRoleAsync(
                            roleBackup.Name,
                            new GuildPermissions(roleBackup.PermissionsRawValue),
                            new Color(roleBackup.ColorRawValue),
                            roleBackup.IsHoisted,
                            roleBackup.IsMentionable);

                        roleIdMap[roleBackup.OriginalId] = createdRole.Id;
                        createdRoles.Add((createdRole.Id, roleBackup.Position));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to create role {RoleName}", roleBackup.Name);
                    }
                }

                if (createdRoles.Count > 0)
                {
                    try
                    {
                        var reorderList = createdRoles
                            .Select(x => new ReorderRoleProperties(x.RoleId, x.Position))
                            .ToList();

                        if (reorderList.Count > 0)
                        {
                            await targetGuild.ReorderRolesAsync(reorderList);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to reorder roles in guild {GuildId}", targetGuild.Id);
                    }
                }

                var createdCategoryMap = new Dictionary<ulong, ulong>();

                foreach (var categoryBackup in backup.Channels
                             .Where(x => x.ChannelType == SavedChannelType.Category)
                             .OrderBy(x => x.Position))
                {
                    try
                    {
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
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to create category {ChannelName}", categoryBackup.Name);
                    }
                }

                foreach (var channelBackup in backup.Channels
                             .Where(x => x.ChannelType != SavedChannelType.Category)
                             .OrderBy(x => x.Position))
                {
                    try
                    {
                        switch (channelBackup.ChannelType)
                        {
                            case SavedChannelType.Text:
                                {
                                    var createdText = await targetGuild.CreateTextChannelAsync(channelBackup.Name);

                                    await createdText.ModifyAsync(props =>
                                    {
                                        props.Topic = channelBackup.Topic;
                                        props.SlowModeInterval = channelBackup.SlowModeSeconds;

                                        if (channelBackup.ParentCategoryOriginalId.HasValue &&
                                            createdCategoryMap.TryGetValue(channelBackup.ParentCategoryOriginalId.Value, out var parentCategoryId))
                                        {
                                            props.CategoryId = parentCategoryId;
                                        }

                                        props.Position = channelBackup.Position;
                                    });

                                    await ApplyRolePermissionOverwritesAsync(
                                        createdText,
                                        channelBackup.PermissionOverwrites,
                                        targetGuild,
                                        roleIdMap);

                                    break;
                                }

                            case SavedChannelType.Voice:
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

                                        if (channelBackup.ParentCategoryOriginalId.HasValue &&
                                            createdCategoryMap.TryGetValue(channelBackup.ParentCategoryOriginalId.Value, out var parentCategoryId))
                                        {
                                            props.CategoryId = parentCategoryId;
                                        }

                                        props.Position = channelBackup.Position;
                                    });

                                    await ApplyRolePermissionOverwritesAsync(
                                        createdVoice,
                                        channelBackup.PermissionOverwrites,
                                        targetGuild,
                                        roleIdMap);

                                    break;
                                }

                            default:
                                _logger.LogInformation(
                                    "Skipping unsupported channel type {ChannelType} for channel {ChannelName}",
                                    channelBackup.ChannelType,
                                    channelBackup.Name);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to create channel {ChannelName}", channelBackup.Name);
                    }
                }

                await command.FollowupAsync(
                    $"Loaded configuration from `{filename}` into **{targetGuild.Name}**.",
                    ephemeral: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while loading guild configuration.");
                await SafeErrorResponseAsync(command, "An error occurred while loading the configuration.");
            }
        }

        private List<RoleConfigurationBackup> BuildRoleBackup(SocketGuild guild)
        {
            return guild.Roles
                .Where(r => r.Id != guild.EveryoneRole.Id && !r.IsManaged)
                .OrderBy(r => r.Position)
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
                // Skip thread channels completely
                if (channel is SocketThreadChannel)
                {
                    continue;
                }

                if (channel is SocketCategoryChannel categoryChannel)
                {
                    channels.Add(new ChannelConfigurationBackup
                    {
                        OriginalId = categoryChannel.Id,
                        Name = categoryChannel.Name,
                        ChannelType = SavedChannelType.Category,
                        Position = categoryChannel.Position,
                        PermissionOverwrites = BuildPermissionOverwrites(categoryChannel)
                    });

                    continue;
                }

                if (channel is SocketTextChannel textChannel)
                {
                    channels.Add(new ChannelConfigurationBackup
                    {
                        OriginalId = textChannel.Id,
                        Name = textChannel.Name,
                        ChannelType = SavedChannelType.Text,
                        Position = textChannel.Position,
                        ParentCategoryOriginalId = textChannel.Category?.Id,
                        Topic = textChannel.Topic,
                        IsNsfw = textChannel.IsNsfw,
                        SlowModeSeconds = textChannel.SlowModeInterval,
                        PermissionOverwrites = BuildPermissionOverwrites(textChannel)
                    });

                    continue;
                }

                if (channel is SocketVoiceChannel voiceChannel)
                {
                    channels.Add(new ChannelConfigurationBackup
                    {
                        OriginalId = voiceChannel.Id,
                        Name = voiceChannel.Name,
                        ChannelType = SavedChannelType.Voice,
                        Position = voiceChannel.Position,
                        ParentCategoryOriginalId = voiceChannel.Category?.Id,
                        Bitrate = voiceChannel.Bitrate,
                        UserLimit = voiceChannel.UserLimit,
                        PermissionOverwrites = BuildPermissionOverwrites(voiceChannel)
                    });

                    continue;
                }

                _logger.LogInformation(
                    "Skipping unsupported channel type {ChannelType} for channel {ChannelName} ({ChannelId})",
                    channel.GetType().Name,
                    channel.Name,
                    channel.Id);
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
        private async Task ApplyRolePermissionOverwritesAsync(
            IGuildChannel channel,
            List<PermissionOverwriteBackup> overwrites,
            SocketGuild guild,
            Dictionary<ulong, ulong> roleIdMap)
        {
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

                    var permissions = new OverwritePermissions(
                        allowValue: overwrite.AllowValue,
                        denyValue: overwrite.DenyValue);

                    await channel.AddPermissionOverwriteAsync(role, permissions);
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
        public SavedChannelType ChannelType { get; set; }
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

    public enum SavedChannelType
    {
        Category = 0,
        Text = 1,
        Voice = 2
    }
}