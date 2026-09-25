---
name: devbook-chapter-metadata
description: Common per-chapter and per-file metadata convention for domain/, arc42/, tech/, design/, and ai/, so tooling can parse status, dependencies, and cross-references.
---

# Chapter and file metadata

`domain/`, `arc42/`, `tech/`, `design/`, and `ai/` are intended to be read by a
visualization and indexing tooling, not just by humans. To make that
possible, every **chapter** in these folders carries a small, parseable
metadata block directly under its heading, in a fenced `meta` (YAML) code
block, and every **file** carries an equivalent block directly under its
top-level (`#`) heading describing the document as a whole.

A "chapter" here means any heading that these folders' own instructions
already treat as an addressable unit:

- `.devbook/domain/<context>/domain.md` — each Aggregate, Domain Service, Domain Event,
  and each Shared Value Objects / Shared Enums chapter, plus every Entity, Value
  Object, and Enum sub-chapter inside an Aggregate. Those sub-chapters each
  carry their own metadata block; they are not covered by their parent
  Aggregate's block. A chapter split into a `domain.<name>.md` — or into a
  `features.<name>.md` or `skills.<name>.md` below — is the same chapter at a
  different path, per `devbook-domain.md`.
- `.devbook/domain/<context>/features.md` — each Feature and Sub-feature.
- `.devbook/domain/<context>/domain.md`, under its `## Ubiquitous Language` grouping —
  each `Term` chapter.
- `.devbook/domain/<context>/skills.md` — each Feature and Sub-feature, where the
  context describes skills rather than product features.
- `.devbook/domain/<context>/actors.md` — each User, Organisation, and Technical actor.
- `.devbook/arc42/<nn>-<name>.md` — the file's top-level chapter, and any ## section
  inside it that is independently trackable.
- `.devbook/tech/<layer>.md` — each `## <Technology Name>` chapter (one graph node per
  chapter).
- `.devbook/design/<name>.md` — the file's top-level chapter, and every `##` chapter
  inside it. `###` sub-headings are covered by their parent `##` chapter and
  carry a block only if they need independent status or cross-references.
- `.devbook/ai/<nn>-<part>.md` and `.devbook/ai/concepts.md` — each `## <Chapter Name>`
  chapter (one graph node per chapter).

- `domain/` `context-map.md`, `model.md`, `flow.md`, their split files,
  `dependencies.md`, and any additional page a context carries, `tech/`
  `technology-graph.md`, and `ai/` `adoption-map.md` are strategic/structural
  artifacts; their `##` sections do **not** carry per-chapter metadata blocks.

## Chapter metadata block format

Place the block immediately after the heading, before any prose:

```markdown
## <Chapter Heading>

\`\`\`meta
status: draft
type: aggregate
\`\`\`

Prose for this chapter starts here.
```

`type` is the only universally required field, and only in the three folders
that define a value set for it (`domain/`, `tech/`, `ai/`). `status` is
required per folder: mandatory in `tech/` and `ai/`, optional in
`domain/`, `arc42/`, and `design/`, where leaving it out means the content is
at rest — see the `status` entry under **Fields**. Optional fields (`related`,
`issue`, `effort`, `roadmap`, and folder-specific fields such as `depends-on`)
are included only when they have a value; empty collections and null values are
omitted rather than written out.

**The `meta` fence stays even when the block ends up empty.** In `arc42/` and
`design/` there is no `type` field, so a resting chapter with no
relations has nothing left to write:

```markdown
## <Chapter Heading>

\`\`\`meta
\`\`\`
```

That is correct and deliberate, not leftover punctuation. The fence is what
marks the heading as an **addressable chapter** — the derived graph makes one
node per heading that carries a block — so deleting it as noise silently drops
the chapter out of `graph.json` and out of every reference that points at it.

## Headings carry the name, `type` carries the kind

Heading text is the **name of the thing only**. What kind of thing it is lives
in the `type` field, never in the heading:

```markdown
## Order

\`\`\`meta
type: aggregate
\`\`\`
```

not `## Aggregate: Order`. The kind is a classification that changes as
understanding sharpens, and encoding it in the heading makes every such change
churn the heading's anchor and every reference pointing at it. Keeping it in
`type` means a reclassification is a one-line metadata edit.

Anchors are therefore slugs of the bare name —
`.devbook/domain/order-management/domain.md#order`, not `#aggregate-order`.

