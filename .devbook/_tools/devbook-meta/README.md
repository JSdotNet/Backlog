# Devbook metadata tooling

Checks the `meta` blocks embedded in the devbook folders under `.devbook/` —
`arc42/`, `domain/`, `tech/`, `design/`, and `ai/`, at `.devbook/arc42/` and so
on — and, with `--write`, derives three machine-readable indexes from them:

- **`graph.json`** — the reference graph between chapters and files.
- **`index.json`** — the ordered reading outline of each area.
- **`annotations.json`** — the open notes, from the `annotation` fences in the
  chapters themselves.

Markdown stays canonical; these indexes are **derived output** — never edit
them by hand. Placement and naming follow
the `devbook-derived-artifacts` instructions.

## Usage

The checker writes nothing unless asked:

```bash
# Validate every reference and meta block, write nothing — the default; CI runs this
node .devbook/_tools/devbook-meta/build.mjs --check

# Refresh the derived indexes: the plugin that owns them runs this, never a session
node .devbook/_tools/devbook-meta/build.mjs --write

# One scope only
node .devbook/_tools/devbook-meta/build.mjs --write --scope tech

# Point at a repository other than the working directory
node .devbook/_tools/devbook-meta/build.mjs --root ../other-repo
```

The repository root defaults to the working directory. Only devbook folders
that actually exist under `.devbook/` produce a scope, so a repository that
adopts just `domain/` and `arc42/` never grows `_meta/` folders for the rest.
`--scope` takes `tech`, `tech/`, or `.devbook/tech` for the same scope. The
generator exits `2` when no devbook folder is present at all, and a root-level
`tech/` is reported as an error and never indexed — the only layout is
`.devbook/`.

### When to run it

**Not on every edit.** Regenerating the indexes in the same pull request that
edits a chapter is what makes them conflict on merge: two branches that each
touch one chapter both rewrite the same JSON, and the only way to resolve it is
to re-run the generator. So refresh is deliberate and belongs to the plugin that owns
the committed indexes, which ships a `build/Update-DevbookIndex.ps1` wrapper for a
refresh on demand and a scheduled workflow that reconciles the default branch, opening
one pull request when the output drifted and nothing when it did not.

`.github/workflows/devbook-meta.yml` **fails** on a broken reference or a
`meta` block that violates the schema — those are errors in the authored
Markdown and they do not fix themselves — and only **warns** when the committed
indexes have drifted.

That is safe because a consumer reading these indexes at runtime is required to
compare each entry's source file against the index it came from and re-read the
entries that are newer. See `devbook-derived-artifacts.md` for
both halves of the contract.

## Outputs

Three artifacts per adopted scope, each co-located with what it describes:

| Path | Scope |
|---|---|
| `.devbook/_meta/graph.json`, `.devbook/_meta/index.json`, `.devbook/_meta/annotations.json` | repository-wide rollup across all adopted devbook folders |
| `.devbook/arc42/_meta/*.json` | `arc42/` only |
| `.devbook/domain/_meta/*.json` | `domain/` only |
| `.devbook/tech/_meta/*.json` | `tech/` only |
| `.devbook/design/_meta/*.json` | `design/` only |
| `.devbook/ai/_meta/*.json` | `ai/` only |

A scoped graph contains every node in its folder, plus any node **outside** it
that an in-scope node references. Those boundary nodes are flagged
`outOfScope: true` so a viewer can draw them as stubs instead of pretending
they belong to the scope. Inbound references from other folders are not
followed, so a scoped graph stays about its own folder.

## Files

