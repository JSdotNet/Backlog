# Archify artifacts for knowledge chapter diagrams

The knowledge folders — `.domain`, `.arc42`, `.tech`, `.design` — carry their
diagrams as mermaid fences inside the chapters. Those fences stay canonical:
they are what the Markdown says, what a pull request diffs, and what the desktop
app draws by default.

This folder adds a second way to look at one of them. [Archify][archify]
re-authors a diagram as a specification and renders a self-contained HTML
document from it, and the desktop app shows that document in place of the drawn
mermaid where one exists and matches. It is a visualization layer over the same
canonical fence, not a replacement for it — `.design/component-libraries.md` is
unchanged, and mermaid is still what renders a knowledge diagram.

The feature is behind the `archify-diagrams` flag on the settings screen, off by
default.

[archify]: https://github.com/tt-a1i/archify

## Where things live

```
.domain/tasks/flow.md              the chapter, with its mermaid fences
.domain/tasks/_archify/
    index.json                       sha256(fence) -> which artifact is whose
    flow.1.workflow.json             the specification, authored by hand
    flow.1.workflow.html             the artifact, rendered from it
```

A specification is named `<chapter>.<ordinal>.<type>.json`, where the ordinal is
the position of the mermaid fence in the chapter, counting from 1, and the type
is one of Archify's five: `architecture`, `workflow`, `sequence`, `dataflow`,
`lifecycle`. Everything the renderer needs is in the filename, so a specification
carries no metadata block that could disagree with where it sits. The one
optional extra segment is the quality profile — see below.

The type is not always the default for the mermaid keyword. A `flowchart` may be
authored as `architecture`, `lifecycle` or `dataflow` when that is what it
actually means; several chapters here are. Both the scanner and the app therefore
*find* a specification by listing `_archify/` rather than by computing its name
from the keyword — otherwise a diagram authored at a non-default type would be
invisible, and the app would never offer to render it. Exactly one specification
may exist per chapter and ordinal; two is an authoring mistake and is reported as
an error rather than silently resolved.

## How an artifact is matched to a diagram

By the SHA-256 of the fence, normalised to LF endings with no trailing
whitespace and no blank lines at either end. Not by name, path or ordinal:
`DiagramView` is handed a source and a language and nothing else, and an ordinal
shifts the moment somebody inserts a diagram above it.

Hashing also settles drift, which is the failure this whole arrangement has to
avoid. An artifact is a *re-authoring* of a diagram rather than a rendering of
one — nothing inside it points back at the mermaid it came from — so an edited
fence would otherwise keep showing the old picture with complete confidence.
Instead the edit changes the hash, the lookup misses, and the reader gets the
mermaid with a note saying the artifact is out of date.

The rule is written twice, and the two must agree exactly:
`normalizeDiagramSource` in `archify-artifacts.mjs`, and `DiagramSourceHash` in
`src/Core/Backlog.UI.Components/Diagrams/DiagramArtifacts.cs`. If they drift,
every lookup misses and the app quietly shows mermaid everywhere — the one
failure mode this design cannot detect from the inside. Unit tests pin both.

## The commands

All of them are run from the repository root and need nothing installed beyond
Node: the generator is vendored under `tools/archify/` and has no
`node_modules`.

```powershell
# Every knowledge chapter diagram and what state its artifact is in.
node tools/diagrams/archify-artifacts.mjs scan
node tools/diagrams/archify-artifacts.mjs scan --missing
node tools/diagrams/archify-artifacts.mjs scan --json
```

The states are the ones the app has to tell apart, because each one means a
different offer under the diagram — or none:

| state | meaning |
| --- | --- |
| `rendered` | the hash matches an index entry and the artifact is on disk |
| `stale` | an entry names this chapter and ordinal, but for an older hash — the fence was edited |
| `unrendered` | a specification exists and no artifact does |
| `missing` | no specification, and Archify has a type for this kind |
| `unsupported` | no Archify type fits — every `classDiagram` in the repository |
| `error` | two specifications claim the same chapter and ordinal |

```powershell
# What to write for one diagram, and where. Prints the fence; writes no stub.
node tools/diagrams/archify-artifacts.mjs scaffold .domain/tasks/flow.md 1

# Render one specification, or everything unrendered and stale.
node tools/diagrams/archify-artifacts.mjs render .domain/tasks/_archify/flow.1.workflow.json
node tools/diagrams/archify-artifacts.mjs render --all

# Exits non-zero if anything is stale or unrendered.
node tools/diagrams/archify-artifacts.mjs verify
```

