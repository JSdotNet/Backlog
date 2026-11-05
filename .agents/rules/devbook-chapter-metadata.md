---
name: devbook-chapter-metadata
description: Common per-chapter and per-file metadata convention for domain/, arc42/, tech/, design/, and ai/, so tooling can parse kinds, ratings, dependencies, and cross-references.
---

# Chapter and file metadata

`domain/`, `arc42/`, `tech/`, `design/`, and `ai/` are intended to be read by
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
  carry a block only if they need independent cross-references.
- `.devbook/ai/<nn>-<part>.md` and `.devbook/ai/concepts.md` — each `## <Chapter Name>`
  chapter (one graph node per chapter).

- `domain/` `context-map.md`, `model.md`, its split files,
  `dependencies.md`, and any additional page a context carries, `tech/`
  `technology-graph.md`, and `ai/` `adoption-map.md` are strategic/structural
  artifacts; their `##` sections do **not** carry per-chapter metadata blocks,
  and the check asks for none. Never demote such a section to a bold label.
- A journey's `###` parts are the same: they carry no block and stay
  addressable as `<scenarios file>#<part>`, per "[Journeys](#journeys)".

## Chapter metadata block format

Place the block immediately after the heading, before any prose:

```markdown
## <Chapter Heading>

\`\`\`meta
type: aggregate
\`\`\`

Prose for this chapter starts here.
```

`type` is the only universally required field, and only in the three folders
that classify every chapter with it (`domain/`, `tech/`, `ai/`); `design/`
types its requirement chapters and nothing else. `rating` is required on every
block in `tech/` and `ai/` and exists nowhere else — see the `rating` entry
under **Fields**. Optional fields (`related`, `tests`, and folder-specific fields such
as `depends-on`) are included only when they have a value; empty collections
and null values are omitted rather than written out.

A block describes the content, never a workflow over it. A merged chapter is in
force: no field says it is a draft, under review, approved, or on its way out —
see "[Moved to tooling](#moved-to-tooling)".

**The `meta` fence stays even when the block ends up empty.** In `arc42/`, and
on every `design/` chapter but a requirement, there is no `type` field, so a
chapter with no relations has nothing left to write:

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

`### Requirement:`, `### Invariant:`, and `#### Scenario:` keep their prefix,
in `domain/` and, for requirements, in `design/`: the heading shape is
OpenSpec's, and a tool that reads OpenSpec finds them by it.

## File-level metadata block

In addition to per-chapter blocks, every file in `domain/`, `arc42/`,
`tech/`, `design/`, and `ai/` carries one file-level metadata block
describing the document as a whole. This gives the tooling a
relations rollup for the file itself, distinct from any individual chapter
inside it — useful for files such as `domain/` `context-map.md`, `model.md`,
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

