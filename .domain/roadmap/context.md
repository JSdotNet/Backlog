# Roadmap Planning

```meta
status: draft
type: context
```

Roadmap Planning owns the forward plan — what is intended to happen, when, in
which order, and what is waiting on what — across projects rather than inside
one of them.

Inside the boundary: the plan itself, planning priority and sequence, the
dependency between two pieces of planned work, and the per-device reading pace
a plan's length is drawn from when the plan states no due date.

Outside it: execution and task status, which [Tasks](../tasks/domain.md#task)
answers, and the registered effort a plan totals but never owns, which lives on
the Tasks and [Devbook](../devbook/domain.md#knowledge-note) chapters an item
gathers. The model itself is in [domain.md](domain.md), which stays this
context's root document until the devbook contract v6 layout lands.

## Story points a week

```meta
status: draft
type: setting
key: planning-velocity.json
scope: user
default: 7
related: [.domain/roadmap/features.md#placing-a-plan-in-time]
```

How many story points the reader gets through in a week. A positive decimal,
held to four places. A week is seven calendar days to the placement: a plan's
length is its effort × 7 ÷ this, rounded up.

There are four paces and the reader picks one: the pace they **type**, and three
**measured** from the estimated work they finished over the last two, four and
eight weeks — the effort of every backlog entry completed in that stretch, today
included, divided by its weeks (2, 4 or 8). Whole calendar weeks, weekends
included, because a window is drawn in calendar days; a week of five working
days would draw every bar shorter than the stretch it was measured over took. A stretch that finished nothing estimated measured no pace and cannot be
picked; if the one picked has since gone empty, the typed pace places the plan
and the roadmap says so. Only the typed pace and the choice are stored — the
measured ones are counted afresh on every read.

It is the divisor behind one thing only: an imported plan that states no due
date gets a **length from its effort** — the story points its gathered tasks and
chapters registered, divided by this — never shorter than a day. A plan that
does state a due date is placed on it, and this figure is not consulted.

It is a **reading preference, not an estimate** (ADR 0013, ruling 4). The effort
stays the tasks' own, added and never invented; the only thing the placement
adds is how fast the reader says they work through it. A person **changing the
pace in use** — typing a new one while it is chosen, or choosing another —
re-lengthens every plan whose window is still sized by its effort: the start is
kept and the end recomputed from what its tasks gather now (ruling 5 as
amended). A window placed by its due date, or moved by hand, stays where it is.
Finished work moving a measured pace is nobody's decision and moves no bar; the
new figure is used at the next change, import, or update from tasks.

Per value:

- **Unset**, an unreadable file, a value that is not a number, or one that is
  not positive all read as seven points a week — one a day, so a reader who
  never set a pace sees the lengths they always had. A pace of zero has no
  length to give, and a default nobody chose should place a plan rather than
  refuse to.
- **A file from when the pace was a day** holds `storyPointsPerDay` and reads
  as seven times it; the next change writes `storyPointsPerWeek` in its place.
- **Zero or less** is refused on the way in, with a message beside the field,
  and nothing changes.
- **Anything finer than 0.0001** is refused rather than rounded away — four
  decimal places is what the file and the field can hold, so a finer pace would
  be written as zero and then read back as the default, silently.
- **A comma as the decimal mark** is refused rather than read as a whole number.
  Under a parser that allowed group separators, `2,5` would arrive as `25` and
  every imported plan would be a tenth of its proper length with nothing on
  screen looking wrong.

Changed on the roadmap, in its heading beside the chart it sizes, and read per placement rather than pinned at
startup, so the next plan laid out uses the pace now in force. Stored per device
beside the working week — the typed pace and the chosen `source`, a file
without one reading as the typed pace — with the same `scope: user` caveat: the choice is one
person's, nothing syncs it, and a second machine starts again at seven.
