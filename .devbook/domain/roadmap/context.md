# Roadmap Planning

```meta
status: draft
type: context
deployment: module
related: [.devbook/arc42/05-building-block-view.md#desktop-app]
```

Roadmap Planning owns the forward plan — what is intended to happen, when, in
which order, and what is waiting on what — across projects rather than inside
one of them.

Inside the boundary: the plan itself, planning priority and sequence, the
dependency between two pieces of planned work, and the reading pace a plan's
length is drawn from when the plan states no due date. Both follow their owner
across the devices they have paired
([ADR 0018](../../arc42/adr/0018-roadmap-plan-and-pace-ride-the-task-feed.md)).
The pace is counted over the owner's working week, which travels with it
([ADR 0019](../../arc42/adr/0019-roadmap-counts-the-working-week.md)). Its
weekly pattern is set in [Productivity](../productivity/context.md#working-week);
its [day overrides](domain.md#day-override) are set on the roadmap's axis.

Outside it: execution and task status, which [Tasks](../tasks/domain.md#task)
answers, and the registered effort a plan totals but never owns, which lives on
the Tasks and [Devbook](../devbook/domain.md#knowledge-note) chapters an item
gathers. The model itself is in [domain.md](domain.md).

## Story points a week

```meta
status: draft
type: setting
key: planning-velocity.json
scope: user
default: 7
related: [.devbook/domain/roadmap/features.md#placing-a-plan-in-time, .devbook/domain/roadmap/features.md#syncing-the-plan-between-devices, .devbook/arc42/adr/0013-imported-plan-is-a-roadmap-item-laid-out-by-import.md#4-the-importer-places-the-window-velocity-is-the-readers, .devbook/arc42/adr/0018-roadmap-plan-and-pace-ride-the-task-feed.md, .devbook/arc42/adr/0019-roadmap-counts-the-working-week.md, .devbook/domain/productivity/context.md#working-week]
```

How many story points the reader gets through in a working week. A positive
decimal, held to four places. A week is the hours of the reader's
[working week](domain.md#working-week), 42.5 on the default week. A plan needs
its effort × those hours ÷ this pace, counted forward over the hours the reader
works (ADR 0019, proposed).

**Kept per repository band**, because productivity differs from one project to
the next, with one **global** pace beside them (ADR 0013, ruling 4 as amended on
2026-09-26 and on 2026-10-07). A plan is placed one repository part at a time. Each
part is placed at the pace in use for its own repository. A part under no
repository, or under a repository nobody configured, is placed at the global pace.
A plan filed under several repositories therefore draws a bar in each of their
bands, and its own window ends on the latest of those bars
([Placing a plan in time](features.md#placing-a-plan-in-time)). A repository keeps no pace of its own
until the reader sets one: until then it reads the global typed pace and choice,
so nobody sees a bar move on upgrading. The first change made for a repository
gives it its own entry, copying the half not being changed — the typed pace, or
the choice — from what it read at that moment. Repositories are named by the
alias Settings gives them, compared without regard to case.

There are four paces and the reader picks one: the pace they **type**, and three
**measured** from the estimated work they finished over the last two, four and
eight weeks — the effort of every backlog entry completed in that stretch, today
included, divided by the working hours in that stretch and expressed per working
week. Each stretch is whole calendar weeks. The hours include the
[day overrides](domain.md#day-override) in it, while a working week stays the
weekly pattern's total
([requirement](requirements.md#requirement-a-measured-pace-counts-the-hours-in-its-stretch)).
So a stretch with no overrides measures the points divided by its weeks (2, 4 or
8), and a blocked day raises the figure. A stretch that finished nothing estimated measured no pace and cannot be
picked; if the one picked has since gone empty, the typed pace places the plan
and the roadmap says so. Only the typed pace and the choice are stored — the
measured ones are counted afresh on every read. A repository's measured paces
count only the entries filed under it, and an entry filed under two counts in
full toward both; the global ones count every finished entry once.

It is the divisor behind one thing only: an imported plan that states no due
date gets a **length from its effort**. Each of its repository parts is as long as
the story points registered by the work filed there, divided by that repository's
figure, and never shorter than a day. A plan that
does state a due date is placed on it, and this figure is not consulted.

It is a **reading preference, not an estimate** (ADR 0013, ruling 4). The effort
stays the tasks' own, added and never invented; the only thing the placement
adds is how fast the reader says they work through it. Every plan whose window
is still sized by its effort is laid out from the work it has **not done yet**,
each repository part at the pace in use for its own repository, from today. This happens each time the
roadmap loads, each time a task changes, and when a person changes the pace in
use (ruling 5 as amended on 2026-09-27). A measured pace that moves as work is
finished therefore moves those bars too, at the next load. A window placed by its
due date, or moved by hand, stays where it is — see
[Placing a plan in time](features.md#placing-a-plan-in-time).

Per value:

- **Unset**, an unreadable file, a value that is not a number, or one that is
  not positive all read as seven points a working week. Before ADR 0019 that was
  one point a calendar day; on the default week it is now about 1.4 points a
  worked day, so a default bar is longer than it was. A pace of zero has no
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

Changed on the roadmap, beside the chart it sizes — each pace under its band's
name in the chart's sidebar and above the band's named lanes, as a points field
("pt/wk") over the choices, each measured one showing its figure, the band growing
to fit them: a repository's own pace in its band, and the global pace, the
**default**, in the band for work under no repository, measured over all finished
work. That band is drawn whenever the chart is, even with nothing filed in it, so
the default is always offered for editing. Every repository band with no pace of
its own shows the default until edited. A stretch that counted nothing is not offered at all, and with none
measured only the field is shown; a chosen stretch that has since emptied says in
a line under the choices that the typed pace is used — and read per placement
rather than pinned at startup, so the next plan laid out uses the pace now in force. Stored on
the device in `planning-velocity.json`: the typed pace and the chosen `source`, a file
without one reading as the typed pace, and an optional `repositories` object
holding each repository's own pair, a file without it reading as it always did.
The choice is one person's, so it follows that person: with sync on, the typed
pace, the chosen source and every repository's own pace travel between their
paired devices, and a second machine reads the pace set on the first instead of
starting again at seven
([ADR 0018](../../arc42/adr/0018-roadmap-plan-and-pace-ride-the-task-feed.md);
see [Syncing the plan between devices](features.md#syncing-the-plan-between-devices)).

The working week travels with the pace, as a `workingWeek` key in the same file
and the same synced document
([ADR 0019](../../arc42/adr/0019-roadmap-counts-the-working-week.md)). The key
holds the weekly pattern and an `overrides` list of `{date, worked}` pairs, one
per [day override](domain.md#day-override). A file without the key reads the
device's own [working week](../productivity/context.md#working-week), or the
default week, and writes the key on its next change. How the overrides travel is
in [the requirements](requirements.md#carrying-the-pace-with-the-person).
