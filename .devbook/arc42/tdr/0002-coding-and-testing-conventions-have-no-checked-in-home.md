# TDR 0002: The coding and testing conventions have no checked-in home

```meta
date: 2026-10-05
related: [".devbook/arc42/11-risks-and-technical-debt.md#technical-debt", ".devbook/arc42/adr/guidelines/README.md#what-was-deliberately-not-imported"]
```

## Status

**Identified** — 2026-10-05 as a record. It has been row D1 in chapter 11 since the
guidelines import on 2026-08-27. Not planned; no owner assigned beyond the repository
owner.

TDR lifecycle: identified → planned → in-progress → resolved.

## The debt

The organization's coding and testing conventions are not written down anywhere in
this repository. An agent or a contributor editing C# here has the architecture
decisions under `.devbook/arc42/adr/guidelines/`, but no statement of how the code
itself should be written or tested.

The conventions this record covers are the coding and testing set from the
*Recommendations* row of `.devbook/arc42/adr/guidelines/README.md`:

- C# coding style;
- testing at the unit, integration, end-to-end and architecture levels;
- object calisthenics;
- the validation strategy;
- logging and audit logging.

The same row lists further conventions that were left behind with them: caching, API
versioning, idempotency, background jobs, Blazor guidance, CI/CD, database migrations,
feature flags, the Aspire start script, Copilot instruction setup and agent skill
authoring. Several of those have since been covered elsewhere, such as the `run` skill
for the Aspire start script and local ADR 0006 for migrations. They are not part of this
record; each is picked up when the work it governs comes up.

## Origin

Commit `715dbe0b` (2026-08-27) retired the `jsdotnet-project-guidelines` and
`jsdotnet-project-design` MCP servers. Their architecture decisions moved into
`.devbook/arc42/adr/guidelines/` and their design guidance into `.devbook/design/`.

The recommendations were deliberately not imported. The guidelines README gives the
reason: they are conventions that apply while code is being edited, so they belong in
an instruction file rather than an architecture chapter. The import marked them as
held for a separate pass, and that pass has not happened.

`.agents/rules/mcp-usage.md` now tells every agent not to consult a guidelines MCP
server. Before 2026-08-27 an agent could look the conventions up; since then there is
nothing to look up.

## Affected components

| Component | Role |
|---|---|
| `.agents/rules/` | The intended home. It holds naming, storybook, UI component and devbook rules, but no coding or testing convention. |
| `.claude/rules/`, `.github/instructions/` | The per-host wrappers each new rule needs, per `.agents/rules/README.md`. |
| `.devbook/arc42/adr/guidelines/README.md` | Lists the conventions as not imported and points here through chapter 11. |
| `tests/` | Every test project. Test structure and naming follow whatever the nearest existing test does. |
| `tests/Backlog.ArchitectureTests` | Enforces module boundaries in code. It is the only convention with a checked-in, executable statement. |

## Impact

- **Consistency.** New code follows the nearest example rather than a stated rule.
  Two agents working in parallel can pick different examples and produce two styles.
- **Review.** A reviewer cannot cite a rule when code drifts from the organization's
  style. The finding becomes a matter of taste.
- **Testing.** The choice between a unit, integration and end-to-end test is made per
  change. The devbook's requirement chapters already show the cost: 33 Inbox
  requirements link only `unit` tests where the domain rule asks for `e2e` (see TDR 0003).
- **Operations.** None. Nothing at runtime depends on these conventions.

## Severity

**Medium.** No user-facing defect follows from it directly. The cost accumulates in
every change that agents write, and most changes in this repository are written by
agents.

## Remediation options

### Option A — one rule per convention group under `.agents/rules/` (preferred)

Write one host-neutral rule per group: C# style with object calisthenics, testing,
validation, and logging with audit logging. Give each a `paths` scope so it loads only
for the files it governs, and add its `.claude/rules/` and `.github/instructions/`
wrappers.

- *Pro:* matches where the guidelines README says these belong. A rule loads while the
  matching file is edited, which is when it is needed.
- *Con:* the content has to be re-sourced from the organization's guidelines and cut
  down to what applies to this repository.

### Option B — import them as guideline documents

Add the conventions under `.devbook/arc42/adr/guidelines/` beside the imported
decisions.

- *Pro:* one import step, the same as the decisions took.
- *Con:* `devbook-arc42.md` says process rules and coding conventions are not
  architecture. A devbook chapter is also task-scoped context, so it would not load
  while code is being edited. Rejected.

### Option C — consult the guidelines MCP server again

- *Pro:* no content to write or maintain here.
- *Con:* reverses the 2026-08-27 decision recorded in `.agents/rules/mcp-usage.md`,
  and makes the conventions unavailable offline and invisible in review. Rejected.

## Measurable outcome

Resolved when:

1. `.agents/rules/` holds one rule per convention group named in *The debt*, each with
   a `paths` scope;
2. each of those rules has its `.claude/rules/` and `.github/instructions/` wrapper;
3. the *Recommendations* row of `.devbook/arc42/adr/guidelines/README.md` points at the
   new rules for the conventions they cover;
4. row D1 in chapter 11 is struck through and this record's status is `resolved`.

## Links

- `.devbook/arc42/adr/guidelines/README.md`, *What was deliberately not imported* — the
  *Recommendations* row.
- `.agents/rules/mcp-usage.md` — the authority order and the retired MCP servers.
- `.agents/rules/README.md` — how a rule and its wrappers are laid out.
- `.devbook/arc42/11-risks-and-technical-debt.md`, *Technical Debt* — row D1.
- Commit `715dbe0b` — *move the org guidelines into .arc42 and stop using the guidelines MCP*.