Headings that name a **grouping** rather than a thing keep their descriptive
text, because that text *is* the group's name: `## Shared Value Objects` is
correct, with `type: shared-value-objects`.

## File-level metadata block

In addition to per-chapter blocks, every file in `domain/`, `arc42/`,
`tech/`, `design/`, and `ai/` carries one file-level metadata block
describing the document as a whole. This gives the tooling a
status/relations rollup for the
file itself, distinct from the status of any individual chapter inside it —
useful for files such as `domain/` `context-map.md`, `model.md`, `flow.md`,
and `dependencies.md`, whose `##` sections don't carry their own per-chapter
blocks.

Place the block immediately after the file's top-level (`#`) heading, before
any blockquote summary, prose, or first chapter:

```markdown
# <File Title>

\`\`\`meta
type: domain
\`\`\`

> Optional blockquote summary, if the file has one.

Prose or the first chapter starts here.
```

The file-level block uses the same fields as a chapter block (`status` required
or optional by folder, exactly as above; `type` required where the folder
defines a file-level value set; `related`, `issue`, `effort`, and `roadmap`
optional) and the same omit-when-empty rule. Folder-specific fields defined for chapters
(`depends-on`, `aliases`, `feature-flag`, `setting`, `role`, `key`, `default`,
`scope`, `deployment`, `version`, `alternatives`) are chapter-scoped and are not used at file level — a file's
overall relationships are expressed through `related` only. The one exception is
`deployment` on a domain `context.md`, which is the bounded context itself.

In `arc42/`, the file's top-level chapter heading (e.g. `# 01. Introduction
and Goals`) already carries a chapter metadata block as described above; for
these files that same block also serves as the file-level block, since an
`arc42/` file is always exactly one top-level chapter — no separate,
duplicate block is added.

Some folders define additional relation fields beyond `related` (e.g.
`depends-on`) — see that folder's own instructions file for
which extra fields apply and what they mean. Most such fields use the same
reference format described below, but not every folder-specific field is a
reference field: in `domain/`, `aliases` (defined in
`devbook-domain.md`) is a list of
plain-string surface names, `role` (same file) is the authorization role name
an actor holds, and `key`, `default`, and `scope` (same file) describe a
feature flag or a setting as the code spells it, none of them
`<path>#<heading-slug>` references — where `feature-flag` and `setting` on a
feature chapter *are* references, to the switch chapters that carry `key` —
in `tech/`, `alternatives` (defined in
`devbook-tech.md`) is likewise a
plain-string list, and in `ai/`, `stage` (defined in
`devbook-ai.md`) is a list of the DevOps
loop's eight stage words. The universal `roadmap` and `tests` fields below
behave the same way.

### Where the folders live

The five folders live under one `.devbook/` parent: `.devbook/arc42`,
`.devbook/domain`, `.devbook/tech`, `.devbook/design`, `.devbook/ai`. The
subfolders **drop the leading dot**: one dot on the parent already signals
"hidden support directory" for everything inside it — the same reason
`.github/workflows` is not `.github/.workflows`. `.devbook/.domain` is not a
devbook folder and resolves to nothing.

A folder is called by its bare name — `domain/`; the dot belongs to the parent
alone — its path is `.devbook/domain/`, and an address is the chapter's real
repository path:
`.devbook/domain/order-management/features.md#feature-checkout`. A root-level
`.domain/` is not a layout: the generator reports one as an error naming the
move and does not index it. Derived `_meta/` folders are written beside the
chapters they index, and the repository-wide rollup under `.devbook/_meta/`.

### Chapter and file references

Chapters are not given a separate stored id. A chapter is addressed by its
file path (relative to the repository root) plus a GitHub-style anchor slug
of its heading text: `<path>#<heading-slug>`, e.g.
`.devbook/domain/order-management/domain.md#order`. This is exactly what
renders as the heading's link target, so it stays correct automatically when
read in any Markdown viewer and never needs to be kept in sync by hand.

A file, addressed at the file-level metadata block, is referenced the same
way but without a heading slug: `<path>`, e.g.
`.devbook/domain/order-management/dependencies.md`. Use this bare-path form when a
`related` entry points at a file as a whole rather than one of its chapters.

Use the `<path>#<heading-slug>` (chapter) or `<path>` (file) form as the
entries in `related` and in any folder-specific relation field (`depends-on`).

### Fields

