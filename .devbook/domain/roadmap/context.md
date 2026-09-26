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

## Story points a day

```meta
status: draft
type: setting
key: planning-velocity.json
scope: user
default: 1
related: [.devbook/domain/roadmap/features.md#placing-a-plan-in-time]
```

How many story points the reader gets through in a day. A positive decimal,
held to four places.

There are four paces and the reader picks one: the pace they **type**, and three
**measured** from the estimated work they finished over the last two, four and
eight weeks — the effort of every backlog entry completed in that stretch, today
included, divided by the stretch's calendar days (14, 28 or 56). Calendar days,
weekends included, because a window is drawn in calendar days; a pace per
working day would draw every bar shorter than the stretch it was measured over
took. A stretch that finished nothing estimated measured no pace and cannot be
picked; if the one picked has since gone empty, the typed pace places the plan
and the roadmap says so. Only the typed pace and the choice are stored — the
measured ones are counted afresh on every read.

It is the divisor behind one thing only: an imported plan that states no due
date gets a **length from its effort** — the story points its gathered tasks and
chapters registered, divided by this — never shorter than a day. A plan that
does state a due date is placed on it, and this figure is not consulted.

It is a **reading preference, not an estimate** (ADR 0013, ruling 4). The effort
stays the tasks' own, added and never invented; the only thing the placement
adds is how fast the reader says they work through it. Changing the pace moves
nothing already on the roadmap, because a window is stored rather than
recomputed (ruling 5) — a re-import is what re-places a plan, and so does asking
one item still sized by its effort to
[update from its tasks](features.md#placing-a-plan-in-time).

Per value:

- **Unset**, an unreadable file, a value that is not a number, or one that is
  not positive all read as one point a day. A pace of zero has no length to
  give, and a default nobody chose should place a plan rather than refuse to.
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
person's, nothing syncs it, and a second machine starts again at one.
