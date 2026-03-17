Catholic Heresy Watch update pack

What is included
- catholic-heresy-rules.json
  Import this directly with your existing /rules import command.
- optional-code-update/src/DiscordAiModeration.Bot/Services/CatholicRulePack.cs
  Helper class for a built-in Catholic rule pack.
- optional-code-update/src/DiscordAiModeration.Infrastructure/Services/SharedPromptBuilder.cs
  Optional prompt builder replacement with stronger good-faith exemptions.
- optional-code-update/snippets/BotService_seed_command_snippet.txt
  Snippet for adding a one-click /rules seed-catholic-heresy slash command.

Fastest setup
1. In Discord, run /rules import
2. Upload catholic-heresy-rules.json
3. Set replace-existing = true if you want a clean install
4. Set your threshold to 80 or 85 for local Ollama models
5. Keep human review on; do not auto-punish from doctrinal flags alone

Recommended settings
- OpenAI: threshold 75 to 85
- Ollama / llama3.2: threshold 85 to 92
- Use simple prompts only if you need lower token cost; otherwise use full prompts

Suggested moderation philosophy
- Alert moderators, do not auto-timeout or auto-ban for theology alone
- Let the good-faith exemption stay in the ruleset
- Reject false positives so the feedback loop improves over time

Notes
- The JSON is designed to work with your current import/export format.
- The optional code changes are intentionally minimal.