- **status** (required in `tech/`, `ai/`; optional in `domain/`,
  `arc42/`, `design/`) — lifecycle state of this chapter's or file's
  content.

  The three editorial folders have a **resting value**, `active`, which is
  written by *omitting the field*. A chapter states its status only while it is
  in transition (`draft`, `proposed`) or carries a standing warning
  (`deprecated`); once it settles, the line comes out. Writing `active`
  explicitly is reported, so one state does not end up with two spellings.

  | Folder | `status` | Absence means |
  |---|---|---|
  | `domain/`, `arc42/`, `design/` | optional | `active` — settled content |
  | `tech/`, `ai/` | **required** | nothing; the value is a *rating* on an adoption ladder, and an unrated technology is not the same as a `candidate` one |

  Spell the absence by leaving the field out, never as `status: null` — same
  discipline as `issue: null`, and the reason is the same.


  On top of **`domain/`**'s ladder — and no other folder's — sit two decision
  rungs. The first is `approved`: a person has read this chapter and approved it. It is the decision the approval gate
  makes before a chapter becomes work, recorded in the chapter so it travels
  with the content and lands in the git history like any other change — not in
  flow configuration, and not in someone's memory.

  `approved` is never a resting value and is never omitted to mean itself. A
  chapter states it while the approval stands and drops back to its ordinary
  rung the moment the content changes: an approval is of what was read, not of
  the heading.

  The other four folders have no decision rung. `arc42/` and `design/` record a
  standing structure, `tech/` and `ai/` rate a technology or a way of working
  with one, and the question these rungs answer — did a person agree this, and
  does what was built satisfy it — is asked of the model. The six record fields
  below are `domain/`'s with the rungs: written anywhere else they are not in
  that folder's vocabulary, and are reported as unrecognized.

  A repository that wants that lapse **checked** rather than remembered writes
  `approved-hash` beside the other two.

  The second rung sits above it, `accepted`: a person has seen the implemented
  work against this chapter and accepted it. The two are a **stack, not a choice** —
  `approved` says the specification is right, `accepted` says what was built
  satisfies it, and they are usually stated by different people on different
  days. So an accepted chapter carries both records: `approved-by` and
  `approved-at` stay, with `accepted-by` and `accepted-at` beside them and
  `accepted-at` on or after `approved-at`. An acceptance written over no
  approval is reported; there would be nothing saying the chapter the build was
  accepted against was ever agreed.

  What is accepted is the chapter's content, never a commit — which pull
  request delivered it belongs to the tracker. Both records drop together in
  one change the moment the content changes, and the chapter returns to its
  folder's ordinary rung: a build was accepted against the text that was
  approved, so neither statement outlives it.
- **approved-by** (`domain/` only, optional) — who approved this chapter: a
  person, a handle, or a team. One value, not a list.
- **approved-at** (`domain/` only, optional) — the day they approved it, in
  `YYYY-MM-DD` form.
- **approved-hash** (`domain/` only, optional) — a fingerprint of the content
  that was approved: `sha256:` followed by eight lowercase hex characters. Written by
  the approval gate in the same change as the rung, and never by hand.

  What is fingerprinted is the block a reader would say they read — its heading
  text and everything under it, down to the next heading at the same or a
  higher level, so a `#` file block covers the whole file and a `##` chapter
  covers its `###` subsections. The `meta` blocks are excluded, because the
  value lives in one; the `annotation` fences are excluded, because a note
  written after the approval is not a change to the content; and whitespace is
  normalised, because a reflowed paragraph reads identically.

  Present and not matching, the chapter is reported as an approval standing
  over content that has changed — the one thing the rung claimed and nothing
  could establish, since git answers per file and not per chapter. Absent,
  nothing is reported: the field is optional, and a repository that omits it is
  exactly where it was before.

  Write `approved-by` and `approved-at` whenever `status: approved` is written,
  add `approved-hash` when the repository uses it, and delete all three in the
  same change that drops the rung. An approval nobody signed and dated is
  reported, as is an approval record left behind on a chapter no longer
  claiming the rung — either the approval is current and the status says so, or
  it has lapsed and the record comes out with it. Under `status: accepted` the
  approval record is not orphaned: the acceptance stands on it.
- **accepted-by** (`domain/` only, optional) — who accepted the built work
  against this chapter. One value, not a list.
- **accepted-at** (`domain/` only, optional) — the day they accepted it, in
  `YYYY-MM-DD` form, on or after `approved-at`.
