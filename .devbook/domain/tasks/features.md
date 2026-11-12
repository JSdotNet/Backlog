# Tasks

```meta
type: features
```

## Task creation

```meta
type: feature
feature-flag: .devbook/domain/tasks/context.md#task-backlog
related: [.devbook/domain/inbox/features.md#routing]
```

Create tasks manually or from triaged inbox items. New tasks default to `draft`.
A new task captures:

- its title and body;
- its type (prompt, task, idea, test);
- its tags;
- its project/repo link.

## Refinement and prioritization

```meta
type: feature
depends-on: [.devbook/domain/tasks/features.md#task-creation]
```

Edit and enrich tasks over time: set priority and status, add context links,
and flag oversized items with suggested splits. On the desktop a person edits the
task's Markdown content directly. They can also adjust these from the reading layout:

- type;
- priority;
- repository;
- status;
- metadata tags.

The task's repository picker lists **Brain** first, set off from the
repositories. Picking it clears the task's repository: Brain is the name the app
gives to filing a task against no repository (local ADR 0027).

Expanded tasks open into an inline Markdown editor for the full task body.
Compact tasks can stay on one line when they only need metadata-level
refinement.

![Desktop task with inline Markdown editing](assets/task-inline-markdown-editing.png)

### Filing a task against a roadmap tag

```meta
type: sub-feature
related: [.devbook/domain/roadmap/domain.md#roadmap-tag, .devbook/domain/tasks/features.md#search-filter-and-organize]
```

