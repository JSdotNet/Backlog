# Roadmap Planning

```meta
status: draft
type: context
deployment: module
related: [.devbook/arc42/05-building-block-view.md#desktop-app]
```

Roadmap Planning owns the forward plan across projects rather than inside one of
them: what is intended to happen, when, in which order, and what is waiting on
what.

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
the Tasks and [Devbook](../devbook/domain.md#shared-value-objects) chapters an item
gathers. The model itself is in [domain.md](domain.md).

## Roadmap view

```meta
status: draft
type: feature-flag
key: roadmap
default: on
related: [".devbook/domain/roadmap/features.md#reading-and-rescheduling-on-a-timeline", ".devbook/arc42/adr/0022-the-shell-shows-one-main-view-picked-by-a-view-switch.md#5-roadmap-is-no-longer-a-takeover"]
```

Turning it on offers the Roadmap in the Home shell's view switch, beside Tasks.
The plan is drawn on a graduated axis as the main view (local ADR 0022).
Switching it off withdraws the roadmap everywhere else too, behind this key and
no other:

- the dashboard's plan-progress part reads nothing;
- the calendar shows no roadmap plans and refuses to place a plan from its shelf;
- the `get_roadmap` tool leaves the MCP endpoint.

A view remembered as the Roadmap opens on Tasks while the flag is off. The flag
is on by default and released, so the settings screen shows it with no `DEV`
mark. The key is `RoadmapFeatures.Roadmap` in the Roadmap module, so a reader
below the shell can ask whether the roadmap is on without referencing the UI.
The settings screen lists it as "Roadmap view" from the feature catalog in
`AppFeatures.cs`. A person's choice is kept in `features.json` beside the app's
other per-device choices. The flag stays as the way to keep the plan out of view,
and is not retired.

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

It is the divisor behind one thing only: an imported plan that states no due
date gets a **length from its effort**. Each of its repository
[parts](domain.md#part) is as long as the story points registered by the work
filed there, divided by that repository's figure, and
[never shorter than a day](requirements.md#requirement-a-plan-is-never-shorter-than-its-start-day).
A plan that does state a due date
[ends on it](requirements.md#requirement-a-due-date-ends-the-plan-whatever-its-effort),
and this figure is not consulted.

It is a **reading preference, not an estimate** (ADR 0013, ruling 4). The effort
stays the tasks' own, added and never invented. The only thing the placement adds
is how fast the reader says they work through it. A plan whose window is still
sized by its effort [keeps up with its work](features.md#placing-a-plan-in-time)
at the pace in use, so changing the pace re-lays those bars. A measured pace that
moves as work is finished moves them too, at the next load.

**Four paces.** The reader picks one of them:

| Pace | How it is counted | Stored |
|---|---|---|
| Typed | The figure the reader types | Yes |
| Measured over two weeks | The effort of every backlog entry completed in the measuring period, divided by the period's working hours, expressed per working week | No, counted afresh on every read |
| Measured over four weeks | The same, over four weeks | No |
| Measured over eight weeks | The same, over eight weeks | No |

A **measuring period** is whole calendar weeks, today included. Its hours include
the [day overrides](domain.md#day-override) in it, while a working week stays the
weekly pattern's total
([requirement](requirements.md#requirement-a-measured-pace-counts-the-hours-in-its-stretch)).
A measuring period with no overrides therefore measures the points divided by its
weeks (2, 4 or 8), and a blocked day raises the figure. A measuring period that
finished nothing estimated measured no pace and cannot be picked. Besides the
typed pace, only the choice is stored.

**Kept per repository band**, because productivity differs from one project to
the next, with one **global** pace beside them (ADR 0013, ruling 4 as amended on
2026-09-26 and on 2026-10-07). Each part of a plan is placed at the pace in use
for its own repository:

| The part is filed under | Pace in use |
|---|---|
| A repository with its own pace | That repository's typed pace or chosen measured pace, counted over the entries filed under it |
| A repository nobody has set a pace for | The global typed pace and choice, measured over that repository's own entries |
| No repository, or a repository nobody configured | The global pace, measured over every finished entry once |
| Any of these, when the chosen measuring period has since gone empty | The typed pace of the same entry, and the roadmap says so |

A plan filed under several repositories therefore draws a bar in each of their
[bands](domain.md#band), and its own window ends on the latest of those bars
([Placing a plan in time](features.md#placing-a-plan-in-time)). An entry filed
under two repositories counts in full toward both. A repository keeps no pace of
its own until the reader sets one, so nobody sees a bar move on upgrading. The
first change made for a repository gives it its own entry. That entry copies the
half not being changed, the typed pace or the choice, from what the repository
read at that moment. Repositories are named by the alias Settings gives them,
compared without regard to case.

Per value:

- **A pace that cannot be read**
  [reads as seven points a working week](requirements.md#requirement-a-pace-that-cannot-be-read-is-seven-points-a-week).
  On the default week that is about 1.4 points a worked day. A pace of zero has
  no length to give, and a default nobody chose should place a plan rather than
  refuse to.
- **A file from when the pace was a day** holds `storyPointsPerDay` and reads
  as seven times it; the next change writes `storyPointsPerWeek` in its place.
- **A typed pace the roadmap cannot store as typed**
  [is refused](requirements.md#requirement-a-typed-pace-the-roadmap-cannot-store-as-typed-is-refused).
  Four decimal places is what the file and the field can hold, so a finer pace
  would be written as zero and then read back as the default, silently. Under a
  parser that allowed group separators, `2,5` would arrive as `25`, and every
  imported plan would then be a tenth of its proper length with nothing on screen
  looking wrong.

Where the pace is changed and shown:

- Each pace is changed on the roadmap, beside the chart it sizes. The typed pace,
  **Mine**, is the one points field ("pt/wk"), in the chart's heading.
- Each band carries its pace under its name in the chart's sidebar, above its
  named lanes, in the same two rows whether it measured anything or not. Releasing
  its slider sets the band's own pace. A **Measured** button returns the band to
  its measured pace, and an info mark lists what was finished a week over the
  last two, four and eight weeks and the pace placing the band.
- A repository's own pace sits in its band. The global pace, the **default**,
  sits in the band for work under no repository and is measured over all finished
  work. That band is drawn whenever the chart is, even with nothing filed in it,
  so the default is always offered for editing.
- Every repository band with no pace of its own shows the default until edited.
- A measuring period that finished nothing says so in the info mark, and the next
  period that measured something places the band.
- A band that measured nothing in any period has no **Measured** button. A line
  under its slider says that Mine is used, and the slider runs from half the
  typed pace to double it.
- The pace is read per placement rather than pinned at startup, so the next plan
  laid out uses the pace now in force.

How the pace is stored and travels:

- The device stores it in `planning-velocity.json`: the typed pace, the chosen
  `source`, and an optional `repositories` object holding each repository's own
  pair. A file without a `source` reads as the typed pace. A file without
  `repositories` reads as it always did.
- The choice is one person's, so it follows that person. With sync on, the typed
  pace, the chosen source and every repository's own pace travel between their
  paired devices. A second machine reads the pace set on the first instead of
  starting again at seven
  ([ADR 0018](../../arc42/adr/0018-roadmap-plan-and-pace-ride-the-task-feed.md);
  see [Syncing the plan between devices](features.md#syncing-the-plan-between-devices)).
- The working week travels with the pace, as a `workingWeek` key in the same file
  and the same synced document
  ([ADR 0019](../../arc42/adr/0019-roadmap-counts-the-working-week.md)). The key
  holds the weekly pattern and an `overrides` list of `{date, worked}` pairs, one
  per [day override](domain.md#day-override). A file without the key reads the
  device's own [working week](../productivity/context.md#working-week), or the
  default week, and writes the key on its next change. How the overrides travel
  is in [the requirements](requirements.md#carrying-the-pace-with-the-person).