- **accepted-hash** (`domain/` only, optional) — the fingerprint of the content accepted,
  computed exactly as `approved-hash` is. Where both are written they are one
  value, because an acceptance is of the approved content; two that disagree
  say the chapter moved between the two decisions, and are reported.

  The three behave field for field like the approval three: written together,
  deleted together, reported when one is left behind, and off the moment the
  content changes.
- **review** (optional) — where this chapter's review pass stands, on the way to
  that decision: `requested` (waiting on the reviewer), `changes-requested`
  (waiting on the author; at least one open annotation says why), or `cleared`
  (waiting on nobody; no open annotation remains, and the chapter is ready for
  the approval decision). Omitted means no review is running. The three states
  are checked against the notes in the chapter body: `changes-requested` over no
  open fence, or `cleared` over one, is a verdict written without its findings.
- **reviewer** (optional) — who owes the next move: one handle, name, or role.
  Never a list.
- **review-at** (optional) — the day the current review state was written, in
  `YYYY-MM-DD` form.

  The three are written together or not at all, mirroring the approval triad so
  a chapter reads the same way on its way to a decision as it does past one.
  None of it is chapter content: a reader loading a chapter for context skips
  the review fields the same way it skips an annotation fence, and only review
  work — a review skill, a queue, the approval gate — reads them. Approval
  deletes all three in the same change that writes the rung: an approved
  chapter carries the decision, not the road to it. A finding is never a field
  here; it is one `annotation` fence beside the passage it is about.

  The allowed values are folder-specific; see the `status` section
  in `devbook-domain.md`,
  `devbook-arc42.md`,
  `devbook-tech.md`,
  `devbook-design.md`, or
  `devbook-ai.md` for the value set
  that applies to the folder you're editing. A file-level `status` reflects
  the document as a whole and is set independently of its chapters' own
  `status` values (e.g. a file can be `active` overall while one chapter
  inside it is still `draft`).
- **type** (required where the folder defines a value set) — what kind of thing
  this chapter or file *is*: the classification that used to be written as a
  heading prefix. Like `status`, the allowed values are folder-specific and are
  defined in that folder's own instructions file. It applies to chapter blocks
  and file-level blocks alike, with a separate value set for each level where
  the folder distinguishes them.

  Three folders define a value set:

  | Folder | Chapter values | File values |
  |---|---|---|
  | `domain/` | `bounded-context`, `aggregate`, `entity`, `value-object`, `enum`, `shared-value-objects`, `shared-enums`, `ubiquitous-language`, `domain-service`, `domain-event`, `feature`, `sub-feature`, `requirements`, `requirement`, `invariants`, `invariant`, `feature-flag`, `setting`, `user`, `organisation`, `technical`, `term` | `context-map`, `context`, `domain`, `actors`, `features`, `skills`, `requirements`, `invariants`, `model`, `flow`, `dependencies`, or an additional page's own filename |
  | `tech/` | `language`, `runtime`, `framework`, `library`, `package`, `tool`, `service`, `platform`, `protocol`, `format` | none |
  | `ai/` | `practice`, `agent`, `skill`, `plugin`, `mcp-server`, `hook`, `workflow`, `model`, `concept`, `guardrail` | `adoption-map`, `stage`, `concepts` |

  `arc42/` and `design/` deliberately define **no** value set. Their only kind
  distinction — chapter vs section — is already carried by heading level, so a `type` field there would restate the document
  structure rather than add anything. Omit it in those folders, per the same
  omit-when-empty discipline that governs the optional fields; setting it is
  reported as a warning.

  In `tech/` this field was previously spelled `kind`. The old name still parses
  so an existing repository is not broken by a generator sync, but it reports a
  warning — rename it to `type`.
- **related** (optional) — list of `<path>#<heading-slug>` or `<path>`
  references this chapter or file points to for context, without a hard
  dependency (e.g. a `design/` component linking to the domain feature it
  serves, or an arc42 section linking to a domain feature it realizes).
  This is the general-purpose cross-folder tag mechanism, available in every
  folder. Omit the field entirely when there are no references.
- **issue** (optional) — URL (or `owner/repo#number`
  shorthand) of the GitHub issue tracking this chapter or file, if one
  exists. Keep this in sync when a chapter is published to, or synced from, an
  issue tracker. Omit the field entirely when no issue exists.