| File | Role |
|---|---|
| `metadata.mjs` | Parses the `meta` blocks — the single implementation of the schema defined by the `devbook-chapter-metadata` instructions. Loaded by `devbook-derived`'s canvas from the materialized path. |
| `graph.mjs` | Graph construction, scope discovery, and scope projection. Imported by the CLI and loaded by `devbook-derived`'s canvas from the materialized path, so the check, the written indexes, and the live view are one parser. |
| `outline.mjs` | Outline generation: root-document resolution (`index: root`, else the `DIRECTORY_CONVENTION` table), numbered ordering, and the per-file lede and diagram count a list view needs. |
| `annotations-index.mjs` | Derives `annotations.json` from the fences: the open-note index every reader comes off, so no reader needs the writer and no reader parses Markdown twice. |
| `annotations.mjs` | The only writer of an annotation fence — `list`, `add`, `reply`, `resolve`, `sweep`, plus a CLI over the same five functions. Edits are surgical, so a field a later version adds survives a write by one that does not know it. `sweep` is the bulk half of `resolve --delete`: it takes every resolved fence in an addressed chapter, bottom-up, and no open one. |
| `build.mjs` | CLI wrapper: writes all three artifacts per scope, prints stats, exits non-zero on errors. |
| `chapter-hash.mjs` | CLI over `metadata.mjs`'s `chapterHash`: prints the content fingerprint of an addressed chapter, the value `approved-hash` records. The approval gate calls it so the value written and the value checked come from one function. |
| `*.test.mjs` | Self-contained checks, one per rule that was worth pinning: run one with `node <file>`, all of them with `node --test "*.test.mjs"`. Each prints `PASS`/`FAIL` per case and exits non-zero on the first failure, so no framework is installed to read them. `behaviour-files.test.mjs` covers the `requirements.md` and invariants-subpage types, the subpage naming and placement checks, the three coverage warnings, and the typed `related` pairing. |

This folder is self-contained — copy it into a repository as
`.devbook/_tools/devbook-meta/` and it runs with no other files installed.

## Output shape: `graph.json`

The required envelope from the derived-index convention, followed by
Cytoscape.js `elements` JSON — consumable directly by Cytoscape and trivially
mappable to D3, vis.js, or Sigma.

```jsonc
{
  "schemaVersion": 5,
  "generatedBy": ".devbook/_tools/devbook-meta/build.mjs",
  "scope": ".devbook/tech",
  "sources": [".devbook/tech"],
  "stats": { "nodes": 57, "edges": 120, "nodesByFolder": { }, "nodesByKind": { }, "nodesByStatus": { } },
  "problems": [],
  "elements": {
    "nodes": [
      { "data": {
          "id": ".devbook/tech/desktop.md#winui-3",
          "label": "WinUI 3",
          "type": "chapter",
          "kind": "framework",
          "folder": "tech",
          "path": ".devbook/tech/desktop.md",
          "status": "candidate",
          "depends-on": [".devbook/tech/desktop.md#windows-app-sdk"]
      } }
    ],
    "edges": [
      { "data": {
          "id": "depends-on:.devbook/tech/desktop.md#winui-3->.devbook/tech/desktop.md#windows-app-sdk",
          "source": ".devbook/tech/desktop.md#winui-3",
          "target": ".devbook/tech/desktop.md#windows-app-sdk",
          "type": "depends-on"
      } }
    ]
  }
}
```

Output is deterministic — no timestamp — so re-running it on unchanged Markdown
produces byte-identical files.

### `type` vs `kind` on a node

Two different questions, two different keys:

| Key | Answers | Values |
|---|---|---|
| `type` | What **role** does this node play in the document structure? | `file`, `chapter`, `heading`, `external` |
| `kind` | What **kind of thing** is it? | The authored `type` metadata field — `aggregate`, `feature`, `framework`, … |

The authored field is called `type` in Markdown but lands on the node as
`kind`, because `type` was already the structural discriminator and renaming it
would break every existing consumer. `tech/` nodes have always carried `kind`;
the unification means every folder that defines a value set now populates it —
`domain/`, `tech/`, and `ai/`. Nodes in `arc42/` and `design/`
carry no `kind`, because those folders deliberately define no value set.

### Node types

| Type | Meaning |
|---|---|
| `file` | A devbook document. `id` is the repo-relative path. |
| `chapter` | A heading that carries a `meta` block. `id` is `<path>#<heading-slug>`. |
| `heading` | A structural heading with no `meta` block, materialized only when something references it. |
| `external` | A reference target outside the devbook folders. |

Nodes carrying `outOfScope: true` sit outside the current scope and are
included only because an in-scope node references them.

