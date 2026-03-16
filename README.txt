This update adds verbose console tracing for AI moderation requests.

Files included:
- src/DiscordAiModeration.Bot/Program.cs
- src/DiscordAiModeration.Infrastructure/Services/AiModerationService.cs
- src/DiscordAiModeration.Infrastructure/Services/OpenAiModerationService.cs
- src/DiscordAiModeration.Infrastructure/Services/OllamaModerationService.cs

What you will see in the console:
- startup configuration including provider/model/log level
- when AI moderation starts for a message
- when a request is sent to OpenAI or Ollama
- HTTP status and timing when a response comes back
- the parsed moderation decision
- raw payload/response bodies when BOT_LOG_LEVEL=Debug or Trace

Optional environment variable:
- BOT_LOG_LEVEL=Debug
  Allowed examples: Trace, Debug, Information, Warning, Error
  If omitted, this update defaults to Debug.

How to use:
1. Extract this zip.
2. Copy the included src folder over your project src folder.
3. Rebuild the solution.
4. Run the bot and watch the console.

Tip:
- If you only want request/response flow without full payloads, set BOT_LOG_LEVEL=Information.
- If you want to inspect payloads and raw AI output, use BOT_LOG_LEVEL=Debug.