- **effort** (optional) — estimate of the work this chapter or file
  represents, in **story points**: a single non-negative integer. `effort: 0`
  is a deliberate "no work left" statement and is different from omitting the
  field, which means "not estimated". An effort is an estimate of size, not a
  measurement of time spent, and it is a legitimate outcome for an AI agent to
  derive it from the chapter's content rather than for a person to write it by
  hand — either way it stays an estimate and is revised as understanding
  changes. A file-level `effort` covers the document as a whole and is set
  independently of its chapters' values; it is not required to equal their sum.
  Available in every folder. Omit the field entirely when the chapter has not
  been estimated.
- **roadmap** (optional) — list of roadmap item tags this chapter or file
  contributes to, e.g. `roadmap: [sync-service, mobile-mvp]`. This lets a
  roadmap item gather its chapters by tag instead of having to reference every
  contributing chapter explicitly. Entries are **plain lowercase kebab-case
  slugs, not `<path>#<heading-slug>` references** — like `domain/`'s `aliases`
  and `tech/`'s `alternatives`, they stay node attributes and produce no graph
  edges. The tag vocabulary belongs to the consuming repository's roadmap, so
  it is not validated here beyond the slug shape. A chapter may contribute to
  several roadmap items. Available in every folder. Omit the field entirely
  when the chapter belongs to no roadmap item.
- **date** (optional) — the calendar day this chapter or file records, in
  `YYYY-MM-DD` form: the day a decision was taken, debt was logged, or a record
  was dated. It is **not** a last-modified timestamp — git already knows that,
  and a hand-maintained one goes stale the first time someone forgets. Set it
  where the date is part of the content (a decision record's newest decision, a TDR's logged
  date) and omit it everywhere else. Available in every folder.
- **tests** (optional) — list of the test cases that assert what this chapter
  or file claims, each written as a `<level>:<runner>:<selector>` test
  identifier:

  ```text
  tests: [unit:dotnet:Ordering.Domain.Tests.OrderTests, e2e:playwright:tests/checkout.spec.ts#Guest checkout completes]
  ```

  Entries are test identifiers, **not** `<path>#<heading-slug>` chapter
  references — like `roadmap` they stay node attributes and produce no graph
  edges. Available in every folder, at chapter and file level. Omit the field
  entirely when nothing is linked. See
  "[Linking test cases](#linking-test-cases)" for the format, the level and
  runner vocabularies, and why this field exists where a code-path field
  deliberately does not.
- **number** (optional, **file-level blocks only**) — this document's number
  within its directory, as a single non-negative integer: arc42 chapter 9,
  TDR 2. A numbered filename (`09-architecture-decisions.md`,
  `7-no-retry-budget.md`) supplies the number on its own, so the field is only
  needed when the filename does not carry one, or carries the wrong one. See
  "[Where reading order comes from](#where-reading-order-comes-from)".
- **index** (optional, **file-level blocks only**) — how this document steers
  the generated outline. `index: root` makes it its directory's entry point, so
  it sorts first; `index: exclude` keeps it out of `_meta/index.json` altogether
  while leaving it in the reference graph. Omit the field for an ordinary listed
  document, which is nearly every file.

- **ext** (optional) — the extension namespace: state owned by a plugin layered
  on top of devbook, not by devbook itself. Keys are dotted and namespaced by
  their owner, `ext.<plugin>.<key>`, because the block grammar is flat:

  ```text
  ext.some-plugin.checked: 2026-09-17
  ```

  The generator carries every `ext.*` key through **untouched and unvalidated**
  and produces no graph edge from any of them — it has no opinion on what the
  values mean, and gathers them under one `ext` key on the node so a consumer
  can hand the block back to its owner. Nothing about them is checked, including
  the omit-when-empty rule that governs every other field.

  That is deliberate: without it, an extension that needs to remember one fact
  about a chapter would force a devbook schema change, a contract bump, and a
  migration in every consuming repository. Namespace by the owning plugin so two
  extensions never collide — this file states that convention and the tooling
  does not enforce it, because enforcing it would mean validating the one field
  that must stay opaque.

  Never move a devbook field into `ext` to dodge a rule, and never read another
  plugin's `ext` keys as if they were schema.

`number` and `index` are file-level only because they place the *document* in
its directory. A chapter's position is already its position in the document.

Folder-specific fields (e.g. `depends-on` on feature/tech/ai chapters,
`feature-flag` and `setting` on domain feature chapters, `key`/`default`/`scope`
on domain switch chapters, `role` on domain actor chapters, `deployment` on
domain `bounded-context` chapters and on a domain `context.md`,
`version`/`alternatives` on tech chapters, `stage` on ai chapters and never on an ai file) are
documented in that folder's
own instructions file, not here — this file only defines the fields common
to every folder.

