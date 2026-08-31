# ADR 0004: Import reuses the entry text grammar; a plan is multi-entry Backlog Entry text

```meta
status: proposed
related: [".domain/backlog/features.md#import", ".domain/backlog/domain.md#backlog-entry", ".design/content-editing.md#scheduling-and-dependency-tokens", ".arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md", ".arc42/adr/guidelines/0014-persistence-and-repository-boundaries.md"]
issue: null
```

## Status

Proposed. Import (`.domain/backlog/features.md#import`) is modelled but not built:
this ADR fixes the format and the persistence path before the feature slice is
written, so the implementation has one decision to follow rather than one to
make up as it goes.

## Context

Import brings in a plan — a sequence of AI prompts to run across one or more
repositories, in dependency order — and turns it into backlog entries in one
step. The obvious way to build that is a dedicated plan format: a YAML or JSON
document with its own schema for prompts, ordering and repository targets, read
by an importer that translates it into entries.

That would be a second grammar for the same fact `EntryTextParser.cs` already
owns. An entry already has a title, a body, sigil and named metadata tokens, and
`##` sub-items. A plan's "prompt" is a `prompt`-type entry; a plan's "ordering
between prompts" is `after:`, the dependency token `.design/content-editing.md`
already defines; a plan's "setup step" or "reminder to update the knowledge
docs" is a sub-item. Nothing about a plan needs a fact an entry cannot already
hold — the earlier work on `.domain/backlog/domain.md#backlog-entry` and
`.design/content-editing.md#scheduling-and-dependency-tokens` this session
already reached that conclusion at the domain and design layers: `import_plan_id`
and `import_item_id` are entry provenance fields, not a second aggregate, and
`id:`/`repo:` are entry metadata tokens, not plan-file syntax.

The one gap between an entry and a plan is that a plan names several prompts in
one document, and an entry is one document. `EntryTextParser.SplitSegments`
already closes it: a second top-level `#` heading starts a new entry, which is
exactly "paste several entries at once" — a capability the format has carried
since ADR 0002, for pasting more than one hand-typed entry in one go, not built
for Import but sufficient for it.

Two token names the grammar does not parse yet, `id:` and `repo:`, appear in
`.design/content-editing.md#scheduling-and-dependency-tokens`'s token table
(added this session) but not in `EntryTextParser.ParseMetadataLine`'s
`switch` — currently they would round-trip as unrecognized `name:value` tokens
under the "unknown tokens survive an edit" rule, read into no field. Adding
their parsing is ordinary work under that same design doc's existing rule ("a
new token MUST be added to the domain model, the entry DTO, and the canonical
rewrite in the same change") and is implementation follow-up, not a further
decision — it does not change what this ADR settles.

Persistence has to answer to `.arc42/adr/guidelines/0014-persistence-and-repository-boundaries.md`:
persistence belongs to the module that owns the data, through an
aggregate-focused repository port, in the one local schema ADR 0003 already
established. An importer that grew its own table for "imported plans" would be
a second persistence surface competing with `ITaskRepository`, in a module that
guideline 0014 says gets exactly one.

## Decision

**A plan is not a file format of its own. It is Backlog Entry text — the exact
hand-typed grammar `EntryTextParser` already implements — with more than one
`#`-titled entry in the document. Import takes a block of that text, splits it
into segments with `SplitSegments`, parses each with `Parse`, and
creates/updates Backlog Entries from the result. One grammar, not two.**

### Intake: upload or paste, one path

A person brings in a plan by uploading a `.md` file or by pasting text
directly — whichever is faster in the moment. Both feed the identical parse
path: a file read is nothing but a way of getting the same string that paste
already produces, and giving it a second code path would only be a second place
for the two to drift.

### Multiple entries per document: no new splitting rule

Import adds nothing to `SplitSegments`. It already treats a second top-level `#`
heading as an entry boundary, which is precisely a plan's "more than one prompt
in this document." Import calls it once, over the whole uploaded or pasted
block, and gets back one segment per entry the plan describes.

