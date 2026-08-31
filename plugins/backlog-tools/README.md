# backlog-tools

Backlog-native tooling — skills specific to the Backlog product itself, as opposed to
general-purpose or knowledge-folder tooling. Currently one skill:

- **`backlog-import-plan`** — turns an agreed specification into a Backlog import plan
  (ADR 0004: `.arc42/adr/0004-import-reuses-the-entry-text-grammar.md`). User-invoked only
  (`disable-model-invocation: true`); it never talks to the Backlog app or GitHub.

This plugin ships two manifests so it installs the same way in either host:

- `.claude-plugin/plugin.json` — Claude Code
- `.github/plugin/plugin.json` — GitHub Copilot CLI

Both point at the same `skills/` folder. Keep their `name`/`description`/`version` fields
in sync by hand when either changes — there is no generator here.

## Install

**Claude Code**, inside a session started at the repository root:

```
/plugin marketplace add .
/plugin install backlog-tools@backlog-tools
```

**Copilot CLI**, from the repository root:

```bash
copilot plugin install ./plugins/backlog-tools
```
