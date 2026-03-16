This zip contains the files needed to add Discord server rule import/export support.

Copy these files into the matching folders in your project:
- src/DiscordAiModeration.Bot/Services/BotService.cs
- src/DiscordAiModeration.Bot/Models/RuleImportExportModels.cs
- src/DiscordAiModeration.Core/Interfaces/IDatabase.cs
- src/DiscordAiModeration.Infrastructure/Data/SqliteDatabase.cs

What this adds:
- /rules export
- /rules import file:<json> [replace-existing:true|false]

Notes:
- Your Program.cs already registers AddHttpClient() in the current repo, so no required Program.cs change is included.
- If your editor shows a formatting difference, that is expected.
