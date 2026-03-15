# Discord AI Moderation Bot

A Visual Studio solution for a Discord moderation bot in C# that:

- reads every message the bot can see
- compares messages against server-specific rules
- uses AI to flag possible rule violations
- posts alerts to a configurable channel
- supports slash commands for configuration
- stores moderator feedback and reuses it in future classifications

## Solution layout

- `src/DiscordAiModeration.Bot` - Discord host, slash commands, worker, startup
- `src/DiscordAiModeration.Core` - domain models and interfaces
- `src/DiscordAiModeration.Infrastructure` - SQLite persistence and OpenAI integration
- `tests/DiscordAiModeration.Tests` - starter test project

## Requirements

- .NET 8 SDK
- A Discord bot application and token
- Message Content Intent enabled in the Discord Developer Portal
- An OpenAI API key

## Environment variables

Set these before running:

- `DISCORD_BOT_TOKEN`
- `OPENAI_API_KEY`
- `OPENAI_MODEL` optional, defaults to `gpt-5-mini`
- `SQLITE_PATH` optional, defaults to `botdata.db`

## Suggested setup

1. Open `DiscordAiModeration.sln` in Visual Studio.
2. Restore NuGet packages.
3. Set the startup project to `DiscordAiModeration.Bot`.
4. Add the required environment variables in your launch profile or system environment.
5. Run the bot.

## Slash commands

### `/modconfig`
- `set-alert-channel`
- `set-ping-role`
- `set-threshold`
- `toggle-ai`

### `/rules`
- `add`
- `list`
- `remove`

### `/review`
- `approve`
- `reject`
- `list`

## Learning from moderator feedback

This solution does not fine-tune a model live. Instead, moderator approvals and rejections are stored and supplied as examples during later classifications. That gives you a practical feedback loop without requiring custom training infrastructure.

## Production notes

Before using this in a live server, you should consider adding:

- per-channel ignore lists
- per-role ignore lists
- rate limiting and batching
- retry policies for external API calls
- structured observability
- permissions checks for slash commands
- a review button workflow instead of only slash command review
- message links in alerts
- automatic moderator actions behind approval gates