The file-level block uses the same fields as a chapter block (`type` required
where the folder defines a file-level value set; `related` and `tests`
optional; `rating` required in `tech/` and `ai/`, exactly as above) and the
same omit-when-empty rule. A file-level `rating` rates the document as a whole
and is set independently of its chapters' ratings. Folder-specific fields defined for chapters
(`depends-on`, `aliases`, `feature-flag`, `setting`, `role`, `key`, `default`,
`scope`, `deployment`, `version`, `alternatives`, and `ai/`'s `stage`, `substage`, `layer`, `feeds`, and `evidence`) are chapter-scoped and are not used at file level — a file's
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
plain-string list, and in `ai/` (defined in `devbook-ai.md`), `stage` is a list of
the delivery path's seven stage words, `layer` a list of its three layer words,
`substage` a list of `<stage>/<word>` entries in the repository's own words, and
`evidence` one word — while `feeds` *is* a reference field, each entry a
`<path>#<heading-slug>` reference, a colon, and the arrow's label. The universal `tests` field below
behaves the same way.

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

A proposed change lives outside the parent, in `openspec/changes/<name>/`; its
`proposal.md` and deltas are checked but are no part of the graph. Their
shape is `devbook-changes.md`'s.

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

A reference runs from a change to the devbook, never the reverse: a devbook
chapter's `related`, `depends-on`, or prose link into `openspec/changes/` is an
error. A delta may name a chapter its own change adds, by the path it lands at.

### Fields

- **rating** (required on every block in `tech/` and `ai/`, and nowhere else) —
  the adoption rating of a technology, or of a way of working with one:
  `candidate`, `trial`, `adopted`, `hold`, or `retired`. A rating is a decision
  the branch makes, so it is content and always stated: an unrated technology
  is not the same as a `candidate` one. The ladder is fixed, because its five
  words are the one adoption vocabulary a reader learns across repositories and
  a radar draws. What each word means in its folder is `devbook-tech.md`'s and
  `devbook-ai.md`'s. The field was spelled `status` until contract 38; the old
  spelling is reported as renamed.
  content changes.
- **type** (required where the folder defines a value set) — what kind of thing
  this chapter or file *is*: the classification that used to be written as a
  heading prefix. Like `rating`, the allowed values are folder-specific and are
  defined in that folder's own instructions file. It applies to chapter blocks
  and file-level blocks alike, with a separate value set for each level where
  the folder distinguishes them.

  Four folders define a value set:

  | Folder | Chapter values | File values |
  |---|---|---|
  | `domain/` | `bounded-context`, `aggregate`, `entity`, `value-object`, `enum`, `shared-value-objects`, `shared-enums`, `ubiquitous-language`, `domain-service`, `domain-event`, `feature`, `sub-feature`, `requirements`, `requirement`, `invariants`, `invariant`, `scenario`, `feature-flag`, `setting`, `user`, `organisation`, `technical`, `term` | `context-map`, `context`, `domain`, `actors`, `features`, `skills`, `requirements`, `invariants`, `scenarios`, `model`, `dependencies`, or an additional page's own filename |
  | `tech/` | `language`, `runtime`, `framework`, `library`, `package`, `tool`, `service`, `platform`, `protocol`, `format`, `plugin`, `skill`, `agent` | none |
  | `ai/` | `practice`, `agent`, `skill`, `plugin`, `mcp-server`, `hook`, `workflow`, `model`, `concept`, `guardrail` | `adoption-map`, `stage`, `concepts` |
  | `design/` | `requirement` | none |

  `arc42/` deliberately defines **no** value set. Its only kind distinction —
  chapter vs section — is already carried by heading level, so a `type` field
  there would restate the document structure rather than add anything. Omit it,
  per the same omit-when-empty discipline that governs the optional fields;
  setting it is reported as a warning.

  `design/`'s one value marks out chapters rather than classifying every one: a
  `### Requirement:` under a component is `type: requirement`, per
  `devbook-design.md`, and every other `design/` chapter omits `type`.

  In `tech/` this field was previously spelled `kind`. The old name still parses
  so an existing repository is not broken by a generator sync, but it reports a
  warning — rename it to `type`.
- **related** (optional) — list of `<path>#<heading-slug>` or `<path>`
  references this chapter or file points to for context, without a hard
  dependency (e.g. a `design/` component linking to the domain feature it
  serves, or an arc42 section linking to a domain feature it realizes).
  This is the general-purpose cross-folder tag mechanism, available in every
  folder. Omit the field entirely when there are no references.
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
  references — like `aliases` they stay node attributes and produce no graph
  edges. Available in every folder, at chapter and file level, except on a
  journey, where it is refused: the journey is the test. Omit the field
  entirely when nothing is linked. See
  "[Linking test cases](#linking-test-cases)" for the format, the level and
  runner vocabularies, and why this field exists where a code-path field
  deliberately does not.
- **demo** (optional) — list of the places in a demo that show what this
  chapter or file claims, each `<path>#<id>`: the repository path of a
  `*.demo.html` and the id of a screen, a state, or a walkthrough its
  `demo-model` lists, or the bare `<path>` for the demo as a whole:

  ```text
  demo: [.devbook/domain/ordering/features.demo.html#checkout, .devbook/domain/ordering/features.demo.html#checkout-declined]
  ```

  Available on any chapter in any folder, and on the file-level blocks of a
  change's `proposal.md` and `solution.md`, per `devbook-changes.md`. A
  walkthrough is **free** or **linked**:

  - A **free** walkthrough carries its own `text` per step. A requirement names
    one per inline Given/When/Then scenario, beside its `tests`: the walkthrough
    shows the case, the test proves it, and its id is that scenario's slug.
  - A **linked** walkthrough shares its journey with a journey's part. It
    keeps its own `id` and adds `part: "<scenarios file>#<part>"`, the link,
    written from the demo's folder or as a repository path, and
    `signature: "<8 hex>"`, the journey signature its text was copied at. Each step
    adds `step`, the 1-based index of the part step it shows — ascending, and
    a part step may be left out — and keeps
    `screen`, `anchor`, `role`, `flags`, and `text` — an embedded copy of that
    step's English text, so the demo plays with nothing fetched. Its screens'
    copy still follows `domain.md`'s language. A `signature` that is not the
    journey's current one makes it stale until its text is re-copied.
  - A **playthrough** plays a whole journey end to end. It keeps its own `id`
    and adds `journey: "<scenarios file>#<journey>"`, written as `part` is, and
    the journey's `signature`. Each step adds `part`, the anchor of the part it
    shows, and `step`, that part step's 1-based index, in the journey's order —
    a part step may be left out, a whole part is warned — and embeds that step's
    text as a linked walkthrough does. A walkthrough carries `part` or
    `journey`, never both.
  - A requirement whose `#### Scenario:` is a `Proved by:` line gets its
    walkthroughs derived, the way its `e2e` test is: every walkthrough in any
    `*.demo.html` under `domain/` whose `part` names that part. A hand-written
    walkthrough entry on such a requirement is redundant (warning); only an
    inline case's walkthrough is written by hand.

  The shape is the authoring reference opening the demo template. Quote an
  address whose `flags` lists more than one key, since a comma otherwise ends
  the entry. Links run from Markdown to the demo; the one link out of a demo
  is a linked walkthrough's `part`, which names the journey part it
  plays. Beyond it a demo names no chapter, and its own metadata script,
  `<script type="application/json" id="demo-meta">`, holds `question` — the one
  question it was prototyped to answer — and nothing else: no stage, status,
  verdict, or page, because where the file sits already says which it is. What
  a demo is and where it lives is `devbook-domain.md`'s. Omit the field
  entirely when no demo shows the chapter.
- **playthrough** (optional, `domain/` `feature` and `sub-feature` chapters
  only) — list of the playthroughs the feature's page shows, each
  `<path>#walkthrough/<id>`: a demo's repository path and the id of a
  playthrough its `demo-model` lists, whole — no step and no query:

  ```text
  playthrough: [.devbook/domain/ordering/features.demo.html#walkthrough/retry-a-declined-card]
  ```

  The journey it plays names the feature in its `related` (warning
  otherwise). A tool that shows the page offers the first entry, and the
  demo is folded into the chapter's fingerprint as a `demo` entry's is. When
  it plays is the host's — spec-manager, Backlog — through devbook's
  `devbook.demo.address@1` contract; how it looks is the repository's demo
  template's. Omit the field when no playthrough walks the feature.
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
- **c4** (optional, `arc42/`, `domain/`) — the C4 model elements this chapter or
  file is about, as bare identifiers of `.devbook/arc42/workspace.dsl`:
  `c4: [backlog.cloud.api]`. Each must name an element the workspace defines.
  Like `tests`, the entries are not `<path>#<heading-slug>` references; each one
  is an edge to an element, and an element's chapters, records, and code are
  read back along those edges, never stored on it.
- **c4-touches** (optional, `arc42/`, `domain/`) — the elements this chapter's
  consequences name but its decision is not about, in the form of `c4`. An
  element belongs in one of the two fields, never both.
- **code** (optional, an `arc42/building-blocks/<slug>.md` file only) — the
  repository-relative folders that hold the block's code:
  `code: [plugins/devbook/tools/devbook-meta]`. Folders, never files. The block
  names its element in `c4` beside it, per `devbook-arc42.md`.
- **severity** (required on an `arc42/tdr/` debt record, and nowhere else) — `low`, `medium`, `high`, or
  `critical`: what the debt costs while it stands. A record without it is reported as a
  warning, never an error, so a debt set written before the field keeps validating until each is ranked.
  Where the debt stands — identified, planned, in progress, resolved — is the
  host's state, per "[Moved to tooling](#moved-to-tooling)".

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
`version`/`alternatives` on tech chapters, `stage`, `substage`, `layer`, `feeds`, and
`evidence` on ai chapters and never on an ai file) are
documented in that folder's
own instructions file, not here — this file only defines the fields common
to every folder. The C4 fields above are the exception: `c4` and `c4-touches`
span two folders, and `code` and `severity` sit beside them so the model's
fields are read in one place.