A chapter that is estimated and carried by a roadmap item therefore reads:

```markdown
## Offline Sync Queue

\`\`\`meta
status: ready
effort: 8
roadmap: [sync-service, mobile-mvp]
related: [.devbook/domain/sync/features.md#offline-sync]
\`\`\`
```

## Linking test cases

A chapter states what something does; a test asserts it. `tests` records that
pairing, so a chapter can say what backs it up, a reader can see whether a
capability is covered end to end, and a viewer can offer to run the thing.

### Why a test link and not a code link

This schema deliberately has **no field linking a chapter to a source path** —
see "Counterpart resolution" in `assets/code-sync-protocol.md`, shared by
`capture-specs`, `apply-change`, and `verify-change`. A path in a metadata block rots on the first refactor
and gives no signal when it does, so a chapter and its implementation are paired
through naming instead.

A test link is admissible for exactly the reason a code link is not: **it is
executable**. An entry that stops resolving fails a run, out loud, in the same CI
that runs the suite. That only holds if the entry is something a runner can be
handed — which is why the format is a runner selector and not a file and a
line.

So an entry means *this test asserts what this chapter claims*. It does not mean
"this is roughly the area of code involved". A test that merely touches the same
subsystem is not a link; leave it out.

### Entry format

Three parts, coarse to fine:

```text
<level>:<runner>:<selector>
```

Only the first two colons delimit. A selector routinely contains colons of its
own — a pytest node id, a `file:line` — so everything after the runner
belongs to the runner.

**level** — how far the test reaches:

| Level | Reach |
|---|---|
| `unit` | Pins a rule inside one unit, in memory. |
| `integration` | Crosses a real boundary: a store, a queue, an HTTP surface, another service. |
| `e2e` | Drives the product the way a user does. |

The level is authored rather than inferred from the runner, because one runner
routinely hosts all three — `dotnet` runs unit tests and API integration tests
alike — and "is this covered end to end?" is the question a reader of the
chapter actually has.

**Two chapter types imply their level, and only two.** A `domain/` rule chapter
sits in the file that says what kind of claim it makes, so the level its `tests`
should reach is already decided:

| Type | Level | Why |
|---|---|---|
| `requirement` | `e2e`, or `integration` for a policy no user triggers | It promises something to someone outside the model, so what proves it is the product driven the way that someone drives it. |
| `invariant` | `unit` | It is what the type guarantees no matter who calls it, and a test that has to start the product to reach it is asserting something else. |

A chapter of either type whose `tests` reach no entry at the expected level is
reported as a **coverage warning** — the link may be at the wrong level, or the
rule may be in the wrong file. It is never an error: refusing the document would
teach people to leave `tests` off and write the rule back into prose, which is
what these files exist to replace. Every other type carries no expectation at
all, and a chapter with no `tests` carries none either — see the Rules below.

**runner** and **selector** — which tool runs it, and how that tool addresses
it:

| Runner | Selector | The command it maps to |
|---|---|---|
| `dotnet` | Fully-qualified test class or method name | `dotnet test --filter "FullyQualifiedName~<selector>"` |
| `playwright` | `<spec path>`, optionally `#<test title>` | `npx playwright test <spec> [-g "<title>"]` |
| `vitest` | `<spec path>`, optionally `#<test name>` | `npx vitest run <spec> [-t "<name>"]` |
| `jest` | `<spec path>`, optionally `#<test name>` | `npx jest <spec> [-t "<name>"]` |
| `pytest` | A pytest node id, `<path>::<Class>::<test>` | `pytest "<selector>"` |

Selectors are runner-native, because a runner-native selector is exactly what a
person pastes into a terminal. Where a runner needs a file *and* a title, the two
are joined with `#` — the same separator this schema already uses between a
path and a heading.

Prefer the form that does not name a file, where the runner offers one: a
`dotnet` fully-qualified name survives a file move, a spec path does not. Point
at the narrowest test that actually asserts the chapter, and prefer one class or
one spec over a long list of methods — an entry should be something you would
run.

A runner outside that table is not an error: the level and the selector still say
what covers the chapter. What it loses is the run command, so the generator
reports it as a warning. Teaching the tooling a new runner means adding its
command to `TEST_RUNNERS` in `.devbook/_tools/devbook-meta/metadata.mjs`.