### `after:` resolution scope for a fresh batch: two passes, in Import, not in the parser

Within one imported document, `after:<value>` first tries to match another
entry's `id:` token in the *same document*; only if nothing matches is it
treated as a real, already-existing `backlog_item_id`, exactly as it always is
outside Import. This is the general local-id rule
`.design/content-editing.md#scheduling-and-dependency-tokens` already states
for any pasted batch — Import is not a special case of it, it is the case the
rule was written for.

That resolution takes two passes because none of the new entries has a real id
yet when the document is parsed:

1. Parse every segment first, collecting each one's `id:` token alongside its
   parsed fields.
2. Create the entries (or find their upsert targets — see re-import below) and
   obtain real ids, then resolve every entry's `depends_on` list from local
   `id:` values to those real ids before the entries are persisted.

`EntryTextParser.Parse` itself needs no change to do this: it already reads
`after:` as an opaque list of strings (`ParsedEntry.DependsOn`) and validates
nothing about what the values mean — that is what lets a value be a real id one
moment and a same-document local id the next. The two-pass resolution is
Import's own orchestration logic sitting on top of an unmodified parser, not a
parser feature.

### `repo:` resolution + auto-registration: Import-only leniency, not a token change

`repo:<name>` resolves against the Repository Registry by name, the same
resolution `.design/content-editing.md#scheduling-and-dependency-tokens`
already specifies for the token in general. Ordinary single-entry editing
leaves an unrecognized name unresolved — the token's own general rule, stated
already: "this token never registers a repository on its own."

Import is the one caller that relaxes that. An unrecognized name triggers
registration through Repository Management's existing registration capability
(`.domain/repository-management/features.md#repository-registration`) before
the entry naming it is created, so a plan can introduce a repository to the
product just by mentioning it. This leniency belongs to Import specifically —
`.domain/backlog/features.md#repository-resolution-on-import` already scopes it
that way — and is not a change to what `repo:` does everywhere else. Import
triggers registration; it does not perform it, and gains no say over what a
registered repository holds beyond asking for one to exist.

Implementation detail, not a grammar change: `ImportPlanDialog` also offers a
"Target repository" field for the whole batch. `ImportPlanCommand` carries it
as `DefaultRepo`, and the handler applies it to a parsed entry only when that
entry's own text carries no `repo:` — the token itself is unchanged and still
wins whenever it is present, so a plan mixing repositories still works exactly
as this ADR describes.

### Plan identity and tagging: the shared `#tag` is the plan id, nothing else

