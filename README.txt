This update changes moderation alerts so they show a direct Discord message link instead of the channel mention.

Copy this file into your project, preserving the folder structure:
- src/DiscordAiModeration.Bot/Services/ModerationQueue.cs

What changed:
- Replaced the embed field "Channel" with "Message Link"
- Added a Discord deep link in the format:
  https://discord.com/channels/{guildId}/{channelId}/{messageId}
- Added the same link to the console log when an alert is sent
