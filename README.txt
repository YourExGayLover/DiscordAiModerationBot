PATCH CONTENTS

1. Replace the solution file with DiscordAiModeration.sln in this zip.
2. Add the new project folder:
   src/DiscordAiModeration.AdminConsole

HOW TO USE

- Open the solution in Visual Studio.
- Set DiscordAiModeration.AdminConsole as the startup project.
- Make sure DISCORD_TOKEN is available as an environment variable, or enter it when prompted.
- Run the app.
- Use commands:
  list-guilds
  backup <sourceGuildId> [outputFile]
  load <targetGuildId> <inputFile>

NOTES

- This app runs as the bot, but all backup/load actions are controlled from the console app instead of Discord slash commands.
- It deletes all channels and deletable roles in the target guild before loading the backup.
- It skips thread channels and unsupported channel types during backup.
