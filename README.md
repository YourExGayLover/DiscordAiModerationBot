# Catholic morality rule pack update

This pack is designed to be loaded **after** the Catholic heresy pack.

## What it adds
- A second built-in seed command: `/rules seed-catholic-morality`
- A standalone JSON import file: `catholic-morality-rules.json`
- A new helper class: `CatholicMoralityRulePack.cs`

## Recommended install order
1. `/rules seed-catholic-heresy replace-existing:true`
2. `/rules seed-catholic-morality replace-existing:false`

Using `replace-existing:false` on the second step will append the morality rules to the heresy rules.

## Notes
- Rule names are unique, so the second pack should add to the first rather than overwrite it.
- Rebuild and restart the bot after replacing source files so Discord re-registers slash commands.
