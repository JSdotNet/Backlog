# Workflow routing

Route every task through the named orchestration skill for its category. Fall back to the
paired specialist agent only when the skill is unavailable.

For this repository, route through an orchestration skill by default. If a task does
not have a dedicated orchestration skill, use the repo-native generic orchestration
entrypoint `orch-fallback` before falling back to specialist agents directly.

## The gate

**Before the first `edit` or `create` to any file under `src/` or `tests/`, you MUST
invoke the matching `orch-*` skill below.** Exploration first is expected and does not
consume the gate; the trigger is the first write, not the first action.

Apply the gate literally:

- **Size is not a criterion.** A one-control UI tweak and a multi-service feature route
  the same way. Do not reason about whether a request is "big enough" to orchestrate.
- **A missing specification is not an exemption.** Ad-hoc requests with no story,
  acceptance criteria, or approved design still route through `orch-feature` or
  `orch-bug`; the skill derives the missing scope as its first stage.
- **Unmet preconditions are not an exemption.** If the matched skill's stated
  preconditions do not hold, invoke it anyway and say so — do not fall through to
  direct implementation.
- **No match means `orch-fallback`,** not direct implementation.

## Routing table

- Direct `.arc42/` chapter content edits (refreshing an existing chapter or
  diagram, not authoring a new ADR/TDR/blueprint): use `orch-arc42-content`.
  Fall back to `architecture:architect`.
- Architecture, ADRs, arc42 docs, or blueprint work: use `orch-architecture`, `orch-adr`,
  `orch-arc42`, or `orch-blueprint`. Fall back to `architecture:architect`.
- Technical debt records: use `orch-tdr`. Fall back to `architecture:architect`.
- Any change to product code under `src/` or `tests/` that adds or extends behavior —
  including small UI tweaks and incremental extensions to an existing feature: use
  `orch-feature`. Fall back to `csharp-coding:coding`.
- Bug fixes — any change under `src/` or `tests/` that corrects existing behavior: use
  `orch-bug`. Fall back to `csharp-coding:coding`.
- New module scaffolding inside an existing project: use `orch-create-module`. Fall back
  to `csharp-coding:coding`.
- New service scaffolding inside an existing project: use `orch-create-service`. Fall
  back to `csharp-coding:coding`.
- Initial project scaffolding inside an existing, configured repository (`.github/`
  setup, guidelines, solution/app structure): use `orch-project`. For a WinUI 3
  desktop track specifically, fall back to `winui:winui-dev` (not
  `csharp-coding:coding`); for an Aspire/service API track, fall back to
  `csharp-coding:coding`. Confirm the project type (desktop vs. service API) — by
  asking the user or checking `.arc42/04-solution-strategy.md` — before delegating,
  since `orch-project`'s default stages assume an Aspire/API project unless told
  otherwise.
- Dependency or package updates: use `orch-update-packages`. Fall back to
  `csharp-coding:coding`.
- Any change to `.domain/` (bounded-context domain model, features, model
  diagrams, dependencies, naming): use `orch-domain`. Fall back to
  `domain-design:domain-architect`.
- Any change to `.backlog/` (work-item Items/Sub-items, drafting, or
  publishing to GitHub Issues): use `orch-backlog`. Fall back to
  `write-epic`/`write-story`/`write-bug` plus `create-github-issue`/
  `update-github-issue` directly.
- Any change to `.tech/` (technology graph: platforms, runtimes, frameworks,
  libraries, packages, services, tools): use `orch-tech`. Fall back
  to `architecture:architect`.
- Any change to `.design/` (UX principles, dark-mode color tokens, typography and
  layout, interaction guidelines, content editing, accessibility, component-library
  recommendations): use `orch-design`. Fall back to
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
- `orch-domain` and `domain-design:domain-architect` may load `.domain/` as
  working context, but should load only the relevant bounded-context chapters.
- `orch-backlog` and other backlog-writing or issue-writing workflows may load
  `.backlog/` as working context, but should load only the relevant work-item chapters.
- `orch-tech` may load `.tech/` as working context, plus the
  `.arc42` chapters (solution strategy, deployment view, ADRs) that ground the
  stack choices it records.
- `orch-design` and `ux-design:ux-designer` may load `.design/` as working
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

## Repository override for `orch-feature`

The plugin-provided `orch-feature` skill states a precondition that the feature
specification, acceptance criteria, and architecture are already approved. **That
precondition does not apply in this repository.** Invoke `orch-feature` for ad-hoc
feature requests too, and treat scope discovery as part of its first stage: restate the
requested behavior, derive at least one measurable acceptance criterion, confirm it with
the user, then continue through the remaining stages. The same applies to `orch-bug`
when no reproduction has been written up yet.

When a recurring task category has no dedicated orchestration skill yet, recommend
creating one in a separate session so repository routing can stay orchestration-first.