### Rules

- Entries **cannot contain a comma** — that is what separates the bracket
  list. Where a test title contains one, address the test by a form that has
  none: a fully-qualified name, a whole spec file, or a shorter unambiguous
  title.
- A chapter with no `tests` is not a chapter that is untested; it is one that has
  not been linked. The absence deliberately carries no claim, and `tests` is not
  a coverage metric.
- A file-level `tests` field covers the document as a whole — the suite for a
  bounded context, the acceptance suite for a feature. Chapter entries are
  the per-chapter detail. Neither implies the other, and neither has to contain
  the other.
- Delete an entry in the same change that deletes or renames the test it names. A
  run that finds nothing is the signal; a dead entry left behind is a link that
  lies until someone runs it.
- Never write an entry for a skipped, disabled, or commented-out test. A disabled
  test asserts nothing, and linking it makes a chapter look covered when it is
  not.

The absence rule holds for the two typed levels too: a `requirement` with no
`tests` is reported for the level by nothing, because it has claimed no
coverage. What it *is* reported for is having no `#### Scenario:` — a rule with
no case that exercises it is one nobody can tell has been broken — and that is a
coverage warning on the same footing.

A delivered feature, and a domain aggregate whose invariants are pinned,
therefore read:

```markdown
## Guest Checkout

\`\`\`meta
type: feature
effort: 5
tests: [integration:dotnet:Ordering.Api.Tests.GuestCheckoutTests, e2e:playwright:tests/e2e/checkout.spec.ts#Guest checkout completes]
\`\`\`
```

```markdown
## Order

\`\`\`meta
type: aggregate
tests: unit:dotnet:Ordering.Domain.Tests.OrderTests
\`\`\`
```

One of that aggregate's rules, in `invariants.md`, names the test for that one
rule rather than the suite:

```markdown
### Invariant: An order cannot be confirmed twice

\`\`\`meta
type: invariant
tests: unit:dotnet:Ordering.Domain.Tests.OrderTests.CannotConfirmAnAlreadyConfirmedOrder
\`\`\`
```

### Running a linked test

`testCommand("<level>:<runner>:<selector>")`, exported from
`.devbook/_tools/devbook-meta/metadata.mjs`, returns
`{ level, runner, selector, command }` — `command` being an argv array meant
to run from the repository root — or `null` when the entry is malformed or
names a runner with no mapping. It executes nothing, so it is the seam a "run
this test" affordance sits on: the command a viewer offers and the command this
convention documents are the same one.

A repository whose runner needs a different working directory, a project path, or
a config flag wraps that argv. The reference identifies the test; how this
repository invokes its runners is a property of the repository, not of the
chapter.

## Authoring guidance

- Heading text is the name of the thing only. Record what kind of thing it is
  in `type`, never as a heading prefix — a reclassification is then a one-line
  metadata edit instead of an anchor rename that breaks every reference.
- If a chapter heading is renamed, update every relation field entry
  elsewhere (`related` or any folder-specific field) that references its
  old `<path>#<heading-slug>` in the same change.
- If a file is renamed or moved, update every relation field entry elsewhere
  that references its old bare `<path>` in the same change.
- Every new or edited file must have its file-level metadata block; every new
  or edited chapter must have its chapter metadata block. Do not add one
  without the other when creating a new file.
- Re-estimate `effort` when a chapter's scope changes, and drop the field again
  if the chapter stops being something worth estimating. Never raise or lower it
  to make a total come out at a wanted number — the estimate describes the
  chapter, not the report.
- Keep `roadmap` tags spelled exactly as the consuming repository's roadmap
  spells them; a mistyped tag silently drops the chapter out of that roadmap
  item's view rather than failing loudly.
- Link a test only when it asserts what the chapter claims, and remove the entry
  in the same change that removes or renames the test. An entry that no longer
  resolves is caught by running it — which is the whole reason this field holds
  a runner selector rather than a path.
- Do not invent additional top-level fields without updating either this
  file (for a universal field) or the relevant folder's instructions file
  (for a folder-specific field) first — the derived index tooling depends on a
  fixed schema. State owned by a plugin on top of devbook is the exception, and
  goes under `ext.<plugin>.<key>` instead of becoming a new field.