A node carrying `openNotes` has that many unresolved annotation fences — on a
`chapter` node its own, on a `file` node the total across the document. The
field is omitted rather than emitted as `0`, so a viewer badges only what has
something waiting. It is counted from the same read that builds the graph, so
the canvas and the committed `graph.json` never disagree about it.

### File node labels

Heading text carries the name only, so all six files of a `domain/` bounded
context are titled with the bare context name. A file node's label is therefore
composed as `<title> (<kind>)`, and the suffix is dropped when the title
already slugifies to the kind:

| File | Title | `type` | Node label |
|---|---|---|---|
| `.devbook/domain/order-management/domain.md` | `Order Management` | `domain` | `Order Management (domain)` |
| `.devbook/domain/order-management/features.md` | `Order Management` | `features` | `Order Management (features)` |
| `.devbook/domain/context-map.md` | `Order Platform` | `context-map` | `Order Platform (context-map)` |
| `.devbook/domain/context-map.md` | `Context Map` | `context-map` | `Context Map` |
| `.devbook/arc42/01-introduction-and-goals.md` | `01. Introduction and Goals` | none | `01. Introduction and Goals` |

Node `id` is the path and was always unique; this only fixes the display label.

The two `context-map.md` rows are the recommended shape and the fallback: title
that file after the system it maps, and reach for the literal `Context Map` only
when there is no meaningful system name. The suppression branch exists so the
fallback does not render as `Context Map (context-map)`.

### Edge types

| Type | Source |
|---|---|
| `contains` | Document structure (file → chapter, chapter → sub-chapter). |
| `depends-on` | The `depends-on` metadata field. |
| `related` | The `related` metadata field. |

`aliases` (`domain/`), `alternatives` (`tech/`), `feature-flag` and `role` (`domain/`),
`stage` (`ai/`), and `roadmap` and `tests` (every folder) are plain-string
fields, not references, so they stay node attributes and produce no edges.
`feature-flag`, `role`, `roadmap`, `stage`, and `tests` accept a scalar or a list but
are always emitted as a list, so a consumer never has to branch on shape. `effort` is emitted as a number rather than the authored
string, so a viewer can total or threshold it directly; a value that is not a
non-negative integer is left off the node and reported as a lint error instead.

The six decision fields — `approved-by`, `approved-at`, `approved-hash`,
`accepted-by`, `accepted-at`, `accepted-hash` — ride along as authored strings on
any node that carries them. They belong to `domain/`'s two rungs, so a node in
another folder should have none; the generator copies what is authored and the
check reports it in the same run, but never invents a value. It reports a
decision with no signature, a signature with no decision, and a fingerprint that
no longer matches the chapter's content.

Everything a block writes under `ext.<plugin>.<key>` is gathered into one `ext`
object on the node, keys and values verbatim. The generator validates none of it
and produces no edge from it — the namespace belongs to whichever plugin is
layered on top of devbook, so a consumer either owns those keys or leaves them
alone.

`date` rides along as the authored `YYYY-MM-DD` string. `number` is emitted as a
number on **file** nodes only, resolved from the `number` field or the filename,
so a TDR node knows it is TDR 7 without the consumer parsing paths. `index`
steers outline generation only and never reaches a node.

### Running a node's linked tests

A node's `tests` entries are `<level>:<runner>:<selector>` identifiers — see
"Linking test cases" in the `devbook-chapter-metadata` instructions for the
format and the vocabularies. `metadata.mjs` exports the mapping from an entry to
a command:

```js
import { testCommand } from "./metadata.mjs";

testCommand("e2e:playwright:tests/e2e/checkout.spec.ts#Guest checkout completes");
// → { level: "e2e", runner: "playwright",
//     selector: "tests/e2e/checkout.spec.ts#Guest checkout completes",
//     command: ["npx", "playwright", "test", "tests/e2e/checkout.spec.ts",
//               "-g", "Guest checkout completes"] }
```

`command` is an argv array meant to run from the repository root, and the
function executes nothing — it is the seam a "run this test" affordance sits on,
so a viewer's run button and this convention's documented command are the same
one. It returns `null` for a malformed entry, and for a runner outside
`TEST_RUNNERS`.

