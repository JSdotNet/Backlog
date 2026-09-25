---
name: devbook-ai
description: Structure and authoring rules for the AI devbook folder, recording how this project develops with AI — which practice, agent, skill, or model is applied at which stage of the DevOps loop, the concepts underneath them, and how far adoption has actually got.
---

# AI adoption (`ai/`)

`ai/` is the durable record of **how this project develops with AI** — which AI
capability is applied at which stage of the DevOps loop, what concepts it rests
on, and how far adoption has actually got.

It is organized by the **loop**, not by the tool. The question `ai/` answers is
"at this stage of how we work, what do we use AI for, and is that real yet?".
The question it does not answer is "what is Claude Code and which version are we
on" — that is `tech/`. Every chapter says which stages it applies at in its
metadata, so a tool can draw the answer as the loop — see
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

`ai/` never re-registers it. A `ai/` chapter names the **usage**: the practice,
the agent persona, the skill, the guardrail — the thing that exists only because
of how we chose to work — and points at the registered technology with
`depends-on`.

| Fact | Folder |
|---|---|
| "We use Claude Code, version X, `adopted`" | `tech/` |
| "At `plan` we draft devbook chapters with the devbook skills, `trial`" | `ai/` |
| "The coding agent persona, and when we hand work to it" | `ai/` |
| "Anthropic's API is a service dependency of the build pipeline" | `tech/` |
| "Every agent-authored change is reviewed by a human before merge" | `ai/` |

The test: **if it has a vendor and a version, it is a `tech/` chapter.** If it is
a decision about how we work, it is a `ai/` chapter. A `ai/` chapter whose
`depends-on` points at nothing in `tech/` is usually fine — most practices,
concepts, and guardrails have no product behind them.

## Context-loading policy

- `ai/` is **not** baseline repository context. Load it when the task is about
  how the team works with AI — adopting a tool into the loop, changing a
  practice, reviewing adoption, onboarding someone into the way of working.
- When `ai/` is needed as task context, load `adoption-map.md` plus only the
  files in scope, not the whole folder.
- An agent does **not** read `ai/` to decide how to do its own current task.
  These chapters are the record of a way of working, not instructions to follow;
  instructions live in instruction files and skills.

## The stages

The stages are the DevOps loop's own eight, in this order, and not the
repository's: `plan`, `code`, `build`, `test` on the dev half, `release`,
`deploy`, `operate`, `monitor` on the ops half, and `monitor` feeds `plan`. A
fixed vocabulary is what lets a tool draw one loop for every repository and a
reader compare two; a stage the flow does not use stays empty rather than being
dropped, and the emptiness is the finding. A chapter says where it sits with
`stage`, on its own block — never through the file it is in.

## Structure

```
.devbook/ai/
  adoption-map.md       # root: how the loop reads here, the adoption diagram
  01-<part>.md          # usage files: chapters grouped by the part of the flow they cover
  02-<part>.md
  …
  concepts.md           # the ideas the practices rest on
  _meta/graph.json      # derived: generated reference graph, never hand-edited
  _meta/index.json      # derived: generated reading outline, never hand-edited
```

**A file groups chapters; it places none.** Split usage files by whatever reads
best — one per stage of the loop, one per part of the repository's own flow, one
per theme — and number them so the folder reads in the order the work happens.
Reading order needs no declaration: `adoption-map.md` is the root document and
sorts first, the numbered files follow, and `concepts.md` sorts after them. See
`devbook-chapter-metadata.md`. A chapter's position on the loop is its `stage`
field, whichever file it is in, so moving a chapter between files changes
nothing in the picture.

## File responsibilities

- **adoption-map.md** — Root strategic view of AI across the loop.
  - Lists the usage files and what part of the flow each covers.
  - Renders the adoption picture as a Mermaid diagram: the eight stages in loop
    order, with the chapters that sit at each one. It is the hand-drawn form of
    [the loop picture](#the-loop-picture), read from the same fields, so the
    two never disagree.
  - Explains the status ladder and how to read and extend the folder.
  - Its `##` sections do **not** carry per-chapter metadata blocks; the file
    carries a file-level block only — the same rule as `.devbook/domain/context-map.md`
    and `.devbook/tech/technology-graph.md`.
- **`<nn>-<part>.md`** — One `## <Chapter Name>` chapter per usage. Each chapter
  is an addressable node in the graph, carries a chapter metadata block, and
  says its stages with `stage`.
- **concepts.md** — The ideas the practices rest on. A concept carries `stage`
  where it applies at particular stages and omits it when it applies throughout.
- **`_meta/*.json`** — Derived, generated indexes for this folder. Never
  hand-edited; see `devbook-derived-artifacts.md`.

## The loop picture

`ai/` is authored so a tool can draw it as the DevOps loop: the eight stages
around the loop, at each stage the AI usages as boxes, each box shaded by its
rating and carrying the name of the technology it rests on. Every element of the
picture is one field on the chapter, and nothing is authored twice.

| In the picture | Read from |
|---|---|
| The eight stages, in order around the loop, four per half | the fixed vocabulary above — authored nowhere |
| A box at a stage | a chapter whose `stage` lists it, shaded by its `status`, with `type` as its glyph |
| A box at several stages | the same chapter, listing several |
| A box in the middle of the loop | a `concept` chapter with no `stage` |
| The technology named on the box | the `tech/` chapter its `depends-on` points at — never `related` |
| The stage's own shading | the highest rung among the boxes at it — derived, authored nowhere |
| When the box got its shade | the chapter's `date` |

A stage with no box renders empty. That is the picture doing its job, so never
fill a stage with a chapter that names a tool and no usage. Nothing else is in
the picture: the tools a stage runs on that no AI usage rests on are `tech/`'s,
and `tech/` is not organized by stage.

## Chapter template

````markdown
## <Chapter Name>

```meta
status: trial
type: practice
stage: [code, test]
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
- **Limits** — where it is deliberately not used, and why.
````

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
| `adopted` | The default way this part of the loop is done here. |
| `hold` | Kept, but no longer expanded; avoid new usage. |
| `retired` | No longer used. Kept because knowing what we stopped doing, and why, is the most useful record in this folder. |

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
| `mcp-server` | An MCP surface the loop depends on at a stage. |
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

The stages of the loop this chapter applies at, as a list of one or more of the
eight words above: `stage: [code, test]`. It is the one field that places a
chapter on the picture, and it sits on the chapter — the file says nothing.

Every chapter carries it except a `concept` that applies throughout, which
omits it and is drawn in the middle of the loop. Any other chapter without it is
off the loop and is reported. A word outside the eight is an error: the
vocabulary is fixed so that every repository draws the same loop.

Entries are **plain words, not `<path>#<heading-slug>` references** — like
`domain/`'s `aliases` and `roadmap` tags, they stay node attributes and produce
no graph edges. Never write it on a file-level block: a file groups chapters and
places none of them.

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
  with no `stage`, not a chapter listing all eight.
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
  adds a chapter or moves one between stages.
- **Move `date` with `status`.** The rating and the day it was set are one edit.

## Reference

- `devbook-chapter-metadata.md` — required `meta` block fields.
- `devbook-tech.md` — the technology registry `ai/` links into.
- `devbook-derived-artifacts.md` (a layered plugin's rule) — rules for `_meta/`.
