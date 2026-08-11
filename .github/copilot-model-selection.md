# Copilot Model Selection Overrides

Backlog is a .NET Aspire product (`src/`, `tests/`, `harness/`) that also carries an unusually
large checked-in knowledge base (`.arc42/`, `.domain/`, `.backlog/`, `.tech/`, `.design/`)
driving what gets built. The plugin's code- and runtime-oriented defaults suit the code side
well, so only the planning side is overridden below. Categories not listed keep the
`copilot-app` plugin defaults.

| Category | Model |
| --- | --- |
| Planning & Product Definition | Claude Opus |

Rationale:

- **Planning & Product Definition** — the `.backlog/`, `.arc42/`, and `.domain/` artifacts are
  first-class products of this repository rather than a throwaway precursor to code, and they
  are what implementation is derived from, so they warrant the strongest reasoning family
  rather than the default mid-tier one.

`Implementation & Coding` and `Testing, QA & Monitoring` were previously overridden on the
premise that this repository had no C# and nothing to run. That is no longer true — there is
a full solution, an Aspire AppHost, and an automated test suite — so both now correctly fall
back to the plugin's coding-specialised defaults.

Values are model **family** names (or `auto`), never version-pinned model IDs; the
orchestrator resolves each family to its latest non-legacy release at run time, per the
`copilot-app` plugin's `instructions/orch-model-selection.instructions.md`.