`scaffold` deliberately writes no stub file. An unauthored stub that happens to
validate is worse than no file at all: `scan` would then call the diagram
`unrendered`, and the app would offer to render nothing.

`render` shells the pinned Archify `deliver`, which validates before it writes
and exits non-zero if it cannot. A specification is only accepted at 9/9 checks
with no errors, and — at the default `showcase` profile — no warnings either. On
success the command writes the index entry —
the only thing that will ever connect the artifact back to the fence — and drops
whatever used to hold that ordinal, so a re-render after an edited fence leaves
no leftover matching text nobody can see any more.

## Motion is opt-in, and every specification has to ask for it

Every specification's `meta` must carry:

```json
"animation": "trace"
```

Archify gates all motion on it. `meta.animation` is an `enum ["trace", "none"]`
in all five schemas and **its default is static**, so a specification that says
nothing renders a diagram that never moves. One flag drives both halves:
`svgRootAttrs` puts `data-animation="trace"` on the diagram's `<svg>`, which is
the only thing the artifact's Motion Governor reads to decide the document is
motion-capable, and `animateAttr` puts the matching `data-animate` +`--step`
hooks on the edges and nodes the keyframes run on. Without the flag the artifact
still embeds the whole animation stylesheet — it just has nothing to apply it to.

All 42 specifications shipped without it and every artifact was silently static.
Nothing reported it, and nothing can: a static artifact is a complete, valid,
nine-of-nine-checks artifact. It renders, it exports, it passes every gate
Archify has. `ArchifyArtifactMotionTests` in `tests/Backlog.ArchitectureTests`
is what notices now, and it checks the rendered HTML as well as the
specification, because the two go out of step whenever a specification is edited
without a re-render.

**Do not go looking for the attribute with a whole-file grep.** Every artifact
embeds a stylesheet full of `svg[data-animation="trace"] [data-animate="edge"]`
selectors, so `grep -l data-animation` matches all 42 whether they animate or
not — which is exactly what sent the first investigation down the wrong path.
Match the diagram's own tag instead:

```bash
grep -o '<svg viewBox[^>]*>' .arc42/_archify/06-runtime-view.1.sequence.html
```

Motion stays reader-controlled. The trace runs once, finitely; reduced-motion,
page hiding, print and canonical export all preserve the complete static
meaning, and the artifact's own Live/Still control keeps the last word. Inside
the app, `backlogDiagrams.renderArtifact` lifts embed mode's blanket
`animation: none` so the artifact's governor can decide — without that lift, a
correctly generated artifact still would not move.

## Re-rendering after editing a specification

`render --all` will not do it. It renders what `scan` calls `stale` or
`unrendered`, and both are judged from the **mermaid fence hash** — which does
not change when you edit a specification. Change a spec and `--all` reports
`Nothing to render.` and exits 0, which reads exactly like success.

Pass the paths instead:

```powershell
node tools/diagrams/archify-artifacts.mjs render (git ls-files '*_archify/*.json' | Where-Object { $_ -notlike '*index.json' })
```

`--all` is for the case it was built for: a fence was edited, so the artifact is
genuinely stale.

## The two diagrams that render at `standard`

Archify has two composition profiles. `showcase` is the default and the bar
everything in this repository is held to. `standard` demotes four rules —
crossings, ambiguous corridors, label-route clearance and desktop readability —
from errors to warnings. It relaxes nothing else: edge-through-node,
endpoint-side-direction and label-overlap are enforced identically at both, and a
`standard` render still fails on any error.

Three diagrams here render at `standard`, and not for want of trying:

| diagram | why |
| --- | --- |
| `.domain/context-map.md` #1 | the relationship graph is non-planar — a K3,3 subdivision drawn straight from the fence |
| `.arc42/05-building-block-view.md` #3 | contains a complete K3,3: `{UI Layer, Local Storage, JSON Indexes}` × `{Inbox Service, Backlog Service, Knowledge Service}` |
| `.arc42/05-building-block-view.md` #2 | no proof of impossibility — three residual crossings nobody has managed to remove. See the note below; this one is different from the other two. |

`showcase` raises `composition/proper-crossing` as an error for any crossing
between two relationships that share no endpoint. In a non-planar graph at least
one such crossing is forced in *every possible drawing* — that is a topological
fact, not a layout problem, and routing an edge the long way round cannot help.
The only ways to reach `showcase` would be to drop a relationship or rewire one,
and both would make the artifact say something the chapter does not.

So they render at `standard`, and say so. A specification opts out by naming the
profile in its filename:

```
.domain/_archify/context-map.1.architecture.standard.json
.domain/_archify/context-map.1.architecture.standard.html
```