Unlike `effort`, an entry the lint rejects is still emitted on the node.
`testCommand` returns `null` for it, so nothing can act on it by accident, and
keeping it means a chapter that declared four links does not silently render as
three. `parseTestReference` and `testRunners` are exported alongside, for a consumer
that wants the parts of one entry, or the known runners and the selector shape
each expects, without building a command.

## Validation

`--check` exits `1` on any `problems` entry at `error` severity:

| Problem | Severity |
|---|---|
| A `related` / `depends-on` reference that resolves to nothing inside a devbook folder | error |
| Two headings in one file that slugify identically | error |
| A block missing `type` where its folder defines a value set for that level | error |
| A `type` value outside its folder's value set | error |
| A reference pointing outside the devbook folders | warning |
| A `type` set in a folder that defines no value set | warning |
| A literal `` `r`n `` / `\r\n` / `\n` escape sequence in body text | warning |
| `tech/` still using the old `kind` field name | warning |
| An `order` field, removed from the schema — reading order now comes from the folder convention | error |
| An `index` value other than `root` or `exclude` | error |
| Two documents in one directory declaring `index: root` | error |
| Two documents in one directory claiming the same `number` | error |
| A `number` that is not a single non-negative integer | error |
| A `date` that is not a `YYYY-MM-DD` calendar day | error |
| `index` or `number` on a chapter block rather than the file-level block | error |
| A `number` field disagreeing with the number in its own filename | warning |
| A `tests` entry not in `<level>:<runner>:<selector>` form | error |
| A `tests` entry whose level is outside `unit`, `integration`, `e2e` | error |
| A `tests` entry that is a `<path>#<slug>` chapter reference rather than a test identifier | error |
| A `tests` entry naming a runner the tooling has no command for | warning |
| A folder-specific field (`depends-on`, `aliases`, `feature-flag`, `role`, `version`, `alternatives`) on the file-level block | error |
| `domain/` `depends-on` or `feature-flag` on a chapter that is not a `feature` or `sub-feature`, or `role` on one that is not a `user`, `organisation`, or `technical` actor | error |
| An `approved` chapter carrying an open `kind: question` annotation | error |
| `ai/` `stage` entry outside the DevOps loop's eight — `plan`, `code`, `build`, `test`, `release`, `deploy`, `operate`, `monitor` | error |
| `ai/` chapter with no `stage`, other than a `concept` applied throughout — a usage off the loop | warning |
| `ai/` `stage` on a file-level block — a file groups chapters and places none of them | error |
| `ai/` `related` entry reaching into `tech/` — the tool a usage rests on goes in `depends-on`, which is what the loop picture draws | warning |
| A directory missing the root document its folder convention names | warning |

### Literal escape sequences

A tool writing Markdown through a shell can emit an escape sequence instead of
the newline it stands for. The result is silent and out of proportion: a
`## Heading` glued onto the end of the previous line stops being a heading, so
the chapter vanishes from the outline, the graph, and every check that reasons
about headings — including all of the `type` validation above. Nothing else here
can see it, because by then the heading is prose.

So the generator scans body text for a literal `` `r`n ``, `\r\n`, or `\n` and
reports a warning. Fenced code blocks are skipped, as are backticked spans, so
documentation that discusses escape sequences does not trip it. `\t` is
deliberately not matched: it breaks no structure and collides with unformatted
Windows paths. `node escape-lint.test.mjs` covers the cases.

## Output shape: `index.json`

The same envelope, followed by `entries` — a nested, **ordered** tree of the
area's readable content. A viewer walks `entries` top to bottom instead of
sorting filenames.

```jsonc
{
  "schemaVersion": 5,
  "generatedBy": ".devbook/_tools/devbook-meta/build.mjs",
  "scope": ".devbook/domain",
  "sources": [".devbook/domain"],
  "problems": [],
  "entries": [
    { "type": "file", "name": "context-map.md", "path": ".devbook/domain/context-map.md",
      "title": "Shop", "kind": "context-map", "status": "draft",
      "summary": "How the shop's bounded contexts relate.", "root": true },
    { "type": "directory", "name": "ordering", "path": ".devbook/domain/ordering",
      "title": "Ordering",
      "children": [
        { "type": "file", "name": "domain.md", "path": ".devbook/domain/ordering/domain.md",
          "title": "Ordering", "kind": "domain", "status": "draft",
          "summary": "Owns the lifecycle of a customer order.", "diagrams": 1,
          "root": true },
        { "type": "file", "name": "features.md", "path": ".devbook/domain/ordering/features.md",
          "title": "Ordering", "kind": "features", "status": "draft" }
      ] }
  ]
}
```

