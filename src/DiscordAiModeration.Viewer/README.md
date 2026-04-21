# DiscordAiModeration.Viewer

A small local web dashboard you can add beside the existing bot solution to browse guilds, text channels, and recent cached messages using your bot token.

## What it does

- Connects with `DiscordSocketClient`
- Reads guilds and text channels the bot can access
- Pulls recent history on startup for each visible text channel
- Updates live when new messages arrive
- Serves a simple local UI at `http://localhost:5118`

## Important limitations

- The bot can only read channels it already has permission to view.
- You need **Message Content Intent** enabled for the bot in the Discord developer portal.
- This project caches a recent slice of messages in memory. It does not persist full history to a database.
- Large servers may take longer to finish initial sync.

## Suggested repo location

Place this folder here inside your solution repo:

```text
src/DiscordAiModeration.Viewer
```

If you do that, update the two conditional project references in the `.csproj` so they still point at:

```text
..\DiscordAiModeration.Core\DiscordAiModeration.Core.csproj
..\DiscordAiModeration.Infrastructure\DiscordAiModeration.Infrastructure.csproj
```

That path already matches the `src/*` layout in your repo.

## Add to solution

```bash
dotnet sln DiscordAiModeration.sln add src/DiscordAiModeration.Viewer/DiscordAiModeration.Viewer.csproj
```

## Run

```bash
set DISCORD_TOKEN=your_bot_token
dotnet run --project src/DiscordAiModeration.Viewer/DiscordAiModeration.Viewer.csproj
```

Then open:

```text
http://localhost:5118
```

## Optional configuration

`appsettings.json`

- `Viewer:Port` - local HTTP port
- `Viewer:MaxMessagesPerChannel` - in-memory cache limit
- `Viewer:AllowAttachmentLinks` - expose attachment URLs in the UI
- `Viewer:PreferredGuildId` - limit the dashboard to one guild
