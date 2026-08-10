# Copilot Model Selection Overrides

Backlog is a documentation, backlog, and architecture-knowledge repository (`.arc42/`,
`.domain/`, `.backlog/`, `.tech/`, `.design/`) with no application runtime, no build, and no
test suite. Its "output" is reasoning-heavy prose and modelling rather than code, so the
overrides below trade the plugin's code- and runtime-oriented defaults for stronger general
reasoning. Categories not listed keep the `copilot-app` plugin defaults.

| Category | Model |
| --- | --- |
| Planning & Product Definition | Claude Opus |
| Implementation & Coding | Claude Opus |
| Testing, QA & Monitoring | auto |

Rationale:

- **Planning & Product Definition** — backlog artifacts are the primary product of this
  repository, not a precursor to code, so they warrant the strongest reasoning family rather
  than the default mid-tier one.
- **Implementation & Coding** — "implementation" here means authoring governed Markdown
  knowledge artifacts, not writing C#; a coding-specialised family is the wrong match.
- **Testing, QA & Monitoring** — there is nothing to run or test (see
  `.github/copilot-orch-context.md`), so this category is left to the runtime for the rare
  tooling-script case instead of reserving a heavyweight coding family.

Values are model **family** names (or `auto`), never version-pinned model IDs; the
orchestrator resolves each family to its latest non-legacy release at run time, per the
`copilot-app` plugin's `instructions/orch-model-selection.instructions.md`.