A numbered area carries `number` on every entry it could resolve one for, and
`date` wherever the document declares one:

```jsonc
"entries": [
  { "type": "file", "name": "README.md", "path": ".devbook/arc42/adr/README.md",
    "title": "Architecture Decisions", "status": "active", "statusDeclared": false, "root": true },
  { "type": "file", "name": "2-record-decisions.md", "path": ".devbook/arc42/adr/2-record-decisions.md",
    "title": "Record Decisions", "status": "active", "statusDeclared": false, "number": 2, "date": "2025-11-02" },
  { "type": "file", "name": "7-use-postgres.md", "path": ".devbook/arc42/adr/7-use-postgres.md",
    "title": "Use PostgreSQL", "status": "active", "statusDeclared": false, "number": 7, "date": "2026-03-04" },
  { "type": "file", "name": "10-adopt-aspire.md", "path": ".devbook/arc42/adr/10-adopt-aspire.md",
    "title": "Adopt .NET Aspire", "status": "active", "statusDeclared": false, "number": 10, "date": "2026-07-19" }
]
```

`number` is resolved from the `number` field or the filename, whichever is
available, and a `directory` entry gets one from a numbered folder name. Both
`number` and `date` are omitted when there is nothing to report, like every
other optional entry field.

On a `file` entry, `kind` is the authored `type` field — what distinguishes six
identically-titled files of a bounded context. It is omitted for folders that
define no value set. On an `area` entry it is the devbook folder itself
(`domain`, `arc42`, …), which is that entry's equivalent answer to "what kind of
thing is this".

### `status` and `statusDeclared`

`status` is always the block's **effective** status, never a raw copy of the
field. In `domain/`, `arc42/`, and `design/` the field is optional and an
absent one means that folder's resting value, `active`, so a block that states
nothing still lands in the right bucket of `nodesByStatus` and still badges as
`active` in a viewer.

Where that resolution happened the entry also carries `"statusDeclared": false`.
It is written **only** when false, for two reasons: a consumer that cares can
distinguish "settled, and the author said so by omission" from a stated value,
and every already-declared entry stays byte-identical to what earlier schema
versions emitted.

`tech/` and `ai/` have no resting value, so an absent status there
resolves to `null` and `validateDocument` reports it as an error. A `null`
status in these artifacts means the corpus is broken, not that the content is at
rest — do not paper over it in a viewer.

### `tests` on a file entry

A `file` entry carries the document's **file-level** `tests` entries, always as a
list. It is there for the same reason `summary` is: a list view wants to badge
what covers each document — "2 tests", "no e2e" — without opening it.
Chapter-level entries are not rolled up into it; those live on their chapter's
node in `graph.json`, which is where a consumer goes for per-chapter detail.

### `summary` and `diagrams`

Two optional fields on a `file` entry, both omitted when they would be empty.
They exist so a viewer can render a devbook folder's **list view** — a lede
under each title, a "3 diagrams" badge beside a chapter — without opening a
single Markdown file. Without them a consumer has to parse the whole corpus to
draw a list, which is the exact cost the index was built to avoid. Both fall
out of the parse the generator already performs, so they cost nothing to emit.

**`summary`** — the document's lede. That is the blockquote the
`devbook-chapter-metadata` instructions place directly after the file-level
`meta` block; a document with no blockquote falls back to its first paragraph
of prose. Either way it is the text before the first `##`, reduced to plain
text (links and emphasis flattened to their content) and capped at 300
characters on a word boundary with an ellipsis. Omitted when the document opens
straight into a chapter.

