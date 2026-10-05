# TDR 0003: The devbook check is not a zero-warning signal

```meta
date: 2026-10-05
related: [".devbook/arc42/11-risks-and-technical-debt.md#technical-debt", ".devbook/arc42/adr/guidelines/README.md", ".devbook/domain/inbox/requirements.md"]
```

## Status

**Identified** — 2026-10-05 as a record. It has been row D5 in chapter 11 since the
`fix-check-findings` entry of plan `devbook-adoption` (2026-09-26, commit `ae7fc854`).
Accepted for now: the warnings below are expected, and the prose findings each have an
owner. No date is set to drive either to zero.

TDR lifecycle: identified → planned → in-progress → resolved.

## The debt

The debt has two halves.

**The checks print warnings on a clean tree.** Both devbook checks exit 0 with nothing
blocking, so CI passes. They still report hundreds of findings on every run, so a
contributor cannot tell from the output alone whether a change added one.

Measured on 2026-10-05 against `main` at `0425aae9`:

| Check | Result |
|---|---|
| `node .devbook/_tools/devbook-meta/build.mjs --check` | exit 0, 0 errors, 314 warnings |
| `node tools/devbook/check-metadata.mjs` | 0 blocking, 287 advisory (arc42 215, domain 72) |

The 314 warnings from `build.mjs --check` break down as follows. The first four rows
are also the 287 advisories from `check-metadata.mjs`; the last row comes only from
`build.mjs`, which compares chapters with each other.

| Warning | Count | Where |
|---|---|---|
| Heading has no `meta` block | 202 | `##`, `###` and `####` sections inside the numbered ADRs (154) and the inherited guideline ADRs (48) |
| Heading has no `meta` block | 13 | the sections of TDR 0001; every TDR adds its own |
| Heading has no `meta` block | 39 | `### Payload`, `### Consumers` and `### Published language rules` under the 13 domain events |
| Requirement proved only by `unit` tests | 33 | `.devbook/domain/inbox/requirements.md` |
| Two chapters claim the same term | 27 | `domain.md` files across contexts, for example *effort*, *task link* and *attachment* |

The first three rows are structural. A decision or debt record is one document whose
sections are not separately addressable. `devbook-domain.md` names the event
sub-sections as structural and says their warning is never driven to zero. The last
two rows are real findings that nobody has scheduled. The `#### Scenario:` headings
under each requirement are structural in the same way, but the installed checker no
longer warns on them.

This record adds its own sections and those of TDR 0002, so with it in place
`build.mjs --check` reports 339 warnings and `check-metadata.mjs` 312 advisories
(arc42 240).

The issue that asked for this record quoted 528 warnings and 270 advisories. Those
figures are out of date; the table above replaces them.

**Prose findings stand deliberately.** The `fix-check-findings` entry fixed most of
what the prose check reported and left these standing, each with an owner:

- stale names in `.devbook/tech/ai-development.md`, `.devbook/tech/tooling.md` and the
  `.devbook/ai/` openers, owned by the `write-ai-adoption-record` and
  `refresh-tech-graph` entries;
- stale names inside table rows, which the prose check does not read: chapters 02, 10
  and 12 still name `.backlog/` or `.brain/`, and chapter 10 still names `SyncFailed`;
- chapter 09 summarising every ADR instead of linking the index;
- "nothing is provisioned in Azure" in chapters 07 and 08 and `tech/cloud.md`,
  unverified against the deployed sync service;
- chapter 08's claim that mobile and the IDE read the devbook database;
- behaviour still stated in aggregate prose rather than requirements. Inbox,
  Productivity and Roadmap now have a `requirements.md`; only Capture and Inbox have
  an invariants page;
- `deployment` left off Productivity, Monitoring & Dashboard, Environment, Repository
  Management and Technology Stack, where it is undecided.

One item from the original list has since been paid: the technology graph no longer
names `orch-*`.

## Origin

The repository adopted the devbook layout in commits `900e100c` (2026-09-25) and
`0ea8f86c` (2026-09-26). The adopted checker warns on every heading without a `meta`
block, a rule written for chapters that are addressable one section at a time. The
records and the domain events predate that rule.

Commit `ae7fc854` (2026-09-27) cleared every error and most findings. It recorded what
remained as D5 rather than adding `meta` blocks to sections that are not chapters.

## Affected components

| Component | Role |
|---|---|
| `.devbook/_tools/devbook-meta/build.mjs` | Prints the warnings. Installed by the devbook plugin and never edited here. |
| `tools/devbook/check-metadata.mjs` | The repository's metadata gate. Counts the same findings as advisory. |
| `.github/workflows/devbook-meta.yml`, `.github/workflows/devbook-metadata.yml` | Run both checks in CI and pass on exit 0. |
| `.devbook/arc42/adr/`, `.devbook/arc42/tdr/`, `.devbook/arc42/adr/guidelines/` | Hold the record sections that warn. |
| `.devbook/domain/` | Holds the event sub-sections, the unit-only requirements, the duplicate terms and most of the deferred prose. |

## Impact

- **Signal.** A new warning is found by comparing counts with the previous run, not by
  reading the exit code. A reviewer who does not know the baseline misses it.
- **Context quality.** An agent loading a chapter with a deferred prose finding reads
  the stale text as current, until the owning entry lands.
- **Growth.** Every new TDR adds one warning per section heading. Every new ADR and
  domain event does the same, so the count rises with ordinary documentation work.
- **Operations.** None. The application does not read these warnings.

## Severity

**Low.** CI still blocks every error and every unknown field, which are the findings
that break the derived index. It rises to **medium** if a real warning, such as a
broken term or a requirement with no test, goes unnoticed in the noise.

## Remediation options

### Option A — keep the baseline and compare counts (current)

Record the counts in this TDR and compare against them in review.

- *Pro:* no change to an installed tool or to the record format.
- *Con:* relies on someone remembering to compare. The count drifts with every new
  record, so the baseline goes stale quickly.

### Option B — ask the devbook plugin to recognise record sections

Raise upstream that ADR, TDR and domain-event sub-sections are structural, so the
checker stops warning on them.

- *Pro:* removes 254 of the 314 warnings at the source, leaving only real findings.
- *Con:* depends on a release of the plugin; `.devbook/_tools/` cannot be patched here.

### Option C — fix the real findings

Link the Inbox requirements to `e2e` or `integration` tests and give each duplicated
term one owning chapter. Let the owning plan entries clear the deferred prose.

- *Pro:* turns the remaining warnings into zero without hiding anything.
- *Con:* each fix is its own change across several bounded contexts.

### Not an option — add `meta` blocks to every record section

It would silence the warning by declaring sections to be chapters, which they are not.

## Measurable outcome

Resolved when both checks report **0 warnings** on `main`, so that any warning in a
pull request is new. Until then, revisit this record when either happens:

- the devbook plugin ships a release that changes how record or event sections are
  checked;
- the warning count on `main` differs from the table above by anything other than the
  sections of a new ADR, TDR or domain event.

Each prose finding is paid when its owning entry lands; strike it through here.

## Links

- `.devbook/arc42/11-risks-and-technical-debt.md`, *Technical Debt* — row D5.
- `.agents/rules/devbook-domain.md` — why the event sub-section warning is expected.
- `.agents/rules/devbook-chapter-metadata.md` — which headings carry a `meta` block.
- TDR 0002 — the testing convention that would settle the unit-only requirements.
- Commit `ae7fc854` — *Clear the devbook validate and prose findings*.
