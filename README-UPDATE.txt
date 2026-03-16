This is a drop-in update for YourExGayLover/DiscordAiModerationBot.

What it adds:
- New guild setting: UseSimplePrompts
- New slash command option: /modconfig toggle-simple-prompts enabled:true|false
- Simple prompt mode uses a shorter moderation request to reduce token usage
- Full prompt mode keeps the richer request format
- SQLite migration for existing botdata.db files

How to apply:
1. Unzip this archive over the root of your existing repo.
2. Let it replace the files in the matching src/... paths.
3. Rebuild the solution.
4. Use /modconfig toggle-simple-prompts enabled:true to turn simple mode on.

Notes:
- Existing databases are migrated automatically by SqliteDatabase.InitializeAsync.
- Simple mode is stored per guild.
