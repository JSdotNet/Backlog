# Orchestration Model Selection Overrides

Team-shared model choice for `orch-*` orchestration runs in `JSdotNet/Backlog`, read by the
`claude-desktop` plugin's orchestrator once per run.

These are **Claude model aliases** — `opus`, `sonnet`, `haiku`, `fable`, or `inherit` — never
version-pinned IDs, so this file does not need an edit when a new release ships. The
orchestrator resolves the model per named agent, not once per stage.

This file is unrelated to `.github/copilot-model-selection.md`. That file configures GitHub
Copilot against the Azure Foundry deployment catalog, where the Anthropic provider still
injects the deprecated `temperature` parameter and therefore cannot run the Claude 5 family.
Claude Code talks to Anthropic directly and has no such constraint, so the Claude families
are chosen here on merit. The two files do not need to be kept in sync.

## Overrides

Only the rows below are overridden; every other category keeps the plugin default from
`claude-desktop/instructions/orch-model-selection.instructions.md`.

| Category | Model |
| --- | --- |
| Planning & Product Definition | opus |
| Documentation & Low-Complexity | sonnet |

## Rationale

- **Planning & Product Definition** (`product-owner:product-owner`) — plugin default is
  `sonnet`. Backlog is knowledge-base-driven: `.backlog/` chapters are governed assets that
  feed straight into what the implementation stages build, so a scope or acceptance-criteria
  error propagates into code rather than being caught at review. Raised to `opus`.
- **Documentation & Low-Complexity** (`documentation:profile`) — plugin default is `haiku`,
  which the convention describes as the one category where the lightweight model is a genuine
  match rather than a cost shortcut. That does not hold here. Documentation in this
  repository means the governed knowledge folders (`.arc42/`, `.domain/`, `.backlog/`,
  `.tech/`, `.design/`) with required `meta` blocks, cross-references, reading order, and
  derived `_meta/` indexes verified by `node .github/tools/knowledge-meta/build.mjs --check`.
  That is structured authoring against a schema, not formatting. Raised to `sonnet`.

## Inherited Defaults

Recorded for readability only — these come from the plugin and are **not** overrides. Do not
turn this list into a second table; the orchestrator parses `## Overrides` above.

- Architecture & Design (`architecture:architect`, `domain-design:domain-architect`) — `opus`.
- Implementation & Coding (`csharp-coding:coding`) — `opus`.
- Testing, QA & Monitoring (`qa:qa`, `qa:qa-monitor`) — `sonnet`. Driving Playwright against
  the harnesses and reading Aspire logs and traces is procedural, tool-heavy work that
  rewards throughput; deliberately left at the default.
- Review (orchestrator, no dedicated agent) — `opus`.
- Human-in-the-Loop (Personal Validation) — no agent and no model; control returns to the user.
- Fallback / Unclassified — session default, left unset on purpose so an uncategorized agent
  inherits the session model instead of getting a guess.

`fable` is available in this session's model list but is not assigned to any category.

## Precedence

A personal global override — `CLAUDE_ORCH_MODEL_SELECTION_PATH`, or
`%USERPROFILE%\.claude\orchestration\model-selection.md` when that variable is unset — takes
precedence over this file. An explicit model instruction given in the current run takes
precedence over both.