`showcase` is the default and is never written into a name, so there is exactly
one spelling of the ordinary case. `render` reads the profile from the name,
prints it along with the warning count, and records `quality` in the index entry.
Anything other than `standard` in that position is rejected — it is not a second
way to name a type.

**This is an exception, not a dial**, and the exceptions are not all of one
kind. Two of them rest on a proof and one does not, and that difference is worth
keeping visible rather than blurring the three together:

- `.domain/context-map.md` #1 and `.arc42/05-building-block-view.md` #3 are
  **provably non-planar**. A crossing is forced in every possible drawing. No
  amount of further effort changes that.
- `.arc42/05-building-block-view.md` #2 is **not known to be impossible** — it
  has no K3,3, and the agent that got closest judged the remaining three
  crossings "unfound, not impossible". It renders at `standard` because two
  attempts across very large budgets could not close it, and because a complete
  faithful drawing with three crossings was judged better than no artifact at
  all. That is a decision about effort, not a theorem. If somebody later finds
  the clean embedding, this one should move back to `showcase`.

Before reaching for the opt-out, establish which of those two you are in, and say
so. A ratio of edges to planar capacity (3V−6) does *not* answer the first
question: `05-building-block-view.md` #3 sits at 0.41 on that scale, well inside
what looks safe, and is non-planar. Only an embedding search settles it.

Note also that #2's obstruction was **clustered** planarity rather than plain
planarity — the graph is planar, but the six subgraph rectangles the fence
declares fix a block topology that forces crossings. Dropping the rectangles and
carrying group membership as per-node tags plus cards took the same edge set from
131 diagnostics to 3. If a dense diagram will not lay out, the rectangles are the
first constraint to question, not the last.

The context map's artifact carries 26 warnings: 2 crossings (one forced crossing
counted twice, because the chapter draws `Technology Stack ↔ Dev PC Management`
in both directions), 23 label-route-clearance, and 1 desktop-readability. That
last one is worth knowing: keeping 30 long relationship labels off the context
boxes forces a wide canvas, and node text falls below Archify's own 6px floor
under roughly 2048px of viewport. It reads well on a large display and is a
squint on a laptop. Shortening the relationship labels is the lever if that ever
becomes a problem — not tolerating a crossing, which is the part that reads fine.

## Authoring a specification

There is no mermaid-to-Archify converter, and there is not going to be one:
Archify's types describe meaning the mermaid does not state. Authoring is an
agent reading the diagram and writing fresh JSON, following
`tools/archify/SKILL.md` — including its step 2, which requires reading a
matching example from `tools/archify/examples/`.

The working loop:

1. `scan --missing` is the worklist.
2. `scaffold <chapter> <ordinal>` gives the fence, the target path, the default
   type and any alternatives.
3. Write the specification. Say what the diagram says; do not add anything it
   does not. Include `"animation": "trace"` in `meta` — see above; it is not the
   default and nothing downstream will tell you it is missing.
4. `render <spec>` and require 9/9 checks, 0 errors, 0 warnings.
5. `verify` at the end.

Inside the app the same two steps are two buttons under a diagram, and they are
the only two states offered:

- a specification exists and the artifact is missing or stale → **Render
  artifact**, which runs the same `render` command in the configured clone;
- no specification and Archify has a type for this kind → **Author with agent**,
  which opens an agent session with a brief naming the chapter, the fence, the
  target path and the type;
- no type fits → no button at all, because an offer is a promise.

## What Archify cannot say

Worth knowing before authoring, because each of these is a place where a
faithful specification still produces a picture that differs from the mermaid:

- **Lifecycle secondary lanes are pinned under main-rail columns 2–4.** A state
  whose partner belongs at column 1 is placed unfaithfully even when every
  transition is correct.
- **The lifecycle renderer draws an empty third band** when only two lanes are
  declared.
- **`participants[].type` is required and component-shaped**, so re-authoring a
  sequence diagram asserts a classification the mermaid never stated.
- **Mermaid node shapes are not preserved.** A stadium, a rhombus and a
  rectangle come back as the same box.
- **No Archify type fits a `classDiagram`.** All twelve in this repository are
  bounded context aggregate models, and none of the five types can say
  "aggregate root", "value object" or `0..*`.

In the app there is one more: the theme is pinned from outside the frame, so the
artifact's own theme toggle does nothing there. It is hidden along with the rest
of the toolbar; open the `.html` directly for the full viewer.

## Updating the vendored generator

See `tools/archify/UPSTREAM.md`. The pinned commit is recorded there, and the
artifacts in the repository were rendered by it — bumping it means re-running
`render --all` and reading the diff, not just accepting it.