**`diagrams`** — how many diagrams the document embeds, counting mermaid code
fences and Markdown image embeds across the whole file. Both are diagrams to a
reader, and the devbook folders use images for nothing else. A fence nested
inside a wider fence is content, not a diagram, and is not counted. Omitted when
the document embeds none.

Neither field is a reference, so neither produces a graph edge; they do not
appear in `graph.json` at all.

At the repository scope the top level is `type: "area"` — one entry per
devbook folder, in canonical area order.

Ordering never comes from one document listing its siblings. Per directory:

1. The **root document** sorts first — the file declaring `index: root`, else the
   entry point its folder convention names (`outline.mjs`'s
   `DIRECTORY_CONVENTION` table). A convention-covered directory missing its root
   document is a warning; two `index: root` in one directory is an error.
2. If anything left carries a **number** — from the `number` field, or parsed
   from a numbered filename such as `09-…`, `7-…`, or `ADR-0007-…` — the
   directory sorts by that number ascending, so 10 follows 7. Unnumbered entries
   are filename-sorted after the numbered run.
3. Otherwise the folder convention's prescribed sequence applies, with anything
   else filename-sorted in between.
4. A directory that is neither numbered nor covered by a convention sorts by
   filename.

A document declaring `index: exclude` is left out of the outline but stays a node
in `graph.json` — it is still referenceable content, just not something a viewer
lists. So an area's `index.json` file count can be lower than its
`graph.json` file-node count. See the `devbook-chapter-metadata` instructions.

`_`-prefixed folders (such as `_meta/` itself) are tooling, not content, and
are excluded from the outline.

## Output shape: `annotations.json`

Every annotation thread in the scope, in document order. A note is authored
Markdown living in the chapter it is about; this is the derived half every
reader comes off — the cross-repository inbox, the open-note count on a graph
node, the approval gate showing the objections raised since `approved-at`.

```jsonc
{
  "schemaVersion": 6,
  "generatedBy": ".devbook/_tools/devbook-meta/build.mjs",
  "scope": ".devbook/arc42",
  "sources": [".devbook/arc42"],
  "stats": { "threads": 2, "open": 1, "resolved": 1, "replies": 1, "chapters": 2 },
  "problems": [],
  "threads": [
    {
      "path": ".devbook/arc42/05-building-block-view.md",
      "folder": "arc42",
      // A thread has no id. It is addressed the way devbook addresses
      // everything — a path plus a heading slug — with `ordinal` standing in
      // for the id the schema deliberately does not assign.
      "address": ".devbook/arc42/05-building-block-view.md#devbook-meta",
      "chapter": "devbook-meta",
      "chapterTitle": "Devbook Meta",
      "ordinal": 1,
      "line": 23,
      // "block" annotates the passage above it; "chapter" sits straight after
      // the heading's meta block and annotates the whole chapter.
      "target": "block",
      "kind": "question",
      "status": "resolved",
      "author": "jobsc",
      "date": "2026-09-02",
      "quote": "one indexed range read",
      "body": "Does this hold after the outline gained the roadmap rollup?",
      "replies": [
        { "author": "claude/flow-spec", "date": "2026-09-02", "body": "No second scan." }
      ],
      // Opaque: validated as a mapping of namespaces, never read into.
      "ext": { "your-plugin": { "raised-in": "2026-09-02" } }
    }
  ]
}
```

`quote`, `replies`, and `ext` are omitted rather than emitted empty, so a
consumer never has to tell "no quote" from "an empty quote".

A `resolved` thread is still listed. It lives for the rest of the branch so a
reviewer sees the exchange in the pull request that raised it — the sweep is
what removes it, not the generator. A note that never gets swept is the smell
this index makes visible; `devbook:annotation-sweep` is what removes it.

## Viewing

The **Reference graph** canvas is `devbook-derived`'s `devbook-graph` extension. It loads
`graph.mjs`, `outline.mjs`, and `metadata.mjs` from `.devbook/_tools/devbook-meta/` at
runtime, rebuilds from disk on open, and adds one canvas-only key to its `/api/graph`
response, `testCommands`, mapping each `tests` entry to its resolved argv. That key is
deliberately absent from the committed `graph.json`: a command depends on the tooling
version, not on the Markdown.
