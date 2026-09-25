---
name: devbook-annotations
description: The annotation fence — a note on a chapter, written in the chapter as a second fenced block, with its schema, its placement rule, its lifecycle, and the rule that keeps an open question out of task context.
---

# Annotations

A note on a chapter belongs in the chapter. Write it as a second fenced block
in the body, `annotation` where the chapter's own block says `meta`:

```annotation
author: jobsc
date: 2026-09-02
body: Is this still true now the outline rollup is a view?
```

Three lines is a complete note. A full thread adds the rest:

```annotation
kind: question
status: resolved
author: jobsc
date: 2026-09-02
quote: one indexed range read
body: |
  Does this hold after the outline gained the roadmap rollup?
  The rollup looked like it needed a second scan.
replies:
  - author: claude/flow-spec
    date: 2026-09-02
    body: No second scan — the rollup is a view over the same index.
ext:
  your-plugin:
    raised-in: 2026-09-02
```

| Field | Required | Meaning |
|---|---|---|
| `author` | yes | A person's name, or an agent/skill id. Written, never inferred — a note survives rewrites `git blame` does not follow. |
| `date` | yes | `YYYY-MM-DD`, the day the note was made. |
| `body` | yes | Markdown. A `\|` block scalar when it runs to more than a line. |
| `kind` | default `comment` | `comment` · `question` · `suggestion` · `flag`. Closed set — a reader sorts by it, so it is never free text. |
| `status` | default `open` | `open` · `resolved`. Two states, and `resolved` is short-lived. |
| `quote` | no | The phrase inside the block above that the note is about. For a person's eye and for highlight rendering — never for resolution. |
| `replies` | no | Ordered list of `{author, date, body}`. One fence is one thread. |
| `ext` | no | Namespaced extension state. Opaque here, validated only as a mapping. |

A note carries no id: a reply is inside its fence and a promoted work item
points at the chapter, so a thread is addressed as `<path>#<slug>` plus its
ordinal among the notes under that heading alone — a note under a subheading
is the subchapter's first note, never its parent's second.

## Position is the anchor

A fence annotates the block immediately above it. Placed directly after a
heading's `meta` block, it annotates the chapter as a whole. Consecutive
fences all attach to the same passage, so one passage can carry several
threads.

A fence never appears inside `meta`, never before the first heading, and never
in a `_meta/` file.

Everything else follows from position. Reword the passage and the note stays
attached; move the passage with its note and it stays attached; move it
without the note and `quote` is the tell — `--check` reports a quote matching
nothing above as a warning, and a person decides. Delete the passage and
delete the note in the same change; leaving it behind is the mistake. Rename
the file and there is nothing to update.

What this gives up is sub-block precision: a note on the third sentence of a
paragraph is a note on the paragraph, with `quote` carrying the sentence.

## Resolving a note means deleting it

A note in a file is an open loop — not a record, not an audit trail, and not
commentary meant to be read a year later. git holds all of that, in the commit
that removed it and the change that answered it.

| State | Means | Lives for |
|---|---|---|
| `open` | Someone is waiting on this. | Until it is answered. |
| `resolved` | Answered, and the answer is in the fence. | The rest of the branch, so a reviewer sees the exchange in the pull request that raised it. |
| gone | Swept. | Forever. The prose change is the record. |

Sweeping is a step, not a hope: delete every resolved fence before the branch
merges. `devbook:annotation-sweep` is that step, over `annotations.mjs sweep`
— chapter-scoped, so a person sees what is about to go. A permanent note is a
smell: if an aside is worth keeping, it is prose, and it belongs in the chapter
as prose.

## An annotation is not chapter content

Notes live in the canonical file, so anything loading a chapter for task
context will read reviewer chatter as settled content unless it is told
not to. A question about whether a rule still holds, ingested as context,
becomes the rule. This is the one failure mode the convention did not have
before, so the discipline is explicit, the same way `_meta/` has one:

- A reader loading a chapter **for context skips every annotation fence**, and
  the `review`, `reviewer`, and `review-at` fields beside it — a chapter someone
  has queried has not thereby stopped saying what it says.
- A reader working **in review mode** — a review skill, a queue, the approval
  gate — reads them, and reads nothing else in the chapter as instruction.
- `capture-specs` never writes one. `apply-change` never carries one into
  a change brief. An open question is a reason to stop at the gate, not a line
  item to implement.

## An open question means the chapter is not agreed

The scope is the `question` kind, open, and nothing wider. A `comment`, a
`suggestion`, or a `flag` is somebody's remark about a chapter that stands; an
unanswered question is a hole in the chapter itself, so it outranks whatever
`status` says.

`--check` does not report one. An open question is the state the fence exists
for and it is legal for the length of a branch, so a gate that failed on every
one would teach everybody to ignore it. What `--check` does report, as an
error, is the contradiction: a chapter on the `approved` rung carrying an open
question, where a person has signed for content that still has an unanswered
question in it. Resolve and sweep the note, or take the approval off.

## Writing one

Every write goes through `.devbook/_tools/devbook-meta/annotations.mjs` — `list`,
`add`, `reply`, `resolve`. Nothing else writes a fence with a regular expression of
its own. Adding a note changes a tracked file: say so, offer the
commit, never push, and never auto-commit into someone's branch.
