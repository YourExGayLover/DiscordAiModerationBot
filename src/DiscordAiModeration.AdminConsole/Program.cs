using Discord;
using Discord.WebSocket;
using DiscordAiModeration.AdminConsole.Services;
using Microsoft.Extensions.Logging;

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.ClearProviders();
    builder.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
    });
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger("AdminConsole");

var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
if (string.IsNullOrWhiteSpace(token))
{
    Console.Write("Enter bot token: ");
    token = Console.ReadLine();
}

if (string.IsNullOrWhiteSpace(token))
{
    Console.WriteLine("A bot token is required.");
    return;
}

var client = new DiscordSocketClient(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMembers,
    LogGatewayIntentWarnings = false,
    AlwaysDownloadUsers = false
});

client.Log += msg =>
{
    logger.LogInformation("Discord: {Message}", msg.ToString());
    return Task.CompletedTask;
};

var service = new GuildAdminConsoleService(client, loggerFactory.CreateLogger<GuildAdminConsoleService>());

var readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
client.Ready += () =>
{
    readyTcs.TrySetResult(true);
    return Task.CompletedTask;
};

await client.LoginAsync(TokenType.Bot, token);
await client.StartAsync();
await readyTcs.Task;

Console.WriteLine();
Console.WriteLine("Discord Admin Console");
Console.WriteLine("---------------------");
Console.WriteLine("Commands:");
Console.WriteLine("  help");
Console.WriteLine("  list-guilds");
Console.WriteLine("  backup <sourceGuildId> [outputFile]");
Console.WriteLine("  load <targetGuildId> <inputFile>");
Console.WriteLine("  quit");
Console.WriteLine();

while (true)
{
    Console.Write("> ");
    var line = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(line))
    {
        continue;
    }

    var parts = ParseArguments(line).ToList();
    if (parts.Count == 0)
    {
        continue;
    }

    var command = parts[0].ToLowerInvariant();

    try
    {
        switch (command)
        {
            case "help":
                Console.WriteLine("list-guilds");
                Console.WriteLine("backup <sourceGuildId> [outputFile]");
                Console.WriteLine("load <targetGuildId> <inputFile>");
                Console.WriteLine("quit");
                break;

            case "list-guilds":
                foreach (var guild in client.Guilds.OrderBy(g => g.Name))
                {
                    Console.WriteLine($"{guild.Name} ({guild.Id})");
                }
                break;

            case "backup":
                if (parts.Count < 2 || !ulong.TryParse(parts[1], out var sourceGuildId))
                {
                    Console.WriteLine("Usage: backup <sourceGuildId> [outputFile]");
                    break;
                }

                var outputFile = parts.Count >= 3 ? parts[2] : null;
                var backupPath = await service.BackupGuildAsync(sourceGuildId, outputFile);
                Console.WriteLine($"Backup written to: {backupPath}");
                break;

            case "load":
                if (parts.Count < 3 || !ulong.TryParse(parts[1], out var targetGuildId))
                {
                    Console.WriteLine("Usage: load <targetGuildId> <inputFile>");
                    break;
                }

                await service.LoadGuildAsync(targetGuildId, parts[2]);
                Console.WriteLine("Load complete.");
                break;

            case "quit":
            case "exit":
                await client.StopAsync();
                await client.LogoutAsync();
                return;

            default:
                Console.WriteLine("Unknown command. Type 'help'.");
                break;
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Console command failed");
        Console.WriteLine(ex);
    }
}

static IEnumerable<string> ParseArguments(string commandLine)
{
    var current = new System.Text.StringBuilder();
    var inQuotes = false;

    foreach (var ch in commandLine)
    {
        if (ch == '"')
        {
            inQuotes = !inQuotes;
            continue;
        }

        if (char.IsWhiteSpace(ch) && !inQuotes)
        {
            if (current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
            }

            continue;
        }

        current.Append(ch);
    }

    if (current.Length > 0)
    {
        yield return current.ToString();
    }
}
