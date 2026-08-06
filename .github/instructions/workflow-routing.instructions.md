# Workflow routing

Prefer the named orchestration skill for each task category. Fall back to the paired
specialist agent only when the skill is unavailable.

- Direct `.arc42/` chapter content edits (refreshing an existing chapter or
  diagram, not authoring a new ADR/TDR/blueprint): use `orch-arc42-content`.
  Fall back to `architecture:architect`.
- Architecture, ADRs, arc42 docs, or blueprint work: use `orch-architecture`, `orch-adr`,
  `orch-arc42`, or `orch-blueprint`. Fall back to `architecture:architect`.
- Technical debt records: use `orch-tdr`. Fall back to `architecture:architect`.
- Feature implementation spanning planning, coding, and validation: use `orch-feature`.
  Fall back to `csharp-coding:coding`.
- Bug fixes spanning triage, fix, and validation: use `orch-bug`. Fall back to
  `csharp-coding:coding`.
- New module scaffolding inside an existing project: use `orch-create-module`. Fall back
  to `csharp-coding:coding`.
- New service scaffolding inside an existing project: use `orch-create-service`. Fall
  back to `csharp-coding:coding`.
- Dependency or package updates: use `orch-update-packages`. Fall back to
  `csharp-coding:coding`.
- Any change to `.domain/` (bounded-context domain model, features, model
  diagrams, dependencies, naming): use `orch-domain-knowledge`. Fall back to
  `domain-design:domain-architect`.
- Any change to `.backlog/` (work-item Items/Sub-items, drafting, or
  publishing to GitHub Issues): use `orch-backlog-knowledge`. Fall back to
  `write-epic`/`write-story`/`write-bug` plus `create-github-issue`/
  `update-github-issue` directly.
- Any change to `.design/` (UX principles, dark-mode color tokens, typography and
  layout, interaction guidelines, content editing, accessibility, component-library
  recommendations): use `orch-design-knowledge`. Fall back to
  `ux-design:ux-designer`.
- Repository documentation, explanatory docs, or governance text: use
  `documentation:documentation` (no dedicated orchestration skill for this repository yet).
- User flows, wireframes, or UX review (artifacts, not guidelines): use
  `ux-design:ux-designer` (`ux-user-flow`, `ux-wireframe`, `ux-design-review`).
- GitHub issue creation or updates, and pull request automation: use `create-github-issue`,
  `update-github-issue`, and `pr-jsdotnet`.
- End-to-end runtime validation, feature/bug verification against a running app, or
  continuous log/trace monitoring during testing: use `qa:qa` (its `delegate-to-qa-monitor`
  skill invokes the `qa:qa-monitor` persona for log/trace watching). Fall back to running
  `aspire-run` plus `aspire-log-monitor` manually if the agent is unavailable.

## Context loading by orchestration and agent

- `orch-architecture`, `orch-arc42`, `orch-arc42-content`, `orch-blueprint`, `orch-adr`,
  `orch-tdr`, and `architecture:architect` may load `.arc42/` as working context, but
  should load only the chapter(s) relevant to the requested scope.
- `orch-domain-knowledge` and `domain-design:domain-architect` may load `.domain/` as
  working context, but should load only the relevant bounded-context chapters.
- `orch-backlog-knowledge` and other backlog-writing or issue-writing workflows may load
  `.backlog/` as working context, but should load only the relevant work-item chapters.
- `orch-design-knowledge` and `ux-design:ux-designer` may load `.design/` as working
  context, but should load only the relevant guideline file(s).
- Non-architecture implementation, bug-fix, package-update, documentation, and UX flows
  should not load `.arc42/` by default. Consult it only when the user explicitly asks
  for architecture context or when implementation depends on a specific documented
  decision, view, constraint, or glossary term.
- UI implementation and UI bug-fix flows should consult `.design/` when the change
  touches visual design, interaction behavior, content editing, or accessibility —
  loading only the relevant guideline file(s), not the whole folder.

- `orch-feature` and `orch-bug` already delegate their local-run/validation stage to
  `qa:qa` internally — do not invoke `qa:qa` separately when already inside one of those
  orchestrations. Invoke `qa:qa` directly only for standalone QA/testing requests that
  aren't part of a full feature/bug orchestration run.

If neither the preferred skill nor its fallback agent is installed, use the closest
available specialist agent and note that orchestration routing was unavailable.