- Optional fields are included only when they carry a value. Empty list-valued
  fields (`related: []`, `depends-on: []`, `roadmap: []`) and null values
  (`issue: null`, `effort: null`) are omitted rather than written out. The same
  rule reaches `status` in `domain/`, `arc42/`, and `design/`, where the resting
  value `active` is what an absent field says: a settled chapter with no
  relations, no estimate, and no issue shows only `type` where the folder
  defines one — and in `arc42/` and `design/`, which define none, an empty
  fence. Keep the fence; it is what makes the heading addressable.

## Where reading order comes from

Files in a devbook folder have an intended reading order that alphabetical
sorting does not capture — `domain/` reads `context` → `domain` → `features`
before `model`, not alphabetically, and TDR 10 comes after TDR 7 rather than
after TDR 1.

That order is never declared by *listing siblings* in one document's block. It
comes from the folder convention, and from what a document says about **itself**
— its number, or that it is its directory's entry point.

Per directory, `_meta/index.json` is generated like this:

1. **The root document sorts first** — the file declaring `index: root`, or
   failing that the entry point its folder convention names:

   | Directory | Root document by convention |
   |---|---|
   | `domain/` | `context-map.md` |
   | `.devbook/domain/<context>/` | `context.md` |
   | `tech/` | `technology-graph.md` |
   | `design/` | `README.md` |
   | `ai/` | `adoption-map.md` |
   | `arc42/` | none — declare `index: root` if the directory has one |

   A directory the convention covers but whose root document is missing is
   reported as a warning. `index: root` overrides the convention, and two of
   them in one directory is an error.

2. **If anything left carries a number, the directory is a numbered set** and
   sorts by that number ascending — arc42 chapters, TDRs, `ai/` usage
   files. The number comes
   from the `number` field, or from a numbered filename when there is no field:
   `09-architecture-decisions.md`, `7-no-retry-budget.md`, and
   `TDR-0007-no-retry-budget.md` all read as numbers. Numbering by filename is
   worth preferring, because it is the ordering a reader sees in a directory
   listing too. Two documents claiming one number is an error; anything
   unnumbered is filename-sorted after the numbered run.

3. **Otherwise the folder convention orders it**: that folder's prescribed files
   in the sequence its instructions file documents in its **Structure** block —
   `domain/`'s `domain` → `actors` → `skills` → `features` → `model` →
   `flow` → `dependencies`, `design/`'s principles-then-tokens run, `tech/`'s `shared.md`
   first and `tooling.md` last — with anything else filename-sorted in between.

4. **A directory that is neither numbered nor covered by a convention sorts by
   filename.**

`index: exclude` drops a document from the outline entirely. It stays a node in
`graph.json`, because it is still real content that other chapters may
reference — it is just not something a viewer lists.

Nothing here lists a directory's contents from inside one of its documents, so
adding or removing a file needs no edit to any *other* file. Regenerate `_meta/`
and it lands in the right place.

Reading order used to be declared in an `order` field on the file-level block,
listing the names of a directory's other entries. That field is **removed** from
the schema: a metadata block describes the thing it sits under, and a
directory's reading order is not a property of one document inside it. A block
that still carries `order` is a validation error — delete it, and use `number`
or `index: root` if the order it declared is not what the rules above produce.

The convention's own part of this lives in the `DIRECTORY_CONVENTION` table in
`.devbook/_tools/devbook-meta/outline.mjs`. Keep that table and the folders'
**Structure** blocks in step with each other.

## Derived metadata index

These metadata blocks are checked by `.devbook/_tools/devbook-meta/build.mjs`,
which builds the reference graph and the reading outline to do it. A layered
plugin may ask the same tool to write them, with `--write`, as derived indexes —
one set per devbook folder plus a repository-wide rollup, placed per that
plugin's `devbook-derived-artifacts.md`:

```text
.devbook/_meta/graph.json          # reference graph, all adopted folders
_meta/index.json          # reading outline, all adopted folders
.devbook/arc42/_meta/graph.json   # arc42/ only
.devbook/arc42/_meta/index.json
.devbook/domain/_meta/…
.devbook/tech/_meta/…
.devbook/design/_meta/…
.devbook/ai/_meta/…
```

Only folders the repository actually has produce a scope.

Regenerate whenever a chapter or file is added, renamed, or re-linked:

```bash
node .devbook/_tools/devbook-meta/build.mjs
```

These are derived output — never edit them by hand. CI
(`.github/workflows/devbook-meta.yml`) fails when a reference does not
resolve or when a committed index is stale. See
the devbook-meta tooling README (`.devbook/_tools/devbook-meta/README.md`) for the output shape.
