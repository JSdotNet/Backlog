---
name: devbook-ai
description: Structure and authoring rules for the AI devbook folder, recording how this project develops with AI — which practice, agent, skill, or model is applied at which stage of the seven-stage delivery path or which layer beneath it, what each hands to the next, the concepts underneath them, and how far adoption has actually got.
---

# AI adoption (`ai/`)

`ai/` is the durable record of **how this project develops with AI** — which AI
capability is applied at which stage of the delivery path, what it hands on,
what concepts it rests on, and how far adoption has actually got.

It is organized by the **path**, not by the tool. The question `ai/` answers is
"at this stage of how we work, what do we use AI for, and is that real yet?".
The question it does not answer is "what is Claude Code and which version are we
on" — that is `tech/`. Every chapter says in its metadata which stages or layers
it works at, so a tool can draw the answer — see
[The loop picture](#the-loop-picture).

> `ai/` describes how **we build the product**. AI shipped **inside** the
> product — a model call in a feature, a retrieval pipeline the user hits — is
> ordinary architecture and belongs in `arc42/`, `domain/`, and `tech/`.
> Cross-link with `related` where the two touch, and keep the boundary sharp:
> an agent that reviews our pull requests is `ai/`; an agent the customer talks
> to is product.

## The boundary with `tech/`

**`tech/` stays the registry.** An AI tool with a vendor and a version — Claude
Code, an MCP server, a model provider, a CI agent — is registered as a `tech/`
chapter like any other technology, with `tech/`'s `status` recording its maturity
as a technology in this project.

`ai/` never re-registers it. An `ai/` chapter names the **usage**: the practice,
the agent persona, the skill, the guardrail — the thing that exists only because
of how we chose to work — and points at the registered technology with
`depends-on`.

| Fact | Folder |
|---|---|
| "We use Claude Code, version X, `adopted`" | `tech/` |
| "At `specify` we draft devbook chapters with the devbook skills, `trial`" | `ai/` |
| "The coding agent persona, and when we hand work to it" | `ai/` |
| "Anthropic's API is a service dependency of the build pipeline" | `tech/` |
| "Every agent-authored change is reviewed by a human before merge" | `ai/` |

The test: **if it has a vendor and a version, it is a `tech/` chapter.** If it is
a decision about how we work, it is an `ai/` chapter. An `ai/` chapter whose
`depends-on` points at nothing in `tech/` is usually fine — most practices,
concepts, and guardrails have no product behind them.

## Context-loading policy

- `ai/` is **not** baseline repository context. Load it when the task is about
  how the team works with AI — adopting a tool into the path, changing a
  practice, reviewing adoption, onboarding someone into the way of working.
- When `ai/` is needed as task context, load `adoption-map.md` plus only the
  files in scope, not the whole folder.
- An agent does **not** read `ai/` to decide how to do its own current task.
  These chapters are the record of a way of working, not instructions to follow;
  instructions live in instruction files and skills.

## The stages

The delivery path has seven stages, in this order, and they are not the
repository's own:

| # | Word | Display name | Asks |
|---|---|---|---|
| 1 | `decide` | Decide | What is worth doing next? |
| 2 | `specify` | Specify & plan | What must be true, and in which steps? |
| 3 | `code` | Code | Build the agreed change. |
| 4 | `verify` | Verify | Does my change work? |
| 5 | `integrate` | Integrate | Is it safe to merge with everything else? |
| 6 | `release` | Release & deploy | Get it to the user. |
| 7 | `operate` | Operate & feedback | What is it telling us? |

Stages 1–4 sit before the pull request, in the author's session; stages 5–7
sit after it. `operate` feeds `decide`, which closes the path into a loop. A
fixed vocabulary is what lets a tool draw one path for every repository and a
reader compare two. A stage the flow does not use stays empty rather than being
dropped, and the emptiness is the finding.

Underneath the path run three **layers**. They are not stages: they work across
all of them.

| Word | Layer | Is |
|---|---|---|
| `thread` | Spec ↔ code thread | What keeps the spec and the code level — written once, checked at every stage. |
| `upkeep` | Upkeep loop | What keeps the repository healthy on a cadence, landing its work as pull requests at Integrate. |
| `harness` | Agent harness | What every session and schedule stands on. |

A stage may split into **sub-stages**, and the repository names its own: split
a stage only when its parts answer different questions with different tools.
A chapter says where it sits with `stage`, `layer`, or both, on its own block —
never through the file it is in.

## Structure

```
.devbook/ai/
  adoption-map.md       # root: how the path reads here, the adoption diagram
  01-<part>.md          # usage files: chapters grouped by the part of the flow they cover
  02-<part>.md
  …
  concepts.md           # the ideas the practices rest on
  _meta/graph.json      # derived: generated reference graph, never hand-edited
  _meta/index.json      # derived: generated reading outline, never hand-edited
```

**A file groups chapters; it places none.** Split usage files by whatever reads
best — one per stage of the path, one per part of the repository's own flow, one
per theme — and number them so the folder reads in the order the work happens.
Reading order needs no declaration: `adoption-map.md` is the root document and
sorts first, the numbered files follow, and `concepts.md` sorts after them. See
`devbook-chapter-metadata.md`. A chapter's position is its `stage` and `layer`
fields, whichever file it is in, so moving a chapter between files changes
nothing in the picture.

## File responsibilities

- **adoption-map.md** — Root strategic view of AI across the path.
  - Lists the usage files and what part of the flow each covers.
  - Renders the adoption picture as one Mermaid `graph LR`: the seven stages
    as nodes chained in path order, `decide --> specify --> … --> operate`, with
    `operate --> decide` closing the loop and a `pr` node between `verify` and
    `integrate`; one `subgraph` beneath them holding the three layers, `thread`,
    `upkeep`, and `harness`, as nodes, each present even when empty; and each
    chapter as a node joined to every stage or layer it lists with `---`,
    shaded by `status` through one `classDef` per rung. A `feeds` entry is a
    labelled `-->|label|` edge between two chapters. It is the hand-drawn form
    of the [adoption view](#the-loop-picture), read from the same fields, so
    the two never disagree.
  - Explains the status ladder and how to read and extend the folder.
  - Its `##` sections do **not** carry per-chapter metadata blocks; the file
    carries a file-level block only — the same rule as `.devbook/domain/context-map.md`
    and `.devbook/tech/technology-graph.md`.
- **`<nn>-<part>.md`** — One `## <Chapter Name>` chapter per usage. Each chapter
  is an addressable node in the graph, carries a chapter metadata block, and
  says where it works with `stage`, `layer`, or both.
- **concepts.md** — The ideas the practices rest on. A concept carries `stage`
  or `layer` where it applies at particular ones and omits both when it applies
  throughout.
- **`_meta/*.json`** — Derived, generated indexes for this folder. Never
  hand-edited; the rule for them comes with the layered plugin that commits the index.

## The loop picture

`ai/` is authored so a tool can draw it as three views of the same chapters.
Every element of every view is one field on a chapter, and nothing is authored
twice.

**Adoption** — how far each usage has got. One row per stage, a pull-request
divider after `verify`, then one row per layer; the columns are the status
ladder.

| In the view | Read from |
|---|---|
| The rows, in order | the seven stages, then the three layers — authored nowhere |
| A card in a row | a chapter whose `stage` or `layer` lists it; a chapter listing several sits at its first stage, else its first layer, and names the rest |
| The card's column | its `status` |
| The card's evidence mark | its `evidence` |
| What the card waits on | its **Moves up when** bullet |
| Since when | its `date` |

**Cohesion** — how the parts form a whole. The stages as columns with the pull
request between `verify` and `integrate`, and the layers as bands beneath.

| In the view | Read from |
|---|---|
| A node | a chapter, in the column or band the adoption view gives it |
| A labelled arrow | a `feeds` entry: to the chapter before the colon, labelled with the text after it |
| A dotted support link | a `related` entry |
| The platforms row | the `tech/` chapters the `depends-on` entries name, with how many usages rest on each |

**Model** — the path as a model of work.

| In the view | Read from |
|---|---|
| The seven stage headers | the fixed vocabulary, with the pull request before `integrate` |
| A sub-stage card under a stage | a `substage` entry `<stage>/<word>`, holding every chapter that lists it, in order of first appearance |
| A stage with no sub-stages | one unnamed card holding the chapters at that stage |
| The layer rows beneath | the chapters whose `layer` lists each one |

In every view a stage with no chapter renders empty. That is the picture doing
its job, so never fill a stage with a chapter that names a tool and no usage.
The stage's own shading is the highest rung among the chapters at it — derived,
authored nowhere. A `concept` with neither `stage` nor `layer` applies
throughout and sits in no row. The tools a stage runs on that no AI usage rests
on are `tech/`'s, and `tech/` is not organized by stage.

## Chapter template

````markdown
## <Chapter Name>

```meta
status: trial
type: practice
stage: [code, verify]
substage: [verify/review]
feeds: [".devbook/ai/02-deliver.md#merge-review: the draft pull request"]
evidence: partial
depends-on: [".devbook/tech/tooling.md#claude-code"]
related: [".devbook/ai/concepts.md#context-engineering"]
date: 2026-09-01
```

One or two sentences: what this is, in this project.

- **Used for** — the concrete work it carries at these stages.
- **Adopted by** — who actually works this way today, and where. This is the
  field that keeps `status` honest.
- **Evidence** — what shows it works, or what would have to be true to promote
  it: a merged pull request, a measured cycle, a retro. `none yet` is a
  legitimate value and a useful one.
- **Moves up when** — the one event that would move the rating up a rung. Only
  on a `trial` or `candidate` chapter.
- **Limits** — where it is deliberately not used, and why.
````

**Moves up when** is required on a `trial` or `candidate` chapter and absent on
every other: a rating below `adopted` says what would move it, and one at or
past it has nothing to wait on. The check warns either way.

Keep chapters short. A chapter is a graph node with enough context to be
understood and judged, not an essay about AI.

## Metadata fields

`ai/` uses the common fields from
`devbook-chapter-metadata.md` (`status` and `type` required;
`related`, `issue`, `effort`, `roadmap`, `date`, and `tests` optional) plus the
folder-specific fields below.

### status

How far adoption has got **for this usage, in this project**, on the same
tech-radar ladder `tech/` uses — deliberately the same five words, so a reader
learns one adoption vocabulary and applies it in both folders:

| Value | Meaning |
|---|---|
| `candidate` | Identified as worth trying here. Nobody has actually used it. |
| `trial` | In use in a limited, reversible way — one person, one branch, one project. |
| `adopted` | The default way this part of the path is done here. |
| `hold` | Kept, but no longer expanded; avoid new usage. |
| `retired` | No longer used. Kept because knowing what we stopped doing, and why, is the most useful record in this folder. |

A repository may narrow this ladder per file in `.devbook/statuses.json`, and
never add to it — see `devbook-chapter-metadata.md`.

There is no `approved` or `accepted` rung here. The two decision rungs are
`domain/`'s alone — see `devbook-domain.md`.

**`status` is required on every `ai/` block, with no resting value to omit** —
for the same reason as `tech/`, whose ladder this is: the value is a rating, and
a usage nobody has rated is not thereby `candidate`. It is *not* the editorial
status that `domain/`, `arc42/`, and `design/` make optional.

What is on the ladder differs between the two folders: `tech/` rates a
**technology**, `ai/` rates a **way of working with one**. A tool can be
`adopted` in `tech/` and its use at a given stage still be `trial` — that pairing
is normal and is exactly what this folder exists to make visible.

Early in adoption most entries are legitimately `candidate` or `trial`. Resist
promoting a chapter to `adopted` because the tool is good; promote it when the
**Adopted by** line says the team actually works this way.

A `retired` chapter is never deleted. What was tried and abandoned is the part of
this record nobody can reconstruct later.

**`date` is the day the current rating was set.** Write it whenever `status`
moves on a chapter — when the trial started, when it was promoted, when it was
retired — and leave it alone otherwise. It is what lets the picture show when a
box got its shade, and it stays a real date rather than a modified timestamp,
per `devbook-chapter-metadata.md`.

### type

What kind of thing the chapter describes. Required on every chapter.

| Value | For |
|---|---|
| `practice` | A way of working at a stage — how work is handed over, what the human does, what the agent does. |
| `agent` | An agent persona used here, and what it is trusted with. |
| `skill` | A skill or slash command that carries part of a stage. |
| `plugin` | A plugin whose contribution to a stage is worth naming as a unit. |
| `mcp-server` | An MCP surface the path depends on at a stage. |
| `hook` | An automation that fires without anyone asking — the strongest form of adoption, and the one most worth recording. |
| `workflow` | A multi-agent or multi-step procedure. |
| `model` | A model choice that is a real decision at a stage, not the default. |
| `concept` | An idea the practices rest on — context engineering, evaluation, prompt patterns. Belongs in `concepts.md`. |
| `guardrail` | A limit or control: a review gate, a permission policy, what agents may not touch. |

File-level `type` is one of `adoption-map`, `stage` (a usage file), or
`concepts`, matching the file's role.

Prefer `practice` when in doubt. A chapter that names a tool but says nothing
about how it is used is a `tech/` chapter that wandered into the wrong folder.

### stage

The stages of the path this chapter applies at, as a list of one or more of the
seven words above: `stage: [code, verify]`. With `layer`, it is what places a
chapter on the picture, and it sits on the chapter — the file says nothing.

Every chapter carries `stage`, `layer`, or both, except a `concept` that applies
throughout, which omits both. Any other chapter with neither is off the picture
and is reported. A word outside the seven is an error: the vocabulary is fixed
so that every repository draws the same path.

Entries are **plain words, not `<path>#<heading-slug>` references** — like
`domain/`'s `aliases` and `roadmap` tags, they stay node attributes and produce
no graph edges. Never write `stage`, `substage`, `layer`, `feeds`, or `evidence`
on a file-level block: a file groups chapters and places none of them.

### substage

The sub-stages this chapter works in, as a list of `<stage>/<word>` entries:
`substage: [verify/review, verify/functional-qa]`. The word is the repository's
own, lowercase kebab-case, and not a fixed vocabulary; the stage before the
slash must be one the chapter lists in `stage`, or the entry is an error. The
model view groups every chapter naming the same entry into one card. Omit it
where a stage does not split.

### layer

The layers underneath the path this chapter works in, as a list drawn from
`thread`, `upkeep`, and `harness`: `layer: [harness]`. A word outside the three
is an error. A chapter that runs under every stage — a worktree per change, a
rule wrapped per host — carries `layer` and no `stage`; one that works at a
stage and also keeps the spec level carries both.

### feeds

The chapters this one hands work to, one entry each, as a list of
`"<path>#<anchor>: <label>"` strings:
`feeds: [".devbook/ai/02-deliver.md#code-gate: the agreed plan"]`. The part
before the colon is a reference, resolved the way `related` is, and a feed that
does not resolve fails the check; the text after it is the arrow's label and is
required. Quote each entry: the label may hold a comma. The graph build draws a
`feeds` edge carrying the label. A hand-off is `feeds`; a chapter that supports
or steers another without handing it work is `related`.

### evidence

How much the current rating rests on, one of:

| Value | Means |
|---|---|
| `recorded` | The way of working has a record: merged pull requests, runs, a retro. |
| `partial` | Some of it is on record; the rest is still to show. |
| `fired` | It ran, and nothing it produced was published or kept. |
| `none` | Nothing yet. |

It summarizes the **Evidence** bullet, which still says what the record is.
Optional; a chapter without it shows no evidence mark.

### depends-on

References to what this usage requires to work: the registered `tech/` chapter
for the tool underneath it, or another `ai/` chapter it builds on. Uses the
`<path>#<heading-slug>` reference format, and produces a graph edge.

This is the one field that crosses into `tech/`, and it crosses in one
direction only — `tech/` chapters never point back at `ai/`. A dependency that
does not resolve is a broken reference and fails the check, so register the
technology in `tech/` first.

It is also the field that puts the technology on the picture: a usage that
rests on a registered tool names it here, and the box carries the tool's name.
A `related` entry reaching into `tech/` draws nothing and is reported — use
`depends-on` for the tool underneath, and `related` for the decision or the
domain chapter beside it.

## Authoring guidance

- **One chapter per thing that could be adopted or dropped on its own.** If two
  chapters would always be promoted together, they are one chapter.
- **Put a usage at the stage where it is used**, not where the tool is
  configured. A hook that fires on commit belongs at the stage whose work it
  guards.
- **A usage that is the same at several stages lists them; one that differs per
  stage is one chapter per stage.** An idea applied throughout is a `concept`
  with no `stage`, not a chapter listing all seven; a usage that runs under
  every stage is a `layer`.
- **Keep `status` honest, and let it go down.** A demotion — `adopted` back to
  `trial`, or straight to `retired` — is the most informative edit this folder
  ever takes. A folder where nothing was ever demoted is a wish list.
- **Record what was dropped.** Set `retired` and say why in the chapter's prose
  rather than deleting it.
- **Register the tool in `tech/` first**, then link it with `depends-on`, never
  `related`. Do not restate its version, vendor, or licence here — that
  duplicates a fact with a version number attached, which is exactly the kind
  that goes stale.
- **Update `adoption-map.md`'s file table and diagram in the same change** that
  adds a chapter or moves one between stages or layers.
- **Move `date` with `status`.** The rating and the day it was set are one edit,
  and **Moves up when** goes with them: write it when a chapter enters `trial`
  or `candidate`, drop it when it reaches `adopted`.

## Reference

- `devbook-chapter-metadata.md` — required `meta` block fields.
- `devbook-tech.md` — the technology registry `ai/` links into.
- The rule for `_meta/` — delivered by the layered plugin that commits the index, where
  a repository has adopted it.
