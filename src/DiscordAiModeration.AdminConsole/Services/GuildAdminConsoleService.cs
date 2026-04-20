using System.Text.Json;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace DiscordAiModeration.AdminConsole.Services;

public sealed class GuildAdminConsoleService
{
    private readonly DiscordSocketClient _client;
    private readonly ILogger<GuildAdminConsoleService> _logger;
    private readonly string _backupDirectory;

    public GuildAdminConsoleService(DiscordSocketClient client, ILogger<GuildAdminConsoleService> logger)
    {
        _client = client;
        _logger = logger;
        _backupDirectory = Path.Combine(AppContext.BaseDirectory, "backups");
        Directory.CreateDirectory(_backupDirectory);
    }

    public async Task<string> BackupGuildAsync(ulong sourceGuildId, string? outputPath = null)
    {
        var guild = _client.GetGuild(sourceGuildId)
            ?? throw new InvalidOperationException($"Bot is not in guild {sourceGuildId}.");

        Log($"Starting backup for {guild.Name} ({guild.Id})");

        var backup = new GuildConfigurationBackup
        {
            Version = 2,
            ExportedAtUtc = DateTime.UtcNow,
            SourceGuildId = guild.Id,
            SourceGuildName = guild.Name,
            Roles = BuildRoleBackup(guild),
            Channels = BuildChannelBackup(guild)
        };

        var path = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(_backupDirectory, $"{guild.Id}-{SanitizeFileName(guild.Name)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json")
            : outputPath;

        var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);

