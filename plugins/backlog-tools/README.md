# backlog-tools

Backlog-native tooling — skills specific to the Backlog product itself, as opposed to
general-purpose or knowledge-folder tooling. Two skills, one for each direction:

- **`backlog-import-plan`** — turns an agreed specification into a Backlog import plan
  (ADR 0007: `.arc42/adr/0007-import-reuses-the-entry-text-grammar.md`). Every entry is
  either a `prompt` an AI session runs or a `task` only the user does — never both in one
  entry. It always ships a review view next to the raw plan — one HTML page built from
  `skills/backlog-import-plan/assets/plan-review.html` that parses the embedded plan
  itself and shows its checks, dependency order and entries, published as an artifact where
  the host has one and written beside the plan otherwise. User-invoked only
  (`disable-model-invocation: true`); it never talks to the Backlog app or GitHub.
- **`backlog-run-plan-item`** — runs one entry of such a plan after it is copied out of the
  Backlog app and pasted into a session. The app puts the invocation on the first line of
  every entry it copies — `/backlog-tools:backlog-run-plan-item entry `<id>`:`, the entry
  under it — so the paste runs the skill outright; it is also model-invoked on the marker
  line every generated `prompt` entry opens with (`Backlog plan item `…``). Both shapes are
  defined in `skills/backlog-import-plan/assets/backlog-import-grammar.md`. It checks the
  item is still outstanding before doing anything — pasting the same item twice is expected
  and must not redo finished work — and then carries out the instructions the way the
  current repository says work is done. When a `backlog` MCP server is in the session's
  tool list it reads the entry's status from it (`read_item`) and reports Ready → In
  progress → Done back (`transition`), every call carrying the `repository` read off the
  git remote; without one it falls back to searching git and says the status has to be set
  by hand.

The plan-item marker exists because a plan is written before the app has given its entries
an id, so it restates the `id:`, `+tag`, `repo:` and `after:` the metadata line carries. The
entry line carries what only the app knows — the stored id — and is what makes any copied
entry a runnable paste.

`hooks/hooks.json` adds a `UserPromptSubmit` hook (Claude Code) that notices either line in
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