## Moved to tooling

Workflow state is not content, and a block carries none. A branch's devbook
states only what is true of that branch's code, so a merged chapter is in force
and nobody approves it afterwards. State exists for three subjects only — a
change proposal, a decision record under discussion, and a debt record — and
the host that runs the workflow keeps it outside git, answering
`devbook.decision@1`, the devbook plugin's `resources/decision.md`. Each host keeps its own;
a skill says which one answered it.

| Field | Where it went |
|---|---|
| `status` in `domain/`, `arc42/`, `design/`, on a journey, on a change's `proposal.md` | Nowhere for an ordinary chapter. A decision record's `proposed` is the host's state while its pull request is open; a change proposal's `draft`, `approved`, `accepted` are the host's, over the proposal's fingerprint. A chapter that should no longer be followed is removed by a change's `REMOVED` delta, per `devbook-changes.md` |
| `status` on a debt record | The host's state for the record. The record's prose says what resolved it |
| `approved-by`, `approved-at`, `approved-hash`, `accepted-by`, `accepted-at`, `accepted-hash` | The host's state for the change proposal, keyed by the fingerprint of the devbook plugin's `resources/chapter-fingerprint.md` |
| `.devbook/statuses.json` | The host's configuration per product. The check no longer reads the file |
| `sync` | Backlog's direction map per repository and folder |
| `issue` | The tracker's reference to the chapter |
| `effort` | A change proposal's tasks, and the work item each becomes |
| `roadmap` | Dropped. Backlog's goals and roadmap plans group the work |
| `change` | Dropped. Git history and the archived change folder record the merge |

The check reports each field by name as an error that names the
`038-workflow-state-leaves-the-chapter` migration, which deletes it; `status` in
`tech/` or `ai/` is reported as renamed `rating`, and the migration renames it.
`review`, `reviewer`, and `review-at` left in contract 21 and are reported the
same way. Never move a dropped field into `ext` to keep it.

## Sync units

The converters and `verify-change` read a chapter together with what it owns,
as one **sync unit**: an aggregate with the entities, value objects, and enums
it owns, the domain events it raises, its invariants, and the requirements and
terms that name it; a domain service the same way; a feature with its
sub-features and requirements; one `feature-flag` or `setting` chapter; one
`user` or `technical` actor chapter; one `arc42/building-blocks/<name>.md` file;
one `##` component chapter of `design/component-libraries.md`. An
`organisation` roots no unit. `.devbook/_tools/devbook-meta/units.mjs` lists
the units and groups them; which units a sweep levels, and which way, is
Backlog's and never a field.

A `domain-event` chapter names the `aggregate` or `domain-service` that raises
it in `related`. That entry is what places the event in its unit; an event
without it is reported and belongs to none.
## Linking test cases

A chapter states what something does; a test asserts it. `tests` records that
pairing, so a chapter can say what backs it up, a reader can see whether a
capability is covered end to end, and a viewer can offer to run the thing.

### Why a test link and not a code link

This schema deliberately has **no field linking a chapter to a source file** —
see "Counterpart resolution" in the devbook plugin's `assets/code-sync-protocol.md` — never
materialized into a repository — shared by
`capture-specs`, `apply-change`, and `verify-change`. A path in a metadata block rots on the first refactor
and gives no signal when it does, so a chapter and its implementation are paired
through naming instead. The one exception is a building block's `code`: a
folder outlives the files in it, the block is already the chapter that
documents its element's code, and a listed folder that no longer exists is
reported as drift.

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
coverage. What it *is* reported for is having no `#### Scenario:` — a promise
with no case that exercises it is one nobody can tell has been broken — and that
is a coverage warning on the same footing. A requirement that no longer holds
is removed by a change's `REMOVED` delta, not kept with a warning on it. An
`invariant` carries no scenario and is never reported for lacking one.

A delivered feature, and a domain aggregate whose invariants are pinned,
therefore read:

```markdown
## Guest Checkout

\`\`\`meta
type: feature
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

One of that aggregate's rules, in `domain.invariants.md`, names the test for that one
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

## Journeys

A **journey** is one end-to-end walk through the product, written as a `##`
chapter typed `scenario` in its bounded context's scenarios file —
`scenarios.md`, a split `scenarios.<name>.md`, or a split's subpage
`scenarios.<name>.<page>.md`. Its `###` headings are the
journey's **parts**, and its steps are list items under them. Keep two words apart
in every rule: the journey is the `scenario` chapter; a **scenario case** is a
`#### Scenario:` under a requirement, which points at one part with `Proved by:`
per `devbook-domain.md`. Because a journey is a chapter, any number of features and
requirements point at it through `related`, and it points back.

| Element | Convention | Checked |
|---|---|---|
| Recognition | `type: scenario` in the block under a `##` of a scenarios file. Every `##` there is a journey. The type on a file-level block, or on a chapter of any other file, is an error; `032-scenarios-md` folds the pages of contract 31 into `scenarios.md`. | Yes |
| Location | `.devbook/domain/<context>/scenarios.md`, `scenarios.<name>.md`, or `scenarios.<name>.<page>.md`, in the context holding most of the requirements whose cases point at its parts, any of them on a tie. Another context is a warning; any other folder an error. `.devbook/scenarios/` holds profiles and data sets, never a journey. | Yes |
| Address | `<scenarios file>#<journey>`, the journey's anchor in its file. It is the journey's identity for every tool; the same name in two files is two journeys. | Yes |
| Anchors | Every journey and part heading is unique in its file, so `<scenarios file>#<anchor>` names one. | Yes, a repeat is an error |
| Language | English only: headings, steps, captions, labels, and every keyword. | The keywords only |
| Lead | One paragraph under the journey's block: who does what, and why it matters. | No |
| Setup fields | `start`, `actor`, `data`, `profile`, `flags`, `settings` on the journey's block, per the table below. | Yes, the references resolve |
| Parts | Each `###` under a journey is one part, titled as an outcome the actor reaches. A part, and any heading under it, carries no `meta` block; a part stays addressable as `<scenarios file>#<part>`. | Yes, the exemption |
| Steps | List items opening with a bold **Given**, **When**, **Then**, or **And**. One action or one observable outcome per step, and no other keyword. | Yes, the keyword set |
| Screenshot point | `![caption](shot:<label>)` between step lists, never inside one, and inside a journey. The label is `[a-z0-9-]+`, unique in the file. | No: the tool that takes it checks it |
| Reference elsewhere | `![caption](scenario:<context>/<scenarios file>#<label>)` shows a screenshot on another page; `related: [<scenarios file>#<journey>]` links a journey and `#<part>` a part. | The reference resolves |
| Fields | `related`, `date`, `aliases`, `ext.*`, and the six setup fields. `tests` is an error: the journey is the test. A merged journey is in force; what proves it is its run. | Yes |
| Runs | `<scenarios file without .md>/<journey>/`: `run.json` and one PNG per label, beside the file, so a subpage's journey runs into `scenarios.<name>.<page>/<journey>/`. | By the scenario tools |

The setup fields say where the journey starts and in which configuration it runs.
They are legal on a journey's block and nowhere else. A reference is written from
the scenarios file's folder — its bounded context — or from the repository root.

| Field | Meaning | Resolves to |
|---|---|---|
| `start` | The route the journey opens on, relative to the profile's base URL, as `[portal:]/route`. A bare route opens on the profile's first portal. | A portal in the profile |
| `actor` | Who is signed in at the start, as `[portal:]<path>#<slug>`; a journey that crosses portals lists one per portal. | A `user`, `organisation`, or `technical` chapter |
| `data` | The named test data sets imported, in order, before the journey starts. | A data set in `.devbook/scenarios/data/<name>/` |
| `profile` | The named configuration to run under; absent means `default`. | A profile in `.devbook/scenarios/profiles.json`, when the repository has one |
| `flags` | The feature flags switched on for this journey; a leading `-` switches one off. A switch modelled as a `setting` goes under `settings` as `=true` or `=false`. | A `feature-flag` chapter |
| `settings` | The settings set for this journey, as `<path>#<slug>=<value>`. | A `setting` chapter |

The effective configuration is the profile with the journey's `flags` and
`settings` on top. Put a flag or a setting on the journey only when it depends on
it; one that describes the environment belongs in the profile. Never write a
secret or a URL as a value: the profile names an environment variable as
`env:<NAME>`.

```markdown
# Scenarios

\`\`\`meta
type: scenarios
\`\`\`

The end-to-end journeys of the work context.

## Set up and fill the backlog

\`\`\`meta
type: scenario
related: [features.md#backlog-board, features.md#backlog-items]
start: /products/webshop/backlog
actor: actors.md#product-owner
data: [webshop-without-statuses]
flags: [context.md#new-board]
\`\`\`

A product owner sets up the backlog of a new product, adds the first item and moves it on.

### Statuses are set up

- **Given** a product "Webshop" with no backlog statuses
- **When** I create the statuses "New", "Doing" and "Done"
- **Then** they appear as board columns in that order

![The board with three empty columns](shot:statuses-set-up)
```

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
  fields (`related: []`, `depends-on: []`) and null values (`version: null`) are
  omitted rather than written out: a chapter with no relations shows only `type`
  where the folder defines one — and in `arc42/`, and on a `design/` guideline,
  an empty fence. Keep the fence; it is what makes the heading addressable.

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
   `dependencies`, `design/`'s principles-then-tokens run, `tech/`'s `shared.md`
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
plugin's own rule for `_meta/`:

```text
.devbook/_meta/graph.json          # reference graph, all adopted folders
.devbook/_meta/index.json          # reading outline, all adopted folders
.devbook/arc42/_meta/graph.json   # arc42/ only
.devbook/arc42/_meta/index.json
.devbook/domain/_meta/…
.devbook/tech/_meta/…
.devbook/design/_meta/…
.devbook/ai/_meta/…
```

Only folders the repository actually has produce a scope.

Run the check whenever a chapter or file is added, renamed, or re-linked:

```bash
node .devbook/_tools/devbook-meta/build.mjs --check
```

The indexes are derived output — never edited by hand, and never regenerated in a
session: where a repository commits them, the layered plugin's own refresh path writes
them. CI (`.github/workflows/devbook-meta.yml`) fails when a reference does not resolve;
whether a committed index has drifted is that plugin's question and never a failure. See
the devbook-meta tooling README (`.devbook/_tools/devbook-meta/README.md`) for the output shape.