        Log($"Backup complete: {path}");
        return path;
    }

    public async Task LoadGuildAsync(ulong targetGuildId, string inputPath)
    {
        var guild = _client.GetGuild(targetGuildId)
            ?? throw new InvalidOperationException($"Bot is not in guild {targetGuildId}.");

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Backup file not found.", inputPath);
        }

        var me = guild.CurrentUser;
        if (me == null || !me.GuildPermissions.ManageRoles || !me.GuildPermissions.ManageChannels)
        {
            throw new InvalidOperationException("Bot needs Manage Roles and Manage Channels in the target guild.");
        }

        Log($"Loading configuration into {guild.Name} ({guild.Id}) from {inputPath}");

        var json = await File.ReadAllTextAsync(inputPath);
        var backup = JsonSerializer.Deserialize<GuildConfigurationBackup>(json)
            ?? throw new InvalidOperationException("Backup file could not be deserialized.");

        Log($"Backup loaded. Roles={backup.Roles.Count} Channels={backup.Channels.Count}");

        await DeleteExistingChannelsAsync(guild);
        await DeleteExistingRolesAsync(guild, me);

        var roleIdMap = new Dictionary<ulong, ulong>();
        var createdRoles = new List<(RoleConfigurationBackup Backup, ulong NewId)>();

        foreach (var roleBackup in backup.Roles
                     .OrderBy(r => HasAdministratorPermission(r) ? 1 : 0)
                     .ThenByDescending(r => r.Position))
        {
            var createdRole = await CreateRoleWithRetriesAsync(guild, roleBackup);
            roleIdMap[roleBackup.OriginalId] = createdRole.Id;
            createdRoles.Add((roleBackup, createdRole.Id));
        }

        await ReorderCreatedRolesAsync(guild, createdRoles);
        await ApplyCreatedRolePropertiesAsync(guild, createdRoles);

        var createdCategoryMap = new Dictionary<ulong, ulong>();
        var createdChannelMap = new Dictionary<ulong, IGuildChannel>();

        foreach (var categoryBackup in backup.Channels
                     .Where(c => c.ChannelKind == "category")
                     .OrderBy(c => c.Position))
        {
            Log($"Creating category {categoryBackup.Name}");
            var category = await CreateCategoryWithRetriesAsync(guild, categoryBackup.Name);

            await category.ModifyAsync(props =>
            {
                props.Position = categoryBackup.Position;
            });

            await ApplyRolePermissionOverwritesAsync(category, categoryBackup.PermissionOverwrites, guild, roleIdMap);

            createdCategoryMap[categoryBackup.OriginalId] = category.Id;
            createdChannelMap[categoryBackup.OriginalId] = category;
            await Task.Delay(2000);
        }

        foreach (var channelBackup in backup.Channels
                     .Where(c => c.ChannelKind != "category")
                     .OrderBy(c => c.Position))
        {
            IGuildChannel? createdChannel = null;

            if (channelBackup.ChannelKind == "text")
            {
                Log($"Creating text channel {channelBackup.Name}");
                var text = await CreateTextChannelWithRetriesAsync(guild, channelBackup.Name);

                await text.ModifyAsync(props =>
                {
                    props.Topic = channelBackup.Topic;
                    props.SlowModeInterval = channelBackup.SlowModeSeconds;
                    props.Position = channelBackup.Position;
                });

                await ApplyRolePermissionOverwritesAsync(text, channelBackup.PermissionOverwrites, guild, roleIdMap);
                createdChannel = text;
            }
            else if (channelBackup.ChannelKind == "voice")
            {
                Log($"Creating voice channel {channelBackup.Name}");
                var voice = await CreateVoiceChannelWithRetriesAsync(guild, channelBackup.Name);

                await voice.ModifyAsync(props =>
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

                await ApplyRolePermissionOverwritesAsync(voice, channelBackup.PermissionOverwrites, guild, roleIdMap);
                createdChannel = voice;
            }

            if (createdChannel != null)
            {
                createdChannelMap[channelBackup.OriginalId] = createdChannel;
                await Task.Delay(1500);
            }
        }

        foreach (var channelBackup in backup.Channels
                     .Where(c => c.ChannelKind != "category" && c.ParentCategoryOriginalId.HasValue))
        {
            if (!createdChannelMap.TryGetValue(channelBackup.OriginalId, out var channel))
            {
                continue;
            }

            if (!createdCategoryMap.TryGetValue(channelBackup.ParentCategoryOriginalId!.Value, out var categoryId))
            {
                continue;
            }

            Log($"Assigning category for {channelBackup.Name}");

            if (channel is ITextChannel textChannel)
            {
                await textChannel.ModifyAsync(props => props.CategoryId = categoryId);
            }
            else
            {
                var voiceChannel = channel as IVoiceChannel;
                if (voiceChannel != null)
                {
                    await voiceChannel.ModifyAsync(props => props.CategoryId = categoryId);
                }
            }

            await Task.Delay(750);
        }

        Log("Load complete.");
    }

    private async Task DeleteExistingChannelsAsync(SocketGuild guild)
    {
        foreach (var channel in guild.Channels.Where(c => c is not SocketCategoryChannel).OrderByDescending(c => c.Position).ToList())
        {
            Log($"Deleting channel {channel.Name}");
            await channel.DeleteAsync();
            await Task.Delay(750);
        }

        foreach (var category in guild.Channels.OfType<SocketCategoryChannel>().OrderByDescending(c => c.Position).ToList())
        {
            Log($"Deleting category {category.Name}");
            await category.DeleteAsync();
            await Task.Delay(750);
        }
    }

    private async Task DeleteExistingRolesAsync(SocketGuild guild, SocketGuildUser me)
    {
        var protectedRoleIds = new HashSet<ulong>(me.Roles.Select(r => r.Id)) { guild.EveryoneRole.Id };

        var rolesToDelete = guild.Roles
            .Where(r => !protectedRoleIds.Contains(r.Id))
            .Where(r => !r.IsManaged)
            .Where(r => r.Position < me.Hierarchy)
            .OrderByDescending(r => r.Position)
            .ToList();

        foreach (var role in rolesToDelete)
        {
            Log($"Deleting role {role.Name}");
            await role.DeleteAsync();
            await Task.Delay(750);
        }
    }

    private async Task<IRole> CreateRoleWithRetriesAsync(SocketGuild guild, RoleConfigurationBackup roleBackup)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                Log($"Creating role {roleBackup.Name} (attempt {attempt}/5)");
                var role = await RunWithHeartbeatAndTimeoutAsync(
                    () => guild.CreateRoleAsync(roleBackup.Name),
                    $"CreateRole:{roleBackup.Name}:Attempt{attempt}",
                    heartbeatMs: 10000,
                    timeoutMs: 180000);

                await Task.Delay(15000);
                return role;
            }
            catch (Exception ex)
            {
                Log($"Create role failed for {roleBackup.Name} on attempt {attempt}: {ex.Message}");
                if (attempt == 5)
                {
                    throw;
                }

                await Task.Delay(15000);
            }
        }

        throw new InvalidOperationException($"Unreachable retry failure for {roleBackup.Name}");
    }

    private async Task<ICategoryChannel> CreateCategoryWithRetriesAsync(SocketGuild guild, string name)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                var channel = await RunWithHeartbeatAndTimeoutAsync(
                    () => guild.CreateCategoryChannelAsync(name),
                    $"CreateCategory:{name}:Attempt{attempt}",
                    10000,
                    180000);

                await Task.Delay(5000);
                return channel;
            }
            catch (Exception ex)
            {
                Log($"Create category failed for {name} on attempt {attempt}: {ex.Message}");
                if (attempt == 5)
                {
                    throw;
                }

                await Task.Delay(15000);
            }
        }

        throw new InvalidOperationException($"Unreachable retry failure for category {name}");
    }

    private async Task<ITextChannel> CreateTextChannelWithRetriesAsync(SocketGuild guild, string name)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                var channel = await RunWithHeartbeatAndTimeoutAsync(
                    () => guild.CreateTextChannelAsync(name),
                    $"CreateText:{name}:Attempt{attempt}",
                    10000,
                    180000);

                await Task.Delay(5000);
                return channel;
            }
            catch (Exception ex)
            {
                Log($"Create text channel failed for {name} on attempt {attempt}: {ex.Message}");
                if (attempt == 5)
                {
                    throw;
                }

                await Task.Delay(15000);
            }
        }

        throw new InvalidOperationException($"Unreachable retry failure for text channel {name}");
    }

    private async Task<IVoiceChannel> CreateVoiceChannelWithRetriesAsync(SocketGuild guild, string name)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                var channel = await RunWithHeartbeatAndTimeoutAsync(
                    () => guild.CreateVoiceChannelAsync(name),
                    $"CreateVoice:{name}:Attempt{attempt}",
                    10000,
                    180000);

                await Task.Delay(5000);
                return channel;
            }
            catch (Exception ex)
            {
                Log($"Create voice channel failed for {name} on attempt {attempt}: {ex.Message}");
                if (attempt == 5)
                {
                    throw;
                }

                await Task.Delay(15000);
            }
        }

        throw new InvalidOperationException($"Unreachable retry failure for voice channel {name}");
    }

    private async Task ReorderCreatedRolesAsync(SocketGuild guild, List<(RoleConfigurationBackup Backup, ulong NewId)> createdRoles)
    {
        if (createdRoles.Count == 0)
        {
            return;
        }

        var sortedBySource = createdRoles.OrderBy(r => r.Backup.Position).ToList();
        var reorderList = new List<ReorderRoleProperties>();

        for (var i = 0; i < sortedBySource.Count; i++)
        {
            reorderList.Add(new ReorderRoleProperties(sortedBySource[i].NewId, i + 1));
        }

        Log("Reordering roles");
        await guild.ReorderRolesAsync(reorderList);
        await Task.Delay(10000);
    }

    private async Task ApplyCreatedRolePropertiesAsync(SocketGuild guild, List<(RoleConfigurationBackup Backup, ulong NewId)> createdRoles)
    {
        var ordered = createdRoles
            .OrderBy(r => HasAdministratorPermission(r.Backup) ? 1 : 0)
            .ThenByDescending(r => r.Backup.Position)
            .ToList();

        foreach (var item in ordered)
        {
            var role = guild.GetRole(item.NewId);
            if (role == null)
            {
                continue;
            }

            Log($"Applying role properties for {item.Backup.Name}");

            await RunWithHeartbeatAndTimeoutAsync(
                () => role.ModifyAsync(props =>
                {
                    props.Color = new Color(item.Backup.ColorRawValue);
                    props.Hoist = item.Backup.IsHoisted;
                    props.Mentionable = item.Backup.IsMentionable;
                    props.Permissions = new GuildPermissions(item.Backup.PermissionsRawValue);
                }),
                $"ModifyRole:{item.Backup.Name}",
                10000,
                180000);

            await Task.Delay(10000);
        }
    }

    private List<RoleConfigurationBackup> BuildRoleBackup(SocketGuild guild)
    {
#pragma warning disable CS0618
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
#pragma warning restore CS0618
    }

    private List<ChannelConfigurationBackup> BuildChannelBackup(SocketGuild guild)
    {
        var channels = new List<ChannelConfigurationBackup>();

        foreach (var channel in guild.Channels.OrderBy(c => c.Position))
        {
            if (channel is SocketThreadChannel)
            {
                Log($"Skipping thread channel during backup {channel.Name} ({channel.Id})");
                continue;
            }

            if (channel is SocketCategoryChannel category)
            {
                channels.Add(new ChannelConfigurationBackup
                {
                    OriginalId = category.Id,
                    Name = category.Name,
                    ChannelKind = "category",
                    Position = category.Position,
                    PermissionOverwrites = BuildPermissionOverwrites(category)
                });

                continue;
            }

            if (channel is SocketVoiceChannel voice)
            {
                channels.Add(new ChannelConfigurationBackup
                {
                    OriginalId = voice.Id,
                    Name = voice.Name,
                    ChannelKind = "voice",
                    Position = voice.Position,
                    ParentCategoryOriginalId = voice.Category?.Id,
                    Bitrate = voice.Bitrate,
                    UserLimit = voice.UserLimit,
                    PermissionOverwrites = BuildPermissionOverwrites(voice)
                });

                continue;
            }

            if (channel is SocketTextChannel text)
            {
                channels.Add(new ChannelConfigurationBackup
                {
                    OriginalId = text.Id,
                    Name = text.Name,
                    ChannelKind = "text",
                    Position = text.Position,
                    ParentCategoryOriginalId = text.Category?.Id,
                    Topic = text.Topic,
                    IsNsfw = text.IsNsfw,
                    SlowModeSeconds = text.SlowModeInterval,
                    PermissionOverwrites = BuildPermissionOverwrites(text)
                });

                continue;
            }

            Log($"Skipping unsupported channel type {channel.GetType().Name} for channel {channel.Name} ({channel.Id})");
        }

        return channels;
    }

    private List<PermissionOverwriteBackup> BuildPermissionOverwrites(SocketGuildChannel channel)
    {
        var list = new List<PermissionOverwriteBackup>();

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
            if (!roleIdMap.TryGetValue(overwrite.RoleOriginalId, out var mappedRoleId))
            {
                continue;
            }

            var role = guild.GetRole(mappedRoleId);
            if (role == null)
            {
                continue;
            }

            Log($"Applying overwrite for {channel.Name} -> {role.Name}");

            var permissions = new OverwritePermissions(overwrite.AllowValue, overwrite.DenyValue);
            await channel.AddPermissionOverwriteAsync(role, permissions);
            await Task.Delay(500);
        }
    }

    private async Task<T> RunWithHeartbeatAndTimeoutAsync<T>(
        Func<Task<T>> action,
        string operationName,
        int heartbeatMs,
        int timeoutMs)
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
            Log($"Still waiting for operation: {operationName} Elapsed={elapsed.TotalSeconds:F0}s");

            if (elapsed.TotalMilliseconds >= timeoutMs)
            {
                throw new TimeoutException($"TIMEOUT waiting for operation: {operationName} after {elapsed.TotalSeconds:F0}s");
            }
        }

        return await task;
    }

    private async Task RunWithHeartbeatAndTimeoutAsync(
        Func<Task> action,
        string operationName,
        int heartbeatMs,
        int timeoutMs)
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
            Log($"Still waiting for operation: {operationName} Elapsed={elapsed.TotalSeconds:F0}s");

            if (elapsed.TotalMilliseconds >= timeoutMs)
            {
                throw new TimeoutException($"TIMEOUT waiting for operation: {operationName} after {elapsed.TotalSeconds:F0}s");
            }
        }

        await task;
    }

    private static bool HasAdministratorPermission(RoleConfigurationBackup role)
    {
        return new GuildPermissions(role.PermissionsRawValue).Administrator;
    }

    private void Log(string message)
    {
        _logger.LogInformation(message);
        Console.WriteLine($"[GuildAdminConsole] {message}");
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "guild" : cleaned;
    }
}

public sealed class GuildConfigurationBackup
{
    public int Version { get; set; }
    public DateTime ExportedAtUtc { get; set; }
    public ulong SourceGuildId { get; set; }
    public string SourceGuildName { get; set; } = string.Empty;
    public List<RoleConfigurationBackup> Roles { get; set; } = new();
    public List<ChannelConfigurationBackup> Channels { get; set; } = new();
}

public sealed class RoleConfigurationBackup
{
    public ulong OriginalId { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint ColorRawValue { get; set; }
    public bool IsHoisted { get; set; }
    public bool IsMentionable { get; set; }
    public ulong PermissionsRawValue { get; set; }
    public int Position { get; set; }
}

public sealed class ChannelConfigurationBackup
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

public sealed class PermissionOverwriteBackup
{
    public ulong RoleOriginalId { get; set; }
    public ulong AllowValue { get; set; }
    public ulong DenyValue { get; set; }
}