There is no separate "plan id" field or wrapper document. A plan's identity is
whichever `#tag` every entry in the pasted document happens to share — an
ordinary tag sigil, filed the same way an entry is
[filed against a roadmap tag](../../.domain/backlog/features.md#filing-an-entry-against-a-roadmap-tag).
`import_plan_id` is populated from that shared tag; `import_item_id` is
populated from each entry's own `id:` token. Both are read off tokens the
grammar already carries — nothing new is parsed to produce them.

Entries in a pasted document that share no common tag still import fine,
individually — Import builds nothing entry creation does not already offer, and
a missing shared tag is not a parse failure. What such an entry loses is
something to be *found by* later: with no `import_plan_id`, a later re-import of
"the same plan" has nothing to recognize it against. This is stated here as an
accepted limitation. Import does not enforce a shared tag at parse time,
because doing so would turn an omission in how a plan was written into a reason
to refuse the entries it describes.

### Re-import / versioning: upsert by `(import_plan_id, import_item_id)`

Bringing in a later version of an already-imported plan adjusts entries still
in flight instead of duplicating them, per
`.domain/backlog/features.md#re-importing-an-updated-plan`. For each parsed
segment carrying an `id:` token, Import looks for an existing entry whose
`import_plan_id` (the shared tag) and `import_item_id` (`id:`) both match:

- **Found, not `done`/`archived`** — update in place: content, dependencies
  (resolved per the two-pass rule above), target repository, and
  setup/knowledge/manual sub-items are replaced from the new version.
- **Found, `done` or `archived`** — leave untouched. A later plan version does
  not reopen finished work, the same principle `Occurrence Spawning`
  (`.domain/backlog/domain.md#occurrence-spawning`) already applies to a
  completed recurring entry: a completed thing stays the record of what was
  done.
- **Not found** — create new, whichever version of the plan first introduced
  the prompt.

A segment with no `id:` token is always created new — there is nothing to
match it against, and Import does not guess at identity where the plan did not
state one.

### Storage: through `ITaskRepository`, no new table, no kept raw text

Per ADR 0003 and guideline 0014, imported entries persist through the exact
same repository port and SQLite store every other entry does. Import is a use
case that calls the existing port with the entries it built; it is not a
reason to add a schema. No new table, and no separate "imported plan" record
kept anywhere — the plan's identity already lives on the entries themselves, as
`import_plan_id`/`import_item_id` and the shared tag.

The raw uploaded or pasted text is **not** kept after the import completes.
This is decided explicitly, not by omission, and for the same reasoning ADR
0003 already gave for not migrating old markdown: "an importer is code that
would have to be correct forever to be worth the one time it runs." The
question a kept copy would answer — "what did this import produce" — is
already answered by the parsed entries themselves, carrying `import_plan_id`
and `import_item_id`, which is what re-import actually reads. A stored copy of
the source text would be a second copy of the same fact with no consumer:
nothing in the re-import flow, or anywhere else in the product, reads the
original text back. Keeping it would be paying a permanent storage and
provenance cost for an audit trail nobody has asked for and nothing queries.

## Consequences

Positive:

- No second file format to design, document, version, or keep in step with the
  entry grammar as it grows. Every future token added to
  `.design/content-editing.md#scheduling-and-dependency-tokens` — `due:`,
  `remind:`, `effort:`, anything to come — is automatically available inside an
  imported plan with no Import-specific work.
- `SplitSegments` and `Parse` need no change to support Import's own two-pass
  dependency resolution; the parser's job stays "read one segment" and Import's
  job is entirely its own orchestration on top.
- A hand-typed entry and an imported one are indistinguishable once created —
  same table, same fields, same sub-item shape — which is exactly what
  `.domain/backlog/features.md#import` states as the point ("Import builds
  nothing that entry creation does not already offer").
- Re-import is a plain upsert keyed on two already-modelled fields, with no new
  index shape beyond what querying entries by tag/id already needs.

Negative:

- `id:` and `repo:` still need parsing added to
  `EntryTextParser.ParseMetadataLine` before Import can be built — the grammar
  documents them, the code does not read them yet. This ADR does not close that
  gap; it is ordinary follow-up work under the token-addition rule
  `.design/content-editing.md#scheduling-and-dependency-tokens` already states.
- An entry pasted without a shared `#tag` is importable but not
  re-importable-against: there is no way, after the fact, to tell Import "these
  entries were one plan" once they were saved without the tag. The person has to
  get the tag right on the version they paste, or accept that a later version
  will create duplicates rather than update in place.
- Discarding the raw text means an import cannot be undone by re-reading what
  was submitted, nor can a later reader see the exact wording of the plan as
  authored — only what it produced. If that turns out to matter (a legal or
  audit need, say), it is a deliberately reversible decision: nothing here
  precludes adding a kept-text column later, it is simply not owed today.

Neutral:

- Import is described here as reusing `after:` for prompt ordering rather than
  inventing an import-specific relationship — a plan's dependency and an
  ordinary entry dependency are the same fact, read the same way by
  `.domain/backlog/domain.md#readiness`. Nothing about "what's next in this
  plan" is a plan-specific query; it is the existing readiness/repository
  grouping `.domain/backlog/features.md#import` already points at.
- `repo:` auto-registration is scoped to Import by the feature, not by the
  token — the same `repo:` token used in ordinary editing stays strict
  everywhere else. A future feature wanting the same leniency would need its
  own stated exception, the same way Import's is stated in
  `.domain/backlog/features.md#repository-resolution-on-import`, rather than
  inheriting Import's behavior implicitly.