Choose a task's tags from a picker that offers every
[Roadmap Item](../roadmap/domain.md#roadmap-item) tag alongside the tags already
in use. A person files a task against planned work by picking the plan's own
vocabulary rather than retyping it. The tag then matches exactly, and the roadmap can gather the task back later.

The picker offers **every** roadmap tag, including ones no task has used yet. The
tag is the same slug the roadmap item holds, and it keeps
matching when the item's title is renamed. Tags remain free-form strings on the task, and nothing forces one to
come from the roadmap.

The picker offers each roadmap tag wearing its plan sigil, `+release-q4`, so a
reader can tell at a glance which tags file the task against planned work. The
[Roadmap Tag](domain.md#roadmap-tag) term says how the sigil reads and how the
roadmap matches it.

### Sub-items and steps

```meta
type: sub-feature
related: [.devbook/domain/tasks/requirements.md#sub-items-and-steps]
```

Break a task into ordered sub-items with a title, a status (pending or done),
and notes. The parent shows its progress, such as 3/5 done. Sub-items can project
to GitHub issue task lists.

On the desktop a step is a `##` chapter written in the task's body. A person edits that text to add,
rename, reorder and remove steps. A step is finished with
`[x]` in its heading or `!done` on the line under it. On the phone a person ticks a
step from the checklist on the task's own page, while the task is in
today's My Day.

A step is a [Sub-Item](domain.md#sub-item); that chapter says what a step
carries and what stays on the task.

### Attached material

```meta
type: sub-feature
related: [.devbook/domain/tasks/domain.md#attachment]
```

Point a task at the folder or archive where its material is kept, such as the
review pack, the screenshots or the exported data. Take the pointer off again
when it is no longer needed. The task records the place and its name,
and whether it is a folder or an archive.

The [Attachment](domain.md#attachment) chapter owns the rules: one place per
task, and a pointer rather than a copy. Task attachments are
machine-local (local ADR 0014), so a path is meaningful on the machine that wrote
it. A task read elsewhere may name a place that is not there, and stays valid. Only where the material is
is recorded, not how much.

### Acceptance from scenario parts

```meta
type: sub-feature
related: [.devbook/domain/devbook/features.md#rendered-devbook-documents, .devbook/domain/sessions/features.md#scenario-evidence-from-a-verify-stage]
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.ScenarioPartChecklistTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.EntryDevbookReferencesTests.Scenario_parts_are_an_acceptance_checklist_with_how_many_pass, unit:dotnet:Backlog.Desktop.UI.UnitTests.EntryDevbookReferencesTests.A_failed_part_names_the_step_its_run_stopped_on, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.DevbookReferenceResolverTests.A_failed_part_carries_the_step_its_run_stopped_on, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.DevbookReferenceToolsTests.Listing_answers_every_part_state_and_the_failed_step, e2e:dotnet:Backlog.EndToEndTests.ScenarioEvidenceTests.A_task_referencing_the_parts_shows_the_acceptance_checklist]
```

A task can point at the parts of a journey that prove it. A journey is one
end-to-end walk, a `##` chapter of a context's scenarios file in the repository's
devbook. Each of its `###` parts is an outcome the journey reaches.

The task
points at a part as `<scenarios file>#<part>`. It
can also point at a journey or a whole scenarios file, which stand for every
part they hold. A requirement stands for the parts its `Proved by:` lines name. A
part reached two ways counts once.

The open task lists these parts under its Devbook references as an acceptance
checklist headed "Acceptance · scenario parts". Each row shows the part's dot and the
part. A quieter line names the journey by its anchor, which also names the
run folder. It says when the journey last ran, as
"place-an-order · 14 Oct 2026 09:30", or "Never run". A failed part whose run recorded the step it stopped on adds that step, as
"· step 2".

The list says how many parts pass and whether that proves the task, as
"2 of 3 passing · not proved" or "3 of 3 passing · proved". A part reads as passed
only when its last run passed on the journey as it stands now. A part that passed
before the journey was edited is stale, because the run proved a different journey,
and it shows no step.

Pressing a part opens its scenarios file in the Devbook pane at that part. The part stays marked and scrolled into view until the
reader picks another chapter or reference. A part the file no longer has opens the file and marks nothing.

When every part passes, the task is proved. That is a signal only: the task keeps its status until a person moves it.

A task's own text can name a scenario screenshot, as an image written
`scenario:<scenarios file>#<label>`. The task detail shows the body as
Markdown source, so the image reads as the text written. Drawing it waits for a rendered reading of the body. Until then the
screenshot is shown where its scenarios file is read in the Devbook pane
([Rendered devbook documents](../devbook/features.md#rendered-devbook-documents)).

A coding session gets the same answer. Listing a task's Devbook references gives
each part with its state, last run and failed step, and a count
with whether the task is proved.

## Bulk editing

```meta
type: feature
depends-on: [.devbook/domain/tasks/features.md#refinement-and-prioritization]
related: [.devbook/domain/tasks/features.md#search-filter-and-organize, .devbook/domain/tasks/features.md#my-day, .devbook/domain/tasks/features.md#recurring-tasks, .devbook/domain/tasks/features.md#multi-repo-targeting]
```

Select several tasks in the current view and set one field across all of them
at once. The selection is made in the list and carries a running count.

| Field | Set | Add | Withdraw | Clear |
|---|---|---|---|---|
| Repo | Yes, replacing whatever repos each task targeted | | | |
| Status, type, priority | Yes | | | |
| Tags | | The chosen tags, to every selected task | One named tag, from every selected task | |
| My Day, reminder, due date | Yes | | | Yes |

Tags are the one field a bulk change does not replace: each task's remaining tags
stay untouched. Status, type, and priority always hold a value, so they can only
be set.

A bulk change reaches only the field being set. A task already holding the target
value is left alone and counted as unchanged.

The outcome is a count of the tasks that changed. A task that
cannot be saved is reported alongside that count rather than abandoning the
rest of the batch. A batch that only partly succeeds says so.

Marking a batch done spawns a successor for each recurring task it holds, as
completing them one at a time does.

A selected task that leaves the current view, by a filter change or by the change itself, leaves the selection.

The selection can also be deleted as a whole. The act sits beside the
field changes, names how many tasks it will take, and applies as directly as
deleting one task. The outcome is reported as for a field
change, and deleting everything selected empties the selection.

## Effort registration

```meta
type: feature
depends-on: [.devbook/domain/tasks/features.md#refinement-and-prioritization]
related: [.devbook/domain/tasks/domain.md#task, .devbook/domain/roadmap/features.md#gathering-work-under-an-item-and-totalling-its-effort]
```

Record a task's [Effort](domain.md#effort). The size of the work is then a fact
about the task rather than a guess made each time someone reads the plan it sits
under. A task with none is not estimated, which is a normal state. A re-estimate happens when the *understanding of
the size* changes, not when time passes.

An AI assistant may derive the estimate from the task's content, and a person
can always set or correct it by hand. The deriving is not built yet. This feature
makes the value **registrable and visible**, so a roadmap item that gathers this task
[adds up](../roadmap/features.md#gathering-work-under-an-item-and-totalling-its-effort) the points
registered here. An unestimated task is counted as unestimated there, never as zero.

## Open-work report

```meta
type: feature
depends-on: [.devbook/domain/tasks/features.md#effort-registration]
related: [.devbook/domain/roadmap/features.md#gathering-work-under-an-item-and-totalling-its-effort, .devbook/domain/tasks/features.md#task-dependencies]
```

Say how much work is open, not only how it splits. The total is one line: how many tasks
are open, the points they carry, and how many carry none. It sits at the end of the
filter bar. In a narrow list it moves beside **New entry**, so the bar keeps its room for filters.
The Board and the Calendar have no **New entry** row, so there it stays on the bar.

It counts what the
repository scope leaves in view, before any status, tag or other filter narrows
the list, so pressing a filter never changes the total.
Open means not ticked off. The points follow the
[effort total's rule](../roadmap/features.md#gathering-work-under-an-item-and-totalling-its-effort):
zero is an estimate, and the unestimated count is said beside the sum rather
than folded into it.

The line is also the way into a report of that same open work, which has
these sections:

- The headline numbers, and what was ticked off in the last seven days, as tasks
  and as points under the same rule.
- Each plan's open tasks, points and progress, with the work filed under no plan
  as its own line.
- How the open work splits by status, priority, type and repository.
- What needs attention: the tasks overdue and due within the week, each named
  and opening from the report. It also says how many tasks are waiting and what
  they weigh.

Waiting work is totalled rather than listed, because each row in the pane
already names [what it waits on](#task-dependencies).

## Scheduling and recurrence

```meta
type: feature
depends-on: [.devbook/domain/tasks/features.md#refinement-and-prioritization]
related: [.devbook/domain/tasks/domain.md#task]
```

Say when a task is due, when to be reminded of it, and whether it comes back.
These are three separate facts: a deadline, an alarm, and a shape. A
backlog that could only say "Friday" could not tell the task due
that day from the one that should interrupt you that morning.

All three are optional and none changes the task lifecycle. A task
with a due date in the past is overdue, read by comparison rather than
recorded as a status, so nothing has to sweep the backlog.

The scheduling fields are written on the task's metadata line as named tokens,
such as `due:2026-08-21`, `remind:2026-08-21T09:00` and `repeat:weekly`. They sit beside the sigil tokens for type, priority, status, area and tags. They are
named because the punctuation namespace is nearly
exhausted, and five one-character marks for five date-shaped concepts would be
unreadable in a hand-edited file.

### Due dates

```meta
type: sub-feature
```

Commit a task to a calendar day, its [Due Date](domain.md#due-date). The channel showing it decides how the
date reads, such as "Today", "Friday, 21 August" or a localized format.

### Reminders

```meta
type: sub-feature
```

Ask to be reminded of a task at a chosen local date and time, its
[Reminder](domain.md#reminder). One whose time has passed reads as overdue until
it is cleared or the task is completed, so a reminder that came due while the app
was closed surfaces.

### Recurring tasks

```meta
type: sub-feature
related: [.devbook/domain/tasks/domain.md#occurrence-spawning, .devbook/domain/tasks/features.md#archive-and-lifecycle, .devbook/domain/tasks/requirements.md#recurring-tasks]
```

Repeat a task every day, week, month or year,
optionally restricted to particular weekdays so "every weekday" is expressible.
The repeat is anchored to the due date, not to when the work finished. A task completed three days late still falls due on its original
weekday.

Ticking a recurring task off, which is what [Completed](domain.md#completed)
means, leaves it completed and creates the next occurrence as a new task. The
finished occurrence stays as the record of what was done. A repeating task therefore accumulates one
completed task per occurrence, and archiving keeps those out of the default
views. [Occurrence Spawning](domain.md#occurrence-spawning) says what the new
occurrence carries over.

## My Day

```meta
type: feature
depends-on: [.devbook/domain/tasks/features.md#refinement-and-prioritization]
setting: .devbook/domain/tasks/context.md#clear-my-day-overnight
related: [.devbook/domain/tasks/features.md#scheduling-and-recurrence, .devbook/domain/tasks/domain.md#agenda-time, .devbook/domain/tasks/domain.md#my-day, .devbook/arc42/06-runtime-view.md#mobile-my-day-and-task-push, .devbook/design/content-editing.md#scheduling-and-dependency-tokens, .devbook/domain/tasks/requirements.md#my-day, .devbook/arc42/adr/0032-my-day-preferences-ride-the-task-feed.md]
```

Pick the tasks to work on today, separately from when they are due. A task
due next Friday can be in today's [My Day](domain.md#my-day), and a task due
today need not be.

My Day expires on its own unless the person turns
[Clear My Day overnight](context.md#clear-my-day-overnight) off. With it on,
the default, yesterday's list clears itself without a timer or an overnight sweep. A device that was switched off for a week comes back to an
empty My Day.

With it off, an unfinished task picked on an earlier day stays in today's My Day
until the person ticks it off or takes it out. A task ticked off today stays,
ticked, until the day ends. One ticked off on an earlier day is gone, and a task moved to tomorrow never shows today. The person sets the switch on the desktop,
and every paired device follows it.

A task in today's My Day may also carry an [Agenda Time](domain.md#agenda-time):
a start time and a duration that place it in the day, such as 10:45 for 45
minutes. It is set and cleared on the desktop only, and dropped when the task leaves My Day. A
task carried over from an earlier day shows none, because its slot
belonged to the day it was picked for.

On the desktop, the agenda time sits in the task's detail pane under
the My Day control, while the task is picked for today. The task row shows
the block beside My Day, such as `10:45–11:30`.

- A start is picked in 15-minute steps and a duration from 15, 30, 45, 60, 90 or 120
  minutes.
- A start picked alone means 30 minutes.
- Changing the start keeps the duration, and changing the duration keeps the start.
- Clearing removes the agenda time and leaves the task in My Day.
- A start or duration typed into the text off those steps is kept and shown as it is.

On the phone, My Day is the Today tab, the first of three tabs
(Today, Inbox, Notes) and the screen the app opens on. Under the phone's own
date it says how many of the day's tasks are ticked off, as `3 of 8 done` with
a bar. It shows My Day, never a due-date grouping,
and splits it into four groups:

**Today's groups**

```mermaid
flowchart TD
    MyDay["Tasks in today's My Day"] --> Ticked{"Ticked off today?"}
    Ticked -- yes --> Done["Done today"]
    Ticked -- no --> Timed{"Has an agenda time?"}
    Timed -- no --> Anytime["Anytime today, in My Day order"]
    Timed -- yes --> Current{"Does its block contain the current time?"}
    Current -- yes --> Now["Now"]
    Current -- no --> Agenda["Agenda, in time order"]
```

**Now** holds the timed task whose block contains the current time, as
a card at the top labelled `Now · until 11:30`. The card carries the task's first
open step, a bar of its steps, and the ways into Focus and the task's own page.
Between blocks it shows the next timed task, labelled
`Next · 14:00`. With none still to come there is no card.

- **Agenda:** the other open timed tasks in time order, each showing its
  block as `10:45–11:30`.
- **Anytime today:** the open tasks without an agenda time, in the order My
  Day keeps them.
- **Done today:** the tasks ticked off today.

Every row ticks its task off or back on, and opens the task's own page.

**The phone's screens**

```mermaid
flowchart TD
    Today["Today"] -- "a row, or the Now card" --> Page["The task's own page"]
    Today -- "the Now card" --> Focus["Focus"]
    Page -- "Focus on this" --> Focus
    Page -- "Move to tomorrow, Mark done" --> Today
    Focus -- "Complete task" --> Today
    Focus -- "Capture a thought" --> Sheet["Capture sheet"]
    Sheet -- "save, cancel, or tap outside" --> Focus
```

The task's own page shows, from top to bottom:

- a way back to Today, and the title;
- chips for the task's area, its block as `Today · 10:45–11:30` when it has an
  agenda time today, and its effort as `3 points`;
- **Focus on this**, when the task has an agenda time today;
- the task's steps as a checklist headed by how many are done, as `2 of 5`;
- the task's own text, read-only;
- **Move to tomorrow** and **Mark done**.

The steps, Move to tomorrow and Mark done change a task only while it is in
today's My Day and not yet ticked off. Any other task shows its steps
locked and without the two buttons or Focus.

**Focus** exists only for a task with an agenda time today that is not yet
ticked off; for any other task the task's own page opens instead.
It shows the task's area and when its block ends, as `Sync · until 11:30`,
and the title. A ring counts down to the end of the block every second, as
`28:40` left in this block, and past the end reads `Over by 03:12`. The countdown is the block itself, so there is no pause.

Below the ring sit the current step, the first not ticked off, as
`Step 2 of 5 · now`, then the steps in order. **Step done — next** ticks off the
current step. When none is left it becomes **Complete task**, which ticks off
the task. **Capture a thought** opens the capture sheet over Focus.

The phone changes four things about a task and nothing
else:

| On the phone | What it writes |
| --- | --- |
| Add a task | A new task picked for today. |
| Mark a task done or undone | The task's tick, the way the desktop's checkbox writes it. See [Completed](domain.md#completed). |
| Tick or untick a step | The status of one [Sub-Item](domain.md#sub-item). |
| Move a task to tomorrow | `myday:` set to the phone's local date plus one day. |

The Today screen adds through its capture button. The other three
changes apply only to a task in today's My Day. Editing, scheduling, the
agenda time and the rest of a task's life stay on the desktop. The phone's delivery is in
`.devbook/arc42/06-runtime-view.md#mobile-my-day-and-task-push`.

## Task dependencies

```meta
type: feature
depends-on: [.devbook/domain/tasks/features.md#refinement-and-prioritization]
related: [.devbook/domain/tasks/domain.md#task, .devbook/domain/tasks/features.md#manual-ordering]
```

Record that a task waits on other tasks, so prompts or tasks meant
to be worked in order say so. A task can wait on several others.

This differs from manual ordering. Ordering is the sequence a person
arranged the backlog in; a dependency is a constraint that holds in any list order.

### Readiness and chain order

```meta
type: sub-feature
```

Read off what can actually be started, as each task's
[Readiness](domain.md#readiness). A blocked task says what it is waiting for.
Finishing one task unblocks its dependents with nothing to recalculate.

Two questions get separate answers: which
task to pick up first, and which tasks could be started now. In a straight chain
those are the same task. When finishing one step unblocks three, they are not.

[A dependency naming a task that cannot be found still blocks](domain.invariants.md#invariant-a-dependency-naming-no-task-still-blocks),
and [dependency loops are named rather than broken](domain.invariants.md#invariant-every-task-in-a-dependency-loop-reads-blocked);
[Dependency](domain.md#dependency) says why.

### Marking a task blocked

```meta
type: sub-feature
related: [.devbook/domain/progress/features.md#raising-an-impediment, .devbook/domain/progress/features.md#linking-what-an-impediment-holds-back, .devbook/arc42/adr/0035-goals-and-impediments-replace-sprints.md]
```

Mark a task blocked by hand when something outside the backlog holds it up. Readiness differs: it reads blocked from `depends_on`. The mark is the
`blocked:` token on the task's metadata line, with the day it was set. An agent
sets or clears it with the `set_blocked` tool. Removing the token unblocks the task.

The mark is the task's alone and raises no impediment. A person who wants the block
tracked [raises an impediment](../progress/features.md#raising-an-impediment) and
[links the task](../progress/features.md#linking-what-an-impediment-holds-back) to it.
The task stores no impediment id, and Tasks never calls Progress.

### Filing a follow-up after an entry

```meta
type: sub-feature
related: [.devbook/domain/tasks/requirements.md#filing-a-follow-up-after-an-entry, .devbook/domain/tasks/domain.md#dependency, .devbook/domain/notifications/features.md#asking-from-the-contexts, .devbook/domain/tasks/features.md#re-importing-an-updated-plan, .devbook/arc42/adr/0038-backlog-queues-session-requests-and-a-manager-session-dispatches-them.md]
```

File one new prompt that waits on an entry, when the person approves that entry's
delivery run and asks for a follow-up in the same answer. The person answers the
run's Personal Validation question with Approve + follow-up and a few words, on the
phone or desktop. Sessions records the approval and asks Tasks to file the
follow-up. The entry the run was working is the **gated entry**.

The follow-up is an ordinary [Task](domain.md#task), born as follows:

| Field | Value |
| --- | --- |
| Type | `prompt` |
| Status | `ready` |
| Waits on | the gated entry, as `after:<id>` |
| Tags | the gated entry's plan tag, when it has one |
| Repository | the gated entry's |
| Title | "Follow-up: " and the gated entry's title |
| Body | the person's words, as body text |
| Provenance | the id of the notification whose answer asked for it |

The gated entry's plan tag is its `import_plan_id`, or else the one plan tag it
carries. The follow-up carries that tag and no `import_plan_id`, so the plan still lists it.
[Re-importing the plan](#re-importing-an-updated-plan) leaves it alone, because
the re-import clears tasks by `import_plan_id`. A gated entry outside any plan gets
a follow-up with no plan tag. A follow-up of a follow-up stays in the plan through
the tag its gated entry carries. Sessions names the gated entry by its id, or by
its plan and item.

The notification id is the follow-up's provenance and its key. An answer handed
over twice files one follow-up, and a deleted follow-up is not filed again by the same answer.

The person's words stay body text. A line that looks like a sub-item, a heading or
a fence is kept as plain text, so the words cannot add steps or structure.

Nothing here creates a session request:
[only a click in the app requests a session](../sessions/requirements.md#requirement-only-a-click-in-the-app-requests-a-session),
as §1 of local
[ADR 0038](../../arc42/adr/0038-backlog-queues-session-requests-and-a-manager-session-dispatches-them.md)
decides. An attended `execute-plan` runs the follow-up like any other ready prompt
of its plan once the gated entry lands, and `run-plan-item` checks it before
doing anything.

## Multi-repo targeting

```meta
type: feature
depends-on: [.devbook/domain/tasks/features.md#task-creation]
related: [.devbook/domain/tasks/features.md#projection]
```

Let one task target multiple repositories (`repo_ids[]`) as a
single source of truth: one item, one priority, one status, in one view across all contexts.

## Projection

```meta
type: feature
depends-on: [.devbook/domain/tasks/features.md#multi-repo-targeting]
related: [.devbook/domain/monitoring/features.md#tasks-and-github-progress, .devbook/domain/tasks/requirements.md#projection]
```

Spawn and close downstream artifacts from a task, without duplicating the task.
What ships is the issue push below, made when the person asks. The
automatic create-and-close is the intended [Projection](domain.md#projection)
policy, and what it will promise is in
[its requirements](requirements.md#projection).

### Issue projection and state read-back

```meta
type: sub-feature
feature-flag: .devbook/domain/tasks/context.md#github-integration
related: [.devbook/domain/repository-management/features.md#github-access-resolution, .devbook/domain/monitoring/features.md#tasks-and-github-progress, .devbook/arc42/adr/0042-repository-management-owns-pull-request-state.md#4-tasks-reads-pull-requests-through-its-own-port]
```

**The background check of a linked pull request**

```mermaid
flowchart TD
    Due["Every pull request due a check"] --> Read["One read through the shared cache"]
    Read --> Answer{"The answer"}
    Answer -- "merged" --> Merged["Link turns green, the merge is remembered"]
    Merged --> Prompt["A prompt entry moves to Done"]
    Answer -- "closed without merging" --> Closed["Shown, not remembered"]
    Answer -- "open, or could not be read" --> Wait["Asked again after two minutes"]
    Wait --> Due
```

Push a task to its target repository as an issue carrying the task's title,
body, and tags. The resulting issue reference is kept on the task, so the link
is part of the item rather than a note about it. The task can then be asked to
re-read that issue's current state, and the pull request that references it.
Downstream progress is then visible from the backlog.

The pull requests a task records are also checked in the background, without the
person asking. The pull requests pane, Progress and the side panel share the cache that one read goes through. Merged and closed answers land on screen
before open ones.

A merge is remembered on this device. Repository Management keeps it in app storage, so the backlog opens instantly and offline
and draws a merged link from the first frame. Tasks keeps only which of its
entries each merge has finished (`IPullRequestFinishMemory`). A restart therefore
neither moves an entry to Done twice nor forgets a move still owed. Neither fact is written to the
task: losing the memory costs one read per
pull request. An open pull request's status is never remembered, because a
stale one would offer a merge GitHub no longer allows.

A prompt entry moves to Done only when the merge is into the
repository's default branch or a `plan/` branch. It moves once per entry and merge,
and only where its lifecycle allows the move. A task or test entry stays the
person's to tick.

## Linked tasks

```meta
type: feature
related: [.devbook/domain/tasks/domain.md#linked-task, .devbook/domain/tasks/domain.md#connected-target, .devbook/domain/tasks/requirements.md#linked-tasks, .devbook/arc42/adr/0020-external-items-arrive-as-linked-tasks.md, .devbook/domain/repository-management/requirements.md#archiving-a-repository]
```

Connect a GitHub repository or a spec-manager product, and every open item in it
appears on the Tasks list as a [Linked Task](domain.md#linked-task). The item
needs no triage and no copying. Each sync keeps the title, status and assignee
in step with the source on the terms
[its requirements](requirements.md#linked-tasks) set. It leaves the person's
own priority, My Day, area, sub-items and notes alone.

Every linked task shows a source badge on its row, in the editor header and in
My Day, and the badge opens the item at the source. The Tasks pane filters by
source but not by assignee ("Assigned to me" was removed), though
each sync still keeps the assignee. A label written `+slug` at the source files the
task under that plan. It then appears on the roadmap's
[shelf](../roadmap/features.md#laying-out-a-plan-whose-tasks-arrived-first)
without a step in Backlog.

Settings → Connectors shows each [Connected Target](domain.md#connected-target)
with two settings that decide where its tasks land:

- **Repository** picks one of the repositories in the
  [Repository Registry](../repository-management/domain.md#repository-registry).
  The target's new tasks, and its tasks not yet filed under a repository, are
  filed under it. A spec-manager product's tasks can then sit under the
  repository the product is built in. Left empty, a GitHub repository files its
  tasks under itself.
- **Statuses** has one row for each status the source lists, in the source's
  order, each with a task status to pick. The empty choice keeps the status the
  fixed map gives and names it, such as "Default (In progress)". A
  spec-manager product lists its own statuses. A GitHub repository lists none,
  so it shows no rows.

A changed status applies to new tasks at once, and to an existing task when its
item next moves at the source.

The card also holds the target's other settings:

- how old an untouched item may be before the first sync skips it;
- whether the title follows the source;
- the sync interval;
- "Complete at the source", for a connector that can complete items.

A connector that can list its targets offers them to pick by name.

### Syncing a target

```meta
type: sub-feature
related: [.devbook/domain/tasks/domain.md#connected-target, .devbook/arc42/adr/0020-external-items-arrive-as-linked-tasks.md]
```

The desktop syncs each enabled target on its own timer, every fifteen minutes
unless the target sets another interval. **Sync now** on the target's card in
Settings → Connectors runs one at once and says on the card how it went. The
Tasks pane has no sync button.

A sync that fails writes nothing, and the card shows why in a sentence the
person can act on, with when it happened. The next sync still asks for what
changed since the last success, and its success clears the message.
Each successful sync logs how many items it fetched,
created, updated and archived.

A spec-manager product is read whole on every sync: the open backlog has no
"changed since" question, so only its archived items are asked for by date.

### Deleting the tasks of a disconnected source

```meta
type: sub-feature
related: [.devbook/domain/tasks/domain.md#connected-target, .devbook/domain/tasks/domain.md#tombstone, .devbook/domain/tasks/requirements.md#requirement-reconnecting-a-target-restores-the-tasks-deleted-with-it, .devbook/domain/tasks/requirements.md#requirement-a-deleted-linked-task-is-never-made-again, .devbook/arc42/adr/0020-external-items-arrive-as-linked-tasks.md]
```

```mermaid
stateDiagram-v2
    state "Live" as Live
    state "Tombstone, deleted with its source" as WithSource
    state "Tombstone, deleted on its own" as Alone
    Live --> WithSource : Delete tasks, target disconnected
    Live --> Alone : the person deletes it
    WithSource --> Live : next sync fetches its item, target connected again
    WithSource --> Live : Restore deleted tasks
    Alone --> Live : Restore deleted tasks
```

Disconnecting a target keeps its tasks on the list as they are. Settings →
Connectors lists each source that left tasks behind under **Disconnected
sources**, with how many tasks it left. **Delete tasks** asks first and then
makes every one of them a [Tombstone](domain.md#tombstone), whatever its status.

The confirmation says that the items at the source stay untouched and that the
deletion reaches the other devices. It also says that connecting the target
again brings the tasks back, and how many other tasks will wait on a missing
one. A source connected again before the delete runs is refused, because its
tasks belong to the sync again.

The sync that restores a task brings it up to date from the source. A first sync
fetches open items only, so a task whose item has closed since stays deleted
until the person restores it. A task deleted on its own is never brought back
by the sync.

### Restoring deleted tasks

```meta
type: sub-feature
related: [.devbook/domain/tasks/domain.md#connected-target, .devbook/domain/tasks/domain.md#tombstone, .devbook/domain/tasks/requirements.md#requirement-restore-deleted-tasks-restores-every-deleted-task-of-the-target, .devbook/arc42/adr/0020-external-items-arrive-as-linked-tasks.md]
```

A connected target whose linked tasks include deleted ones shows **Restore
deleted tasks** on its card in Settings → Connectors. The confirmation names
how many tasks come back, including those deleted one by one. Confirming restores every deleted task of that target and no other, and
the restore reaches the other devices. The next sync brings each task up to
date. A target disconnected before the person confirms is
refused, and nothing is restored.

### Promote to plan

```meta
type: sub-feature
related: [.devbook/domain/roadmap/domain.md#roadmap-item, .devbook/arc42/adr/0020-external-items-arrive-as-linked-tasks.md]
```

Dropped and never built: it would have turned a linked task into a Roadmap Item. The owner decided against it, and the target setting for what happened to the original task was removed. A linked
task reaches the roadmap only through its [plan label](../roadmap/domain.md#source-label),
and [a sync never creates a Roadmap Item](requirements.md#requirement-a-sync-never-creates-a-roadmap-item).

## Search, filter and organize

```meta
type: feature
related: [.devbook/domain/brain/features.md#bi-directional-linking, .devbook/arc42/adr/0022-the-shell-shows-one-main-view-picked-by-a-view-switch.md, .devbook/design/interaction-guidelines.md#shell-header, .devbook/arc42/adr/0027-the-brain-is-a-scope-beside-the-repositories.md, .devbook/domain/brain/features.md#brain-scope, .devbook/domain/repository-management/requirements.md#archiving-a-repository]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.The_board_view_lays_the_tasks_out_in_columns_under_the_filter_bar_and_is_remembered_with_its_columns, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.The_calendar_view_lays_the_tasks_out_on_a_month_under_the_filter_bar_and_is_remembered, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.The_view_is_remembered_and_a_fresh_shell_instance_reopens_on_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.A_remembered_scope_comes_back_anchor_first_with_the_devbook_option_offered, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.A_remembered_repository_no_longer_configured_is_dropped, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.A_remembered_plural_scope_restored_onto_the_roadmap_collapses_to_the_anchor, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.A_scope_change_is_remembered_and_every_repository_writes_no_key, unit:dotnet:Backlog.Desktop.UI.UnitTests.TagFilterTests.A_pressed_plan_can_be_copied_as_its_tag, unit:dotnet:Backlog.Desktop.UI.UnitTests.TagFilterTests.No_plan_and_general_tags_offer_no_plan_to_copy, unit:dotnet:Backlog.Desktop.UI.UnitTests.TagFilterTests.Pressing_a_plan_further_along_the_strip_leads_it_with_its_copy, unit:dotnet:Backlog.Desktop.UI.UnitTests.TagFilterTests.A_plan_press_hands_the_focus_back_to_the_moved_chip]
```

Find and narrow the backlog:

- Search across title, body, tags and linked knowledge notes.
- Filter by [Area](domain.md#area), repo, type, status, priority, and recency.
- Read the tasks in grouped views, with Devbook content embedded inline.

The repository scope can hold several repositories at once while a screen that
can show several is in front. The scope narrows back to one on the
roadmap and on Tools, which read one repository or none. The screens that can show
several are:

- the task views;
- In progress;
- the Dashboard;
- the session list;
- the pull requests list.

The scope leads with a permanent **Brain** chip, the
[Brain scope](../brain/features.md#brain-scope): Brain is the scope of
everything filed against no repository.

- Pressed alone, it shows only the tasks with no repository, in every view that
  reads the scope.
- Ctrl adds it to the repositories already pressed, as it adds a repository.
- Nothing pressed still means every task.

Brain is the only way to narrow to tasks with no repository (local ADR 0027).

The repository scope is remembered on this device in the order it was chosen.
The app reopens on the same repositories, the first chosen still first,
and Brain in its place when it was pressed. The Devbook is then offered on
them with no chip to press. A repository no longer
configured is dropped, and a scope with none left is
every repository. A scope narrowed back to one comes back as that one. Where an
[archived repository](../repository-management/domain.md#archived-repository)
appears in the scope, the Board and the pickers is set by
[its requirements](../repository-management/requirements.md#archiving-a-repository).

The filtered tasks can be read three ways, picked in the header's view switch:
**Tasks**, the list with the open task beside it; the
[Board](#board-view); and the [Calendar](#calendar-view). The three share one
filter bar, one open task and one selection, so switching never loses what the reader narrowed to.

Among the filters is the [Task Type](domain.md#task-type): prompt, task, idea or
test, one at a time. The Board and the Calendar, including its tray of tasks with
no due date, narrow by type like the list. The filter bar belongs to
these three views only. The app reopens on the view last
chosen, and on Tasks by default. The Inbox and the Devbook open
beside any of them.

The source and type filters offer only what has open work, plus the one pressed.
In a narrow list, both fold into one **Filter** menu on the bar.

The tags on the filter bar sit in one row of their own, below the other filters,
and the strip shows a **More** toggle only while its tags overflow it. A pressed
[plan](../roadmap/domain.md#roadmap-tag) leads the strip, whether the reader pressed it on the bar or arrived with it pressed from the roadmap. The plan
carries a copy button fused to its chip, which copies the plan tag verbatim,
`+` and all, while the
[`copy-entries`](context.md#copy-entries) flag is on. With the
[`session-requests`](../sessions/context.md#session-requests) flag on, the plan's
[queue icon](../sessions/features.md#start-a-session-from-an-entry) follows the
copy, and the waiting chip takes its place while a request is open. **No plan** and
the general tags offer no copy, and a plan offers none once it is let go. When
the reader presses a plan further along the strip, the plan moves to the front
with its copy, and the focus stays on the moved chip.

### Manual ordering

```meta
type: sub-feature
related: [.devbook/domain/tasks/requirements.md#manual-ordering]
```

Hand-sequence tasks within the backlog by dragging them into a preferred
order, independent of recency or priority. Where a task nobody ranked lands is
[a requirement](requirements.md#requirement-unranked-tasks-fall-back-to-recency).

### Board view

```meta
type: sub-feature
related: [.devbook/domain/tasks/domain.md#task, .devbook/domain/tasks/domain.md#effort, .devbook/domain/tasks/domain.md#roadmap-tag, .devbook/domain/repository-management/requirements.md#archiving-a-repository]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksPaneBoardTests.Columns_by_plan_leave_out_finished_work_and_a_plan_with_nothing_left_has_no_column]
```

The same filtered tasks laid out as columns of cards, under the same filters
and beside the same open task. The columns can be:

- by status, the default: Draft, Ready, In progress and Done;
- by plan: one per [Roadmap Tag](domain.md#roadmap-tag) with work left in view,
  then the tasks in no plan. A ticked-off or
  archived task is no card here, so a plan with nothing left draws no
  column;
- by repository, with the tasks filed against no repository in a column titled
  **Brain**, the name the scope gives them;
- by [Priority](domain.md#priority).

Each column says how many tasks it holds, the [Effort](domain.md#effort) they
add up to, and how many of them nobody has estimated. A task under two plans is
in both plan columns. The choice of columns is remembered with the view.

While the columns are by status, the status filter steps aside, since every
status is a column. A task can then be dragged to another column, but only a move
the [task lifecycle](domain.md#task) allows is taken. While a task is
held, the columns it may go to are marked, and the others say why not. A ready task, for example, has to be started before it can be
done. Dropping it makes the same status change as the task's status picker.

Each status column ends in a way to write a new task into that status.
With the task open beside it, the board scrolls sideways rather than squeezing its columns.

### Calendar view

```meta
type: sub-feature
related: [.devbook/domain/tasks/domain.md#due-date, .devbook/domain/tasks/features.md#my-day, .devbook/domain/roadmap/features.md#laying-out-a-plan-whose-tasks-arrived-first, .devbook/design/interaction-guidelines.md#group-shape-says-cardinality]
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.The_calendar_range_is_remembered_and_a_fresh_shell_reopens_on_it]
```

The same filtered tasks laid out on a month or on a week by their
[Due Date](domain.md#due-date), under the same filters and beside the same open
task. A **Month | Week** choice beside the month's name picks the range. One of the two is always pressed, and the choice is remembered on this device. The month
runs Monday to Sunday in whole weeks, so its first and last rows reach into the
neighbouring months. Those days are dimmed, and weekends are shaded. Today is
marked and says how many tasks are in [My Day](#my-day).

Each day lists its tasks with their lifecycle state. An open task
past its due date stands out as overdue, and a finished one is struck through. On
the month, past two a day folds the rest behind a count that opens the whole day.
A recurring task shows on its due date only, because the next occurrence is written when
this one is finished.

The open tasks with no due
date wait beside the calendar. Dragging one onto a day gives it that due date,
and dragging a task to another day moves it. Only the due date places a task here.

The week shows its seven days, Monday first, as tall columns. Each day lists
every task, with nothing folded and no day dimmed. The reader picked the week, so a day of the neighbouring month is part of it. The plans, the
[planned hours](#planned-hours) and the drags work as on the month.
Previous and next step a week at a time. The heading names the week's
days, such as "5 – 11 October 2026".

- Switching to the week opens on the week holding today when today is in the
  month shown, and on the month's first week otherwise.
- Switching back to the month shows the month holding the week's Thursday. A
  week across two months therefore lands in the month that holds most of its
  days.
- Only the range is remembered, never the date shown.

The roadmap's plans can be drawn over the month. A **Show plans** choice beside
the month's name turns them on; it is off by default, and remembered on this device.

- Each planned item's window is a bar across the days it spans, cut at every
  week, under the day numbers and above the tasks.
- Plans that overlap in a week each take a row of their own. A bar carried on
  from the week before opens with "…".
- A bar says its title, its plan tag and how many of its points are finished out
  of the total.
- A bar wears its repository's colour while repository colours are showing, as on
  the roadmap. It wears a neutral grey while they are not.
- A milestone is a diamond on its date.

Beside the tasks with no due date, **Plans without a window** lists the plans
waiting on the roadmap's
[shelf](../roadmap/features.md#laying-out-a-plan-whose-tasks-arrived-first).
Dropping one on a day starts its window there. The roadmap, never the Tasks view,
works out where it ends from its points and the pace, as it places any plan.

The drop is a pointer gesture. The keyboard ways are on the
roadmap: its shelf's **Plan it** places the plan from today, and its item editor
moves the window to any day. The Calendar reads the plans and starts a shelf plan, and changes nothing else.

### Planned hours

```meta
type: sub-feature
related: [.devbook/domain/tasks/domain.md#planned-hours, .devbook/domain/tasks/features.md#calendar-view, .devbook/domain/roadmap/features.md, .devbook/design/interaction-guidelines.md#combobox]
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.TaskCalendarHoursTests, unit:dotnet:Backlog.UI.Components.UnitTests.PlannedHoursEditorTests, unit:dotnet:Backlog.UI.Components.UnitTests.CalendarHoursTests]
```

The person can set [Planned Hours](domain.md#planned-hours) aside for a task on
a day of the [Calendar](#calendar-view). A day shows each such task as a chip of its own with the figure, such as "Refactor sync · 3h", apart
from the due-date chips. The day adds its hours up against the hours the person
works that day, read from the roadmap's working week and days off. A day holding
more is marked, and says by how much.

Hours are set in either of two ways:

- From a day's **Add hours…**, which asks for the task and the figure.
- By dragging a task onto a day with Shift held, from the month or from the
  tasks with no due date. This asks for the figure only and leaves the due date.

Pressing a task's hours on a day changes or removes them. The open task's
**Planned hours** lists every day it has hours on, to change, remove or add one.
The hours follow the filters and repository colours like the task chips.

The figure is typed, or taken from suggestions: half an hour, and
1, 2, 3, 4, 6 and 8 hours. A typed figure is rounded to the quarter hour. A figure
that is not a number, is none, or is more than a whole day is refused with a
sentence that says why.

In the open task's **Planned hours**, a refused figure goes back to the
figure that stood. Taking a suggestion for a day there changes that day at once.
A typed figure is kept on Enter or when the field is left. On the line that adds
a day, a suggestion only fills in the figure, and **Add hours** adds the day and
the figure together.

When the day's hours editor closes, the keyboard focus goes back to what opened
it: the task's hours on the day, or the day's **Add hours…**. After the hours are removed, the
focus goes to **Add hours…**.

The roadmap never reads planned hours. A plan's window and pace come from points and finished work, so setting hours aside moves no bar on
the roadmap or the Calendar.

## Prompt features

```meta
type: feature
```

Copy a prompt's text to the clipboard in one click. Each copy or use is logged
in the task's usage history, and a person can reopen earlier prompts from that
log. The copy button sits on the task's row, on its board card and in the open
entry while the [`copy-entries`](context.md#copy-entries) flag is on (the default). Off, the button is gone and the queue icon beside it stays.

### Copy as a runnable paste

```meta
type: sub-feature
feature-flag: .devbook/domain/tasks/context.md#copy-entries
related: [.devbook/domain/tasks/features.md#import, .devbook/domain/tasks/domain.md#task-type]
```

The copy hands over a prompt an AI session can run. It leads with one line that invokes the
`backlog-tools:run-plan-item` skill on the [Task](domain.md#task), by the id
Backlog knows it by. The title and body follow as written.

The metadata line stays behind, because it is the app's bookkeeping. The plan a task was [imported](#import) under, the repositories it
targets and the tasks it [waits on](#task-dependencies) are Backlog's to answer
when the session asks by that id. A session that can ask checks whether the task
is still outstanding, and tells Backlog when the work is
picked up and when it lands. A session that cannot ask reads a prompt with one
more line on top.

Every [type](domain.md#task-type) is copied this way, a `task` too. The session decides whether to run it.

### Start a session on a prompt

```meta
type: sub-feature
feature-flag: .devbook/domain/sessions/context.md#session-requests
related: [.devbook/domain/sessions/features.md#start-a-session-from-an-entry, .devbook/domain/tasks/features.md#multi-repo-targeting]
```

Queue a coding session that runs a prompt, in its repository's clone folder, without
copying it. The queue icon sits beside the copy button, on the row and
in the open entry. Pressing it creates a session request in the Sessions context, and the
entry shows it is waiting for a session until the session links itself. The
[Sessions feature](../sessions/features.md#start-a-session-from-an-entry) says who is
offered it and what the waiting chip shows.

### Hand-off to Copilot CLI

```meta
type: sub-feature
feature-flag: .devbook/domain/tasks/context.md#copilot-cli
related: [.devbook/domain/productivity/features.md#ai-activity-capture]
```

Hand a task to GitHub Copilot CLI as a task brief without retyping it. The
task's own Markdown is the brief: its title, metadata, body and sub-items. The
hand-off is recorded in the task's usage history, so the task itself shows that
AI was put to work on it.

## Import

```meta
type: feature
depends-on: [.devbook/domain/tasks/features.md#task-creation, .devbook/domain/tasks/features.md#task-dependencies, .devbook/domain/tasks/features.md#multi-repo-targeting]
related: [.devbook/domain/tasks/features.md#prompt-features, .devbook/domain/tasks/features.md#sub-items-and-steps, .devbook/domain/tasks/features.md#filing-a-task-against-a-roadmap-tag, .devbook/domain/tasks/domain.md#task-type, .devbook/domain/tasks/domain.md#task-status, .devbook/domain/repository-management/features.md#repository-registration, .devbook/domain/inbox/domain.md#triage, .devbook/domain/tasks/requirements.md#import]
```

```mermaid
flowchart TD
    Doc["A document, uploaded or pasted"] --> Count["Import says how many roadmap items and tasks it will produce"]
    Count --> Tasks["Task entries are written as Tasks"]
    Tasks --> Plans["plan entries go to Roadmap Planning"]
    Plans --> Result["The result reports both halves apart"]
```

Bring in a plan that lists a sequence of work across one or more repositories,
in the order its entries depend on each other. Import turns the whole plan into
tasks in one step. Most plans
are sequences of AI prompts to run, which is the case Import was written for. An
uploaded file and a paste read the same plan. Import is a faster way of filling the backlog, not
a second way of holding work.

Each entry in the plan becomes one [Task](domain.md#task) of the
[type](domain.md#task-type) the plan states for it, one of the four types
[task creation](#task-creation) offers. `prompt` is the common case. An entry that states no
type becomes a plain `task`, the default for a hand-typed task. One plan can
therefore mix prompts to run, steps
only a person can do, and things worth coming back to later.

An entry may also state the [status](domain.md#task-status) it should arrive at.
Where the entry came from decides which status the task is born with:

| The entry | The task is born |
|---|---|
| Was drafted from an inbox item | `draft`, whatever status its text states |
| States a status | At that status, set directly rather than as a lifecycle step |
| States no status | `ready` |

A stated status is reachable whatever it is, the settled ones included. A plan
can therefore record a step finished before import, or hold back
one still being shaped as `draft`.

`ready` is the one default Import sets differently from a hand-typed task, which starts at `draft` while it is being shaped. A
plan is work already agreed. If every entry
landed at `draft`, somebody would have to promote each one by hand before
[Readiness](domain.md#readiness) showed any of it.

An entry drafted from an inbox item carries that item as its `source_inbox_id`.
That is the item a plan was drafted from (`sourceInboxId`), or for a batch routed together, the item the entry's `id:` maps to
(`sourceInboxIds`). A model wrote the entry about captured content and nobody
has read it yet, so the provenance decides the status, not the text. This
governs birth only: a later version that restates the status of a task already
under way still moves it.

The entry's instructions become the task's body. The order the plan
declares between entries becomes an ordinary
[task dependency](#task-dependencies) between the tasks import creates, with the
plan's local entry references resolved to the real task ids. "What's next" for an imported chain reads as for any other: by
[Readiness](domain.md#readiness), grouped by [repository](#multi-repo-targeting).

The plan itself becomes one of the task's tags, filed like a task
[filed against a roadmap tag](#filing-a-task-against-a-roadmap-tag). Everything
one plan brought in can then be found and filtered as a group.

A plan also carries work around a prompt that is not the prompt itself:

- a setup or dependency step the repository needs before the prompt can run,
  such as installing a plugin;
- a reminder to update that repository's devbook;
- a task only a person can do.

These land as ordinary
[sub-items](#sub-items-and-steps) on the task they belong to, ordered ahead of
the entry they gate, the same as hand-typed ones.

Every task import creates carries where it came from; see
[Task](domain.md#task), so a batch stays traceable to the plan and the item that produced each task.

A document may also carry entries that are not tasks. Import hands an entry
whose type word is `plan` to
[Roadmap Planning](../roadmap/features.md#laying-out-imported-plans). Such an entry describes the plan as planned work:

- its title;
- its tag;
- its repositories;
- what it waits on;
- when it is due.

One document may hold `plan` entries and task entries together, or only one
kind. A `plan` entry is the one kind Import acts on that
nothing else in this context will. Pasted anywhere but Import, it is refused.
The decision is
`.devbook/arc42/adr/0013-imported-plan-is-a-roadmap-item-laid-out-by-import.md`.

A document of task entries alone can also be
laid out on the roadmap at once, but only when the person asks. It then gets one
item per plan tag its entries carry, titled from the tag and scoped to the
repositories those entries name. This is off by default, so a plan re-imported for its tasks does not grow an unwanted item.

Each level waits only on its own kind. A task entry's `after:`
naming a `plan` entry, or the other way round, is reported as unresolved.
A roadmap that refuses its half,
such as plans waiting on each other in a circle, leaves the tasks
written and says so.

### Repository resolution on import

```meta
type: sub-feature
related: [.devbook/domain/repository-management/features.md#repository-registration, .devbook/domain/repository-management/domain.md#repository-registry]
```

Resolve each entry's target repository by the name the plan's author wrote,
against the same [Repository Registry](../repository-management/domain.md#repository-registry)
[multi-repo targeting](#multi-repo-targeting) already resolves `repo_ids`
against. A known name resolves to its `repo_id`. A name the registry has never seen is registered there on the spot,
before the task naming it is created. Import uses Repository Management's
[registration capability](../repository-management/features.md#repository-registration),
so a plan can introduce a repository just by mentioning it.

Import triggers registration; it does not perform it. The repository is
registered with whatever the plan states about it, at minimum its name. What a
registered repository holds stays Repository
Management's decision, and Import has only the ability to ask.

The Import dialog also offers a "Target repository" field for the whole batch.
When it is filled in, it becomes the resolved repository for every entry that
names none of its own. An entry that carries its own `repo:` token keeps it.

### Re-importing an updated plan

```meta
type: sub-feature
related: [.devbook/domain/tasks/domain.md#task, .devbook/domain/tasks/domain.md#task-status]
```

Bring in a later version of a plan already imported once, and have it replace
that plan rather than add to it. What happens to each task the plan produced
depends on its status:

| The task's status | What the re-import does |
|---|---|
| `draft` or `ready`, so nobody has started it | Deletes it, then writes the new version's entry in its place |
| `in_progress` | Keeps it and brings it up to date from the new version |
| `done` or `archived` | Skips the whole entry, a restated status included |

The plan's id is what a task remembers about where it came from. A task carrying
the same tag that came from somewhere else was never part of the plan and is not
touched.

An `in_progress` task takes these from the new version:

- its instructions;
- its type;
- its dependencies;
- its target repository;
- its setup, devbook and manual sub-items.

Status moves only when the new version restates it. Otherwise the task keeps the progress
it has made. A plan silent on an entry's status is not asking for work under way to return to `draft`.

A `done` or `archived` task is left alone, as the settled record of what was done.

Clearing is by plan, not by what the new version mentions. A
not-yet-started task the plan has stopped describing goes with the
version that described it, which is how a plan drops work nobody began. Each entry
keeps the same id across versions, so a task already under way or
finished is recognized as the same entry. An entry id
the plan never carried is created. A task
replaced this way is a new task, so a dependency on it written from outside the
plan stops resolving.

## AI assistance over the visible backlog

```meta
type: feature
feature-flag: .devbook/domain/tasks/context.md#ai-assistant
related: [.devbook/domain/devbook/features.md#repository-devbook-areas, .devbook/domain/productivity/features.md#ai-productivity-tracking]
```

Ask questions about the work currently in view and get an answer grounded in it.
The answer comes from the tasks the active filters leave visible plus
the loaded backlog knowledge, not the entire backlog. Entries that were opened but never
edited are left out. The assembled context is capped so a large backlog
degrades into a partial answer rather than a failure. AI assistance is opt-in
and the product stays fully usable with it off.

## Archive and lifecycle

```meta
type: feature
related: [.devbook/domain/tasks/domain.md#task, .devbook/domain/tasks/requirements.md#archive-and-lifecycle]
```

Move tasks between active and archived states; archived tasks are excluded
from default views but always accessible and restorable.

Reopening a `done` task returns it to `in_progress`, whatever status it held
before. The earlier status is deliberately not kept, because reopening starts the work
again. [Task](domain.md#task) holds the lifecycle this follows.

Ticking a task off records that it is [Completed](domain.md#completed), a
separate fact from either status; that term says how the two differ. What the
checkbox and the status picker do to the tick and the status is in
[this feature's requirements](requirements.md#archive-and-lifecycle).

A task that moves to in progress is [Started](domain.md#started), shown as
`started:` on its metadata line beside `completed:`. The roadmap reads the two
together to draw a finished plan where its work ran. The detail panel
shows the day as a read-only **Started** badge beside the effort, on a task
that has one. The Board's In progress slot reads "Moves to In progress and
stamps Started" under "Drop to start", only when the task has no
stamp yet.

## Refresh from shared storage

```meta
type: feature
related: [.devbook/arc42/06-runtime-view.md#state-sync-and-webhook-forwarding, .devbook/domain/tasks/features.md#multi-device-sync, .devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md]
```

Deprecated, and removed from the product on 2026-09-16. Superseded by
[Multi-device sync](#multi-device-sync).

It refreshed the backlog when another process or device modified
the shared backlog file. Neither its setting in Settings →
Storage nor the check exists any more.

It was an interim mechanism that did not work. It assumed two machines could
share one backlog through a shared folder. That held while a task was its own
Markdown file and stopped when the store became a single binary
database. A file-sync product produced conflicted
copies of that database and silently reverted committed edits. Polling more
often did not help: the corruption was in the sharing, not in the staleness.
The sync itself now shows a synced change in the task list without reopening the
app.

## Multi-device sync

```meta
type: feature
related: [.devbook/domain/tasks/domain.md#task, .devbook/arc42/08-crosscutting-concepts.md#task-sync, .devbook/arc42/07-deployment-view.md#cloud-deployment-azure, .devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md, .devbook/domain/tasks/requirements.md#multi-device-sync, .devbook/domain/sync/features.md#syncing-between-devices]
depends-on: [.devbook/domain/tasks/features.md#task-creation]
```

Keep the same backlog on more than one of the person's machines, without either
giving up its own copy. Each device reads and writes its own store and
reconciles with the others through the sync service. The cloud carries a replica
of the tasks and is never the authority for them.

Working offline is the ordinary case. Every task can be
created, edited, completed and deleted with no connectivity, and connectivity only
lets the other machine find out sooner. The task list then shows what
arrived without reopening the app. A change to an entry being
edited waits until the editing is done rather than replacing
the text under the caret.

When the same task was edited in two places,
[the later edit wins whole](requirements.md#requirement-the-later-edit-wins-whole).
This deliberately loses the other edit. Merging field by field would need a
domain rule for reconciling two different priorities or statuses,
and the domain has none.

Both halves are built. In the [Sync](../sync/context.md) context,
[Pairing a device](../sync/features.md#pairing-a-device) gives each device its
identity, and the sync service refuses any call without a valid token. The
task change feed is pushed and pulled through the sync service, which
keeps devices, pairing codes and the task replica in Azure Cosmos DB. The
desktop's replica merge applies the later-edit-wins rule to each pulled copy; see
[Task Sync](../../arc42/08-crosscutting-concepts.md#task-sync).

## Backup to a repository

```meta
type: feature
related: [.devbook/domain/tasks/features.md#multi-device-sync, .devbook/domain/repository-management/features.md, .devbook/arc42/adr/0010-backup-is-the-database-committed-to-a-repository.md, .devbook/arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md]
```

Keep a copy of the backlog off the machine, by
committing it to a GitHub repository the person names. A backup is the whole
backlog as one file, the file the app reads, committed at a fixed
place. Every earlier backup is in the repository's
history, and the newest is always at the same address. A backup that would
commit exactly what the repository holds is skipped.

Backups run on a schedule, every day at a set time or every week on a set day
and time. **Back up now** on Settings → Storage &
backup runs one on demand. A scheduled backup missed while the app was closed is taken shortly after
the app next opens.

The page's headline answers whether the backlog is safe: *Backed up 3 h
ago — 7 daily copies kept.*, or *Not backed up yet.* It moves when a backup
completes while the page is open. The Backup card shows the same two figures,
**Last backup** and **Keeping n daily copies**. A line under them says whether the
last backup committed anything and when the next one is due. A daily copy is a
day on which this machine committed a new copy. Every commit stays in the
repository's history, so the count only grows.

A backup is one-way and never changes the backlog on the machine:
getting a backup back is a decision the person carries out by hand. That keeps a backup from
becoming the shared-folder arrangement that
[Refresh from shared storage](#refresh-from-shared-storage) records the cost of.
It is not [Multi-device sync](#multi-device-sync): a backup leaves and stays behind, while sync is a live copy that comes back.

## Roadmap planning

```meta
type: feature
depends-on: [.devbook/domain/tasks/features.md#refinement-and-prioritization]
related: [.devbook/domain/roadmap/features.md, .devbook/domain/roadmap/domain.md#roadmap-plan, .devbook/domain/tasks/domain.md#task]
```

**Superseded.** Roadmap planning is a bounded context of its own — see
[Roadmap Planning](../roadmap/features.md). This chapter is kept, at this heading,
because other chapters reference it; it is not the model any more.

This chapter said the roadmap groups *selected backlog tasks* and derives its
progress from them. The first half was too narrow: a plan has to hold work not yet refined
into a task, which is most of planning. The plan is stored
in its own right, and a planned item may *optionally* name the task that
executes it.

The second half survives: **Task remains the source of truth for a work item's
status and execution priority.** Roadmap Planning owns planning priority and
sequence, never writes a task's status or priority, and reads a linked item's progress from the task.

What Tasks keeps from this feature is the
[refinement and prioritization](#refinement-and-prioritization) that makes
a task ready in the first place, and the
[Partnership link](../roadmap/dependencies.md) that lets a plan point at it.

### Roadmap views

```meta
type: sub-feature
related: [.devbook/domain/roadmap/features.md#reading-and-rescheduling-on-a-timeline]
```

**Superseded** by
[reading and rescheduling on a timeline](../roadmap/features.md#reading-and-rescheduling-on-a-timeline)
and
[reading the plan by repository](../roadmap/features.md#reading-the-plan-by-repository)
in Roadmap Planning. Now/Next/Later is not how the plan is read. A horizon is a
grouping, and the plan is read against dates, in repository bands with the
person's own lanes inside them.