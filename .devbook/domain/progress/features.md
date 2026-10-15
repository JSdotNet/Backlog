# Progress

```meta
type: features
status: draft
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

Every feature here sits behind the [Progress area](context.md#progress-area)
flag.

## Setting up a sprint series

```meta
type: feature
status: draft
feature-flag: .devbook/domain/progress/context.md#progress-area
related: [.devbook/domain/progress/domain.md#sprint-series, .devbook/domain/progress/domain.md#series-assignment, .devbook/domain/progress/requirements.md#setting-up-a-sprint-series]
```

Give a set of repositories one cadence to work to. A person creates a series,
gives it its repositories and chooses how its sprints run. A repository belongs
to at most one series. A repository in no series keeps the
[In progress](../sessions/features.md#in-progress-by-task) view it has today.

### Choosing the cadence

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/domain.md#sprint-length, .devbook/domain/progress/domain.md#sprint-window]
```

Choose a sprint length of one or two weeks, and the weekday sprints start on.
Every sprint of the series runs from that weekday for that many days. A changed
length or weekday applies from the next sprint.

### Naming the sprints

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/domain.md#name-pattern]
```

Choose the pattern a sprint's name is made from, with `{n}` for its number. The
default `Sprint {n}` names the fourth sprint "Sprint 4".

### Choosing which briefs show the series

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/domain.md#brief-visibility, .devbook/domain/progress/features.md#sprints-in-the-briefs]
```

Choose whether the series' open sprint shows in the daily brief, and whether it
shows in the weekly brief.

### Switching a series off

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/domain.md#series-switch, .devbook/domain/progress/requirements.md#requirement-a-switched-off-series-repositories-keep-the-in-progress-view]
```

Switch one series off without losing it. Its repositories show the In progress
view again, and it keeps everything it holds until it is switched back on. A
repository of a switched-off series still cannot join another series.

### Deleting a series

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/domain.md#sprint-series, .devbook/domain/progress/requirements.md#requirement-a-deleted-series-frees-its-repositories]
```

Delete a series made by mistake, or one no longer worked to. Any series can be
deleted, on or off, with or without sprints. "Delete series…" on its card asks
first, naming the series, how many sprints go with it and which repositories
it frees. Cancel changes nothing. Once confirmed, the series and every sprint it
recorded are gone, and its repositories show under "Not in a sprint", free to
join another series. No task changes, and the series is deleted on every paired
desktop. Nothing offers to bring it back: to keep its sprints, switch it off
instead.

### The Sprints settings page

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/context.md#progress-area]
```

Settings › Sprints is where a series is set up, offered while the Progress area is
on. It draws one card per series: its switch, its repositories, its length, start
weekday and name pattern with the next sprint's name, its goal rules and when the
goal is met, which briefs show it, and "Delete series…". "Add repository" offers only the
repositories in no series. A "Not in a sprint" card lists those repositories, and
"Add another sprint" starts a series over the one picked. Every change is saved as
it is made, and a refused change is said on its card in the module's words. Done
is shown as the rule it is, not a choice: a task's status Done, or every linked pull
request merged.

## Filling a sprint

```meta
type: feature
status: draft
feature-flag: .devbook/domain/progress/context.md#progress-area
related: [.devbook/domain/progress/domain.md#sprint, .devbook/domain/progress/requirements.md#filling-a-sprint]
```

Say what a sprint holds. Its membership is Progress's own: adding a task to a
sprint, or linking a plan, changes nothing on the task.

### Adding a task

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/domain.md#sprint-item]
```

Add one task to the open sprint by its id. Any task in the series' repositories
can be added, whatever its status. Removing it again takes it out of the sprint
and leaves the task as it was.

### Linking a plan

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/domain.md#plan-link, .devbook/domain/roadmap/domain.md#roadmap-tag]
```

Link a whole plan to the sprint by its plan tag. The plan brings in every task
filed under that tag in the series' repositories, finished or not. A task filed
under the plan later joins the sprint by itself. Unlinking the plan takes out the
tasks it brought in, except those added directly.

### Planning from the sprint view

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/features.md#the-sprint-view, .devbook/domain/progress/features.md#keeping-a-note-on-an-item]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.SprintPlanningTests.Add_to_sprint_adds_a_task_through_the_module_and_the_sprint_is_read_again, unit:dotnet:Backlog.Desktop.UI.UnitTests.SprintPlanningTests.Add_to_sprint_links_a_whole_plan, unit:dotnet:Backlog.Desktop.UI.UnitTests.SprintPlanningTests.The_row_menu_marks_goal_and_removes_through_the_module, unit:dotnet:Backlog.Desktop.UI.UnitTests.SprintPlanningTests.The_note_dialog_writes_the_note_and_the_open_question, unit:dotnet:Backlog.Desktop.UI.UnitTests.SprintPlanningTests.A_closed_sprint_offers_no_add_and_no_row_menu]
```

While the sprint view shows the open sprint, it plans it. **Add to sprint…** beside
Refresh opens a picker over the series' repositories: plans first, then tasks, narrowed
to one repository and by a search. Choosing a plan links it; choosing a task adds it.
What the sprint already holds is listed but not offered again. The picker stays open
for the next choice and says what the last one did.

Each row carries a menu: mark the task as goal or take the mark off, add or edit its
note, mark or clear its open question, and take it out — **Remove from sprint** for a
task added directly, **Unlink +plan** for one a linked plan brought in. A closed sprint
shows no menu and no Add. Every act is a write through Progress, and the sprint is read
again once it lands; a refusal shows in Progress's words.

### Planning from a task

```meta
type: sub-feature
status: draft
related: [.devbook/arc42/adr/0028-progress-is-its-own-boundary-and-tasks-knows-no-sprints.md, .devbook/domain/sessions/features.md#in-progress-by-task]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.SprintPlanningTests.A_tasks_sprint_section_adds_it_marks_it_and_takes_it_out_through_the_module, unit:dotnet:Backlog.Desktop.UI.UnitTests.SprintPlanningTests.A_tasks_sprint_section_links_its_plan_and_writes_its_note, unit:dotnet:Backlog.Desktop.UI.UnitTests.SprintPlanningTests.With_progress_off_or_the_task_in_no_series_the_section_draws_nothing]
```

A task's side panel carries a **Sprint** section while its repository works to a series
switched on. It says how the task stands in the open sprint — not in it, added directly,
brought in by its plan, or both — and offers what applies: add the task or remove it,
add its plan or unlink it, and, for a member, the goal mark and the note with its open
question. The desktop shell composes the section from Progress beside **Sessions and
pull requests**; the task gains no sprint field.

The writes go to the open sprint. The next sprint starts only when the open one closes,
so there is no later sprint to plan ahead into.

## Setting the sprint goal

```meta
type: feature
status: draft
feature-flag: .devbook/domain/progress/context.md#progress-area
related: [.devbook/domain/progress/domain.md#goal-rule, .devbook/domain/progress/domain.md#goal-threshold, .devbook/domain/progress/requirements.md#setting-the-sprint-goal]
```

Say what the sprint sets out to do, and how to tell it did. A sprint carries a
goal in words. Its series picks the goal tasks by rules and says when the goal
is met.

### Choosing the goal rules

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/domain.md#goal-rule-kind]
```

Choose any combination of rules for the series:

- the tasks in chosen plans;
- the tasks marked as goal;
- the tasks with chosen tags;
- every task in the sprint.

A task picked by more than one rule counts once.

### Marking a task as goal

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/domain.md#sprint-item]
```

Mark one sprint item as a goal task. The mark counts while the series has the
"marked as goal" rule.

### Choosing when the goal is met

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/domain.md#goal-threshold, .devbook/domain/progress/domain.md#done]
```

Choose whether the goal is met when every goal task is done, or when a set share
of them is, such as 80%. A task is done when its status is Done, or when every
pull request linked to it is merged.

## Following a sprint

```meta
type: feature
status: draft
feature-flag: .devbook/domain/progress/context.md#progress-area
related: [.devbook/domain/progress/domain.md#sprint-progress, .devbook/domain/progress/requirements.md#following-a-sprint]
```

See the open sprint of the series in scope: its name, its dates, its goal, how
many goal tasks are done, and whether the goal is met. Each member shows as a row
with its task, whether it is done, and its owner. The owner is the author of the
task's linked pull requests. A task with no linked pull request shows no owner.

### The sprint view

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/requirements.md#requirement-a-repository-in-no-series-keeps-the-in-progress-view, .devbook/domain/sessions/features.md#in-progress-by-task]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.SprintViewTests.With_progress_off_the_now_view_is_drawn_with_no_switch, unit:dotnet:Backlog.Desktop.UI.UnitTests.SprintViewTests.A_repository_in_no_series_keeps_the_now_view_unchanged, unit:dotnet:Backlog.Desktop.UI.UnitTests.SprintViewTests.A_switched_off_series_reads_as_no_series, unit:dotnet:Backlog.Desktop.UI.UnitTests.SprintViewTests.Choosing_the_sprint_draws_the_sprint_view_and_choosing_now_brings_the_now_view_back, unit:dotnet:Backlog.Desktop.UI.UnitTests.SprintViewTests.Pull_request_chips_are_coloured_by_state_and_the_owner_is_their_author, unit:dotnet:Backlog.Desktop.UI.UnitTests.SprintViewTests.The_pace_reading_names_the_open_goal_tasks_the_blocked_ones_and_the_gap_to_the_even_pace]
```

The [In progress](../sessions/features.md#in-progress-by-task) tab shows the sprint.
While the header's repository scope holds a repository of a series that is switched on
and has a sprint, a **Now | Sprint n** switch sits beside the title. Now is the view by
task, unchanged. With no such repository in scope, or Progress off, there is no switch.
A scope with repositories of several series shows the first series in scope order.

The sprint side opens on the open sprint and steps to the previous and next one. It
shows:

- the series' name, how many repositories it has and the sprint's dates;
- a filter to one of the series' repositories, and grouping by plan or none;
- the goal card: the goal in words, the goal tasks done of their total with a bar by
  status, the goal rules that counted them, then all the sprint's work done of its
  total, how much is outside the goal and how much was added after the start;
- the done line: members done by the end of each working day against the even pace —
  every member done by the last working day — with one sentence on where the sprint
  stands;
- **Needs you**: the members that are blocked, wait on review with an open pull
  request, or carry an open question;
- one group per plan, a goal plan's group marked "Goal plan", and a row per member:
  its status, its title with "Goal" or "added" where they apply, a chip per linked pull
  request coloured by its state (merged green, open amber, draft grey), its owner and
  its note;
- folded, "Outside the sprint": the live sessions and open pull requests on work the
  sprint does not hold.

The sprint is read when the side opens, on a step and on Refresh.

### Copying a sprint as a table

```meta
type: sub-feature
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.SprintTableClipboardTests]
```

**Copy as table** in the sprint view's header puts the sprint on the clipboard as a
table that pastes formatted into PowerPoint and Outlook. It has one row per item,
grouped by plan in the board's order: goal plans first, then the other plans by name,
then the items in no plan under "No plan". The plan is named on its group's first row.

| Column | Holds |
|---|---|
| Plan | The plan tag, on the group's first row |
| Item | The task's title |
| Status | Done, In progress, In review, Blocked or Not started, in its colour |
| Pull requests | Each linked pull request's number, linked to GitHub, with "open", "draft" or "closed" when it is not merged |
| Owner | The authors of the linked pull requests |
| Note | The sprint's note on the item, "Open question:" in front when it is one |

The table holds every item, whatever the repository filter shows. The clipboard
carries it twice: as HTML with every style inline, which PowerPoint and Outlook keep,
and as tab-separated plain text for anything that takes no HTML. The status colours
are fixed values for a white page, because a pasted table never sees the app's theme.
The button is unavailable while the sprint is being read.

The daily strip's **Copy for the stand-up** copies the same table.

### The daily strip

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/domain.md#brief-visibility, .devbook/domain/progress/domain.md#sprint-progress, .devbook/domain/progress/requirements.md#requirement-the-daily-strip-shows-only-for-the-open-sprint-of-a-series-in-the-daily]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.SprintDailyTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.SprintViewTests.A_series_shown_in_the_daily_draws_the_since_yesterday_strip_above_the_goal_card_and_it_folds, unit:dotnet:Backlog.Desktop.UI.UnitTests.SprintViewTests.Copy_for_the_stand_up_puts_the_strip_and_the_sprint_table_on_the_clipboard, unit:dotnet:Backlog.Desktop.UI.UnitTests.SprintViewTests.A_series_not_shown_in_the_daily_has_no_strip]
```

A collapsible **Since yesterday** strip sits at the top of the sprint view, under the
header and above the goal card, while the open sprint is shown and its series has
"Show it in the daily" on. Its header holds a label — the day, the sprint and its
working day — and one headline sentence on what landed and what is stuck, and folds
the rest away. Open, it shows:

- a summary: the series, the goal tasks done of their total, all the work done of its
  total and the working days left;
- **Since yesterday**: what was done, opened for review, started and added to the
  sprint since the previous working day — Friday on a Monday — up to today, with the
  pull requests merged or opened; "Nothing moved" when nothing did;
- **On today**: each member in progress, then each unfinished member waiting on review
  with an open pull request;
- **Blocked and open questions**: each blocked member with the day it was marked, and
  each member carrying an open question in its note's words;
- **Pace**: how many goal tasks are open, the blocked among them, and whether the goal
  is at risk; then all the work against the even pace and about how much lands by the
  last working day at the pace so far.

The goal is at risk while it is not met and a goal task is blocked, the last working
day has come, or the goal tasks still needed outnumber what the pace so far lands in
the working days left. Every sentence is a fixed template filled from the sprint
reading; none is written by a model. **Copy for the stand-up** puts the strip on the
clipboard followed by the sprint's table from [Copying a sprint as a table](#copying-a-sprint-as-a-table):
as HTML, which pastes formatted, and as plain text beside it.

### Keeping a note on an item

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/domain.md#sprint-item]
```

Write a one-line note on a sprint item, and mark it as an open question. Both
belong to the sprint, not to the task, and the next sprint starts without them.

## Closing a sprint

```meta
type: feature
status: draft
feature-flag: .devbook/domain/progress/context.md#progress-area
related: [.devbook/domain/progress/domain.md#close-outcome, .devbook/domain/progress/requirements.md#closing-a-sprint]
```

End the open sprint and start the next one in one step:

1. The person chooses an outcome for each unfinished item.
2. The person writes the next sprint's goal.
3. Progress closes the sprint and starts the next one.

The three outcomes are:

| Outcome | The task | The sprint records |
|---|---|---|
| Carry | Joins the next sprint, as a directly added member | Carried |
| Back to the backlog | Leaves the sprint and joins no other | Returned |
| Drop | Is untouched | Dropped, in this sprint only |

No outcome changes the task. The next sprint is numbered one higher and starts on
the first start weekday after the closed sprint's last day. It starts with no
linked plan: an unfinished task a plan brought in moves on only when it is carried.

### Closing from the sprint view

```meta
type: sub-feature
status: draft
related: [.devbook/domain/progress/features.md#the-sprint-view, .devbook/domain/progress/features.md#copying-a-sprint-as-a-table, .devbook/domain/progress/requirements.md#closing-a-sprint]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.SprintCloseTests, unit:dotnet:Backlog.UI.Components.UnitTests.SprintCloseDialogTests]
```

While the sprint view shows the open sprint, **Close sprint…** beside Add opens the
close dialog over the sprint as last read. It reviews the sprint:

- the goal met or not, with the goal tasks done of their total;
- all the sprint's work done of its total;
- how many items were added after the start.

Then it lists every unfinished item, with its goal mark, its plan, its owner and where
it stands under the title, and an outcome beside it: **Carry to Sprint n** — the
default — **Back to the backlog** or **Drop**. **Set all to Sprint n** puts every item
back on carry. Below the list the person writes the next sprint's goal.

**Copy the review as a table** copies the table from
[Copying a sprint as a table](#copying-a-sprint-as-a-table) with a **Result** column
added: Done, Carried to Sprint n, Back to the backlog or Dropped, following the choices
as they stand. **Close and start Sprint n** closes the sprint through Progress and the
view opens on the sprint it started. A refusal stays in the dialog in Progress's words.

The closed sprint stays one step back through previous, read-only like every closed
sprint, and still holds every item it had at closing.

## Syncing sprints between devices

```meta
type: feature
status: draft
depends-on: [.devbook/domain/tasks/features.md#multi-device-sync]
related: [.devbook/arc42/adr/0018-roadmap-plan-and-pace-ride-the-task-feed.md, .devbook/arc42/adr/0028-progress-is-its-own-boundary-and-tasks-knows-no-sprints.md, .devbook/domain/progress/domain.md#sprint-series, .devbook/domain/roadmap/features.md#syncing-the-plan-between-devices, .devbook/domain/progress/requirements.md#syncing-sprints-between-devices]
```

Keep every sprint series the same on every desktop the person has paired. A series
travels whole beside the tasks the devices already sync
([Multi-device sync](../tasks/features.md#multi-device-sync)): its cadence, naming,
goal rules and brief choices, every sprint with its dates and goal, which tasks and
plans belong to each, the goal marks, the notes and open questions, and how each
closed sprint ended. It travels only while sync is on and the device is paired.

Each series is replaced whole, never merged, as the roadmap's
[plan](../roadmap/features.md#syncing-the-plan-between-devices) is. When two devices
have both changed one series since they last synced, the **newer change wins on
every device** and the older one is lost. Two different series are two documents,
so a change to one never displaces a change to the other. A device that has **never
set up a series has nothing to send**.

A **deleted series is deleted on every desktop**. The deletion travels as the
series' last change, so the newer-change rule decides it like any other. A change
made to the series on another desktop before that desktop heard of the deletion
is older and is lost with it. A change saved after the deletion is newer and
brings the series back on every desktop.

Two desktops that each set up a series for the same repository while offline both
keep theirs. Once both have synced, **the repository works to the series set up
first**, and the other is switched off with a line saying why. It keeps every sprint
it recorded, and once the person takes the repository out of it, it can be switched
back on. Every desktop decides this the same way, from the series alone, and asks
nobody.

Series travel whether the [Progress area](context.md#progress-area) is on or off,
because the series recorded while it is off are kept. The phone keeps no sprints and
lists none.

## Sprints in the briefs

```meta
type: feature
status: draft
feature-flag: .devbook/domain/progress/context.md#progress-area
related: [.devbook/domain/progress/domain.md#brief-visibility, .devbook/domain/productivity/features.md#the-weekly-brief, .devbook/domain/progress/requirements.md#sprints-in-the-briefs]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.SprintWeekBriefTests, integration:dotnet:Backlog.Desktop.UI.UnitTests.DashboardWeeklyBriefTests, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.ProgressSprintBriefSourceTests]
```

Read the open sprint of a series in the daily and the weekly brief, for a series
that chose to show there. The daily brief names the sprint, its goal, how many goal
tasks are done, and whether the goal is met.

The weekly brief opens with a Sprint block for each series that is switched on, shows
there, and has a sprint overlapping the reported week; paging back a week reads that week's sprint.
The block holds:

- the sprint's name, then the series, its repositories, "week n of m" and the sprint's
  window;
- one sentence from a fixed template: the goal tasks and the items done by the week's
  end against an even pace, what was added after the start and removed, and what is
  blocked;
- five figures: goal tasks done, done this week, items in the sprint with those added
  after the start, pull requests merged that week, and blocked;
- "Shipped, by plan": the members done that week, under their plan, each with the
  pull requests it merged, linked to GitHub;
- "Into week n", or "Into next week" in the sprint's last week: the items still open,
  the blocked among them, and the pull requests waiting on review.

The even pace is the share of the sprint's items that would be done by the week's end
at the same rate every working day, Monday to Friday. Blocked, added and removed are
the members' current marks, because a sprint keeps no history of them; a task taken
out of an open sprint leaves no member, so only a close's Back to the backlog and Drop
count as removed. The Dashboard reads all of this through its own port, answered from
the Progress context's published surface. With Progress off, or no series showing in
the weekly brief, there is no Sprint block and the brief is unchanged.
