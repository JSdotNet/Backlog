# backlog-tools

Backlog-native tooling — skills specific to the Backlog product itself, as opposed to
general-purpose or knowledge-folder tooling. Two skills, one for each direction:

- **`backlog-import-plan`** — turns an agreed specification into a Backlog import plan
  (ADR 0007: `.arc42/adr/0007-import-reuses-the-entry-text-grammar.md`). Every entry is
  either a `prompt` an AI session runs or a `task` only the user does — never both in one
  entry. User-invoked only (`disable-model-invocation: true`); it never talks to the
  Backlog app or GitHub.
- **`backlog-run-plan-item`** — runs one entry of such a plan after it is copied out of the
  Backlog app and pasted into a session. Model-invoked: it triggers on the marker line
  every generated `prompt` entry opens with (`Backlog plan item `…``, defined in
  `skills/backlog-import-plan/assets/backlog-import-grammar.md#plan-item-marker`), checks
  the item is still outstanding before doing anything — pasting the same item twice is
  expected and must not redo finished work — and then carries out the instructions the way
  the current repository says work is done. It does not read from or write to Backlog yet;
  a feedback loop that pulls the plan's other items for context and reports status back is
  the planned next step.

The marker exists because Backlog's copy button hands over an entry's title and body but
not its metadata line, so a pasted entry has lost its `id:`, `+tag`, `repo:` and `after:`
unless the body restates them.

`hooks/hooks.json` adds a `UserPromptSubmit` hook (Claude Code) that notices the marker in
a prompt and nudges the session to invoke `backlog-run-plan-item`, so triggering does not
rest on the skill description alone. It needs `grep` on the hook shell, which Claude Code
provides on every platform it runs on.

This plugin ships two manifests so it installs the same way in either host:

- `.claude-plugin/plugin.json` — Claude Code
- `.github/plugin/plugin.json` — GitHub Copilot CLI

Both point at the same `skills/` folder. Keep their `name`/`description`/`version` fields
in sync by hand when either changes — there is no generator here.

## Install

**Claude Code**, inside a session started at the repository root:

```
/plugin marketplace add .
/plugin install backlog-tools@jsdotnet-backlog
```

**Copilot CLI**, from the repository root:

```bash
copilot plugin install ./plugins/backlog-tools
```
