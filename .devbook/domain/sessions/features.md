# Sessions

```meta
type: features
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Session inventory

```meta
type: feature
related: [.devbook/domain/sessions/domain.md#session-log]
```

Answer "what have my agents been doing here" as one list: every session the
environment has a record of, most recently active first, whichever agent ran it.

One list rather than one per agent, because the question is about the work and not
about the vendor. A person who has both agents installed does not think in two
inventories.

### Live and past sessions together

```meta
type: sub-feature
related: [.devbook/domain/sessions/features.md#open-on-the-live-sessions]
```

A session that is running now and a session that finished last week are rows in the
same list, distinguished by their state rather than by which list they are in. Two
lists would force the reader to know which one to look in before knowing whether the
session had ended — which is usually the thing they are trying to find out.

The list **opens on part of itself**, and that is a smaller concession than splitting
it. One list narrowed by a control is still one list: the rows left out are one press
away under the same heading, the count beside the title names both numbers whenever
any are being left out, and the empty state the narrowing can produce says which
states it kept and how many sessions are on the other side. A reader looking for a
session has one place to look and one control to move.

What two lists would have cost is exactly what none of that costs. A finished session
is never *somewhere else* — only not currently shown, by a choice the reader can see
they made.

### Open on the live sessions

```meta
type: sub-feature
related: [.devbook/domain/sessions/domain.md#session-view, .devbook/domain/sessions/features.md#sessions-from-another-machine, .devbook/domain/sessions/domain.md#session-view]
```

The list opens on the sessions that are still going, and a `Live` / `All` choice sits
first in the row of controls above it — before the grouping, because it decides which
rows exist before the grouping decides where they sit. "What is going on right now" is
the question somebody opens this for, and several hundred finished records are the
answer to a different one.

Live means running **or** stalled, both. Stalled is computed from silence rather than
declared by the agent — nothing writes "I have stopped" — so a stalled session is one
nobody can show has ended, and a default that dropped it would hide the row most worth
looking at: the one that may be sitting there waiting on the reader.

The choice is not remembered. Every open starts on `Live`, the way the grouping starts
at `Nothing`, because a reader should not have to remember having already answered
"what is going on right now" — and a view silently restored from last week is how a
reader concludes the product has lost their sessions.

**The default is only as good as the evidence under it, and for one agent that is
thin.** Claude leaves a record per running session, so its live rows are evidence.
Copilot leaves no liveness marker at all: its sessions read as running while their
record is fresh and as finished once it has been quiet for longer than the
`Stale Threshold`, and they never read as stalled. A Copilot session that is genuinely
running but has said nothing for half an hour therefore falls out of this view. That is
a limit of what Copilot writes down rather than a rule this product wanted, and it is
why the way back to `All` is named in the empty state instead of left to be discovered.

A session that arrived from another machine is thin in the same way and for a
different reason — a record carries no liveness marker whichever agent wrote it —
so it too falls out of this view once it has been quiet long enough. See
`.devbook/domain/sessions/features.md#sessions-from-another-machine`.

### Only what the agent recorded

```meta
type: sub-feature
related: [.devbook/domain/sessions/domain.md#working-location]
```

Each session shows what its own agent wrote down and no more. The agents disagree
about one field of the two — Copilot names the repository and the branch, Claude
names the branch and never the repository — so a Claude row shows its branch and
reads "not recorded" where its repository would be, visibly rather than blankly,
while a Copilot row shows both.

The alternative was inferring the missing repository from the working folder. A
wrong repository attributed to a session renders exactly as convincingly as a right
one, which is what makes inference the more expensive option.

What a row may show in that cell instead, as of 2026-09-22, is the **resolved
repository** — marked as resolved, with the cell's title saying in words that the
agent recorded none and the folder was placed inside a registered clone. This is
not the inference above: it is not read off the path but looked up against the
clone directory the person registered (`.devbook/domain/sessions/domain.md#working-location`).
A recorded repository always outranks it in the cell, and a session with neither
still reads "not recorded". The repository scope in the shell's header admits a
session by either — which is what makes a scoped list able to hold the Claude
sessions running in that repository's clone — and its narrowing sentence names
both terms, so a Claude session missing from a scoped list is understood as one
outside every registered clone rather than as one that never ran.

### What a session cost and what it shipped

```meta
type: sub-feature
related: [.devbook/domain/sessions/features.md#the-work-a-run-is-linked-to, .devbook/domain/productivity/features.md#what-the-sessions-cost-and-shipped]
```

As of 2026-09-23 a row also shows three things Claude writes into its transcript and
the session record keeps: **the pull requests the session linked itself to**, drawn
exactly as a run's are — "PR #n", opening on GitHub — **its tokens**, output and input
with every model and the cache figures in the cell's title, and **where it was run
from**, a quiet badge beside the type ("desktop", "cli").

A pull request the row's own run already draws is not drawn again: one piece of work
is one reference on screen. The tokens are the session's own spend, and the title
says the agents it spawned are not in them. Copilot records none of the three, and a
row that could not say shows nothing or an em dash — never a zero, which would be a
claim that the session linked nothing or spent nothing.

### Sessions from another machine

```meta
type: sub-feature
related: [.devbook/domain/sessions/domain.md#session-log, .devbook/domain/sessions/features.md#group-by-environment, .devbook/arc42/08-crosscutting-concepts.md#session-record-sync, .devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md]
```

A session that ran on one of the person's machines can be read on another. The
record travels and the work stays where it happened, and what arrives joins the
same list rather than a panel of its own — somebody running agents on a desktop
and a laptop has one set of sessions on two boxes, not two inventories to check
in turn.

Off until it is switched on, and switched on separately from anything else that
syncs. A record of what the assistants have been doing is the kind of thing a
person agrees to let off a machine rather than finds out has left it, and wanting
the same backlog on two machines is a different wish from wanting that — so it is
a switch of its own, and it starts off. Until it is on, this is the list it has
always been: everything read here and nothing else.

**A row from elsewhere says less than a row read here, and visibly so.** Only the
whitelisted metadata travelled — which agent, which machine, which repository and
branch, when the session was alive, how much of it there was, its title, and the
usage limits it was refused by — so such a row has no working folder to show. That
is not a gap waiting to be closed: a folder names a disk the reading machine cannot
see. The row still carries the delivery runs filed under that folder, matched on
the worktree key its record carried. A record from a device that predates the title
is headed by its session id, an identifier that plainly reads as one.

**A session of this machine's outlives its transcript.** The assistant cleans its
transcripts away after a month by default; this machine keeps its own record of every
session it has read, so the session stays in the list — titled, with its folder,
dated, measured and with its refusals — after the files are gone, with or without
sync. While the transcript is there the row is read from it, and the record is never
shown beside it. A row answered from the record says so under its folder, in the
origin line's quiet register: the transcript is gone, and what it shows is what the
last reading kept. The pane amends the records of the sessions that moved each time it
opens; **Update records**, beside Refresh, amends every record the files still hold —
adding to each, never replacing it — says how many it amended and started, and with
sync on sends every record again so the other machines are amended too.

**And it reads as finished once it goes quiet, sooner than the truth may be.** A
record carries no liveness marker, so a session nobody has heard from for longer
than the `Stale Threshold` reads as over — the same treatment Copilot's sessions
already get here, for the same reason and with the same cost. A session still
running on the other machine can therefore drop out of the live view between one
exchange and the next. Reading it as stalled instead would be the worse answer:
stalled says something is still there and quiet, and a record cannot tell that
from a session that ended a month ago.

### Re-read on request

```meta
type: sub-feature
```

The list is read when it is opened and again when the reader asks for it. Nothing
polls.

A surface that refreshed itself would be claiming to be live, and the evidence does
not support the claim: one of the two agents leaves no liveness marker at all, so a
self-moving list would move without meaning.

Records arriving from another machine do not change that, and the distinction is
worth keeping straight. Those arrive on a schedule of their own — a machine cannot
be asked for its sessions at the moment somebody looks at them — but they arrive
into what this environment has a record of, which is the thing the list reads. The
reading is still the reader's: nothing appears on screen until they open the list
or ask for it again, so what moves under them is never the rows they are looking
at.

## Session grouping

```meta
type: feature
related: [.devbook/domain/sessions/domain.md#session-grouping]
```

Carve the same list up by the two things that separate sessions from each other in
practice, without removing any of them.

### Group by environment

```meta
type: sub-feature
related: [.devbook/domain/sessions/domain.md#environment, .devbook/domain/sessions/features.md#sessions-from-another-machine, .devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md]
```

A section per environment, named after it, with its own count — so "how many are
running on that box" is read rather than counted.

It carves something now. For as long as an environment could only read its own
records this was one section named after the box the reader was sitting at; a
reader whose machines exchange session records gets a section per machine that
has reported. Which section a row lands in is settled by the environment that
gathered it and never by the one showing it, so a session sits under the machine
that ran it wherever it is being read.

A section for a machine the reader is not sitting at is as fresh as that
machine's last exchange and no fresher, and that costs something real rather than
nothing: a session started since is not in the section at all, and the count on
it therefore answers for what has arrived rather than for what is happening over
there. What the section does not do is lie about the sessions it has. Their state
is derived against the reader's own clock from the timestamps that travelled, so
a row ages honestly on this side instead of repeating the other machine's opinion
of it — which is why the staleness needs naming here and not a caveat on every
row.

### Group by agent

```meta
type: sub-feature
```

A section per agent, so Claude's sessions and Copilot's can be read separately
without the two being separate lists.

### Grouping never hides a session

```meta
type: sub-feature
related: [.devbook/domain/sessions/features.md#open-on-the-live-sessions]
```

The total is the same whichever grouping is chosen, and the surface keeps that total
on screen beside the title, above the controls that act on the list — so a grouping
can be trusted not to be a filter in disguise.

That count is what carries the guarantee, and it answers to the view and never to the
grouping: it does not move while `Group by` moves the same rows around. When the view
is leaving some of them out the count names both numbers — kept and total — so the
total is still there to be checked against, with the reader's own subtraction stated
beside it rather than folded into it. One number where two are owed would let the only
control that removes rows borrow the innocence of the ones that do not.

## Honest partial answers

```meta
type: feature
related: [.devbook/domain/sessions/domain.md#session-catalog]
```

Say what could not be read and what was left out, rather than presenting whatever
arrived as the whole picture.

### Name the source that could not be read

```meta
type: sub-feature
```

When one agent's records cannot be read — never installed, or not readable by this
user — the other agent's sessions are still shown and the unreadable one is named.

"No Copilot sessions" and "Copilot could not be read" are different facts, and only
one of them is worth investigating.

### Say how much was left out

```meta
type: sub-feature
related: [.devbook/domain/sessions/domain.md#session-limit]
```

An environment can hold hundreds of session records — enough that reading all of
them costs real time and showing all of them buries the handful that are running. A
reading for a list therefore describes the most recent sessions per agent, and when
it does, it states how many exist.

Per agent rather than overall, so the agent that happens to keep more history cannot
crowd the other one out of a list whose whole point is showing both.

A reading for a count takes the other shape: everything since a horizon, with no
limit. A count over the most recent hundred per agent is the limit wearing a
total's clothes — every busy machine reads "200" — so a consumer that counts asks
since a horizon and gets every session inside it, and every source keeps records at
least as far back as the longest horizon a consumer can ask for
(`.devbook/domain/sessions/domain.md#session-history`). A source that cannot reach the
horizon it was asked still says so.

## Delivery runs beside their sessions

```meta
type: feature
depends-on: [.devbook/domain/sessions/features.md#session-inventory]
related: [.devbook/domain/sessions/domain.md#delivery-run, .devbook/domain/sessions/domain.md#run-attachment]
```

Show the orchestrated delivery runs an environment's dashboards recorded — which skill
ran, what it was called, how it ended, how much it cost — in the same list as the
sessions, because a run is a piece of work a session was doing and a reader asking
"what has been going on here" wants both answers in one place.

**One list, whichever side arrived first.** A session record and a run file describe
the same piece of work from two sides, and either can be the one this environment
still holds: the run's session may have aged out of the reading, or the run may belong
to a session read on another machine. So the row is the unit of the list, not the
session — a row is a session with the runs it drove, or a run standing on its own —
and the reader never has to know which table to look in.

The dashboards keep a run's file outside the repository, keyed by the worktree it ran
in, and the file says nothing about which session drove it. What this context adds is
the matching: the same worktree key derived from a session's folder, and the window
the two were open together.

A run does not have to come from a dashboard. A session can report to this product
directly, and then the run is recorded here as it happens rather than found afterwards
— in the same shape, in the same list, on the same terms. See
[Record a run as it happens](#record-a-run-as-it-happens).

### Under the session that drove it

```meta
type: sub-feature
related: [.devbook/domain/sessions/domain.md#run-attachment]
```

A run filed under the worktree a listed session ran in, and open while that session
was active, is shown under that session's own row and across every column of the
list, stacked the way a reader asks: the work it is linked to, the dashboard's status
under it, one line of figures — stages done, output tokens, the context peak, the tool
calls — and the skill that owned it last. The stages, the token buckets, the gauge,
the tool activity by category and by MCP server, and the run's own title are behind a
fold.

Under the row rather than in a column, because a run is not a property of a session;
across every column rather than inside the name cell, because a quarter of the width
turned each of its parts into three lines while the rest of the row sat empty.

On the session's row rather than in a column, because the session is the process and
the run is the work it was tracking; and behind a fold, because a run holds ten stages
and tens of thousands of tool calls, and a row that showed them would be a report with
a table around it.

### The work a run is linked to

```meta
type: sub-feature
related: [.devbook/domain/sessions/domain.md#delivery-run-reference]
```

A run names what it was for and what it produced, and both are shown on its line: the
tracker issue it was working and the pull requests it opened, each as a reference of
the kind this product draws for work that lives elsewhere, one per line, opening where
it lives.

The Backlog entry it was started from is not a reference beside them — **the run's own
status is how a reader reaches it**, because the entry is where this product tracks
the work and the status is the run's answer about that work. Pressing it hands the
entry back to whatever holds the task list, which shows and selects it; a run that
names no entry leaves the status a plain word.

The run's own title is not shown on the line at all. A dashboard names a run after the
work it was doing, so beside a reference to that work the title is the same sentence
twice; it stays in the fold for a reader who wants the run's own words.

Three sources, because a run file records them in three places — its own tracker
field, the links its stages kept, the plan item in the prompt it was started with —
and one item named twice is one reference. No state travels with them: this
environment has not asked the tracker how the item is doing, and saying otherwise
would be inventing a reading.

### A row of its own in the same list

```meta
type: sub-feature
related: [.devbook/domain/sessions/features.md#say-how-much-was-left-out]
```

A run no listed session ran in is a row of the same list — named after its worktree,
dated by the run, on the machine its file was read on, with a line saying that no
session record backs it and which dashboard does — rather than dropped, or dressed as
a session, or put in a table of its own.

It answers every control the sessions answer: grouped by machine and by agent beside
them, narrowed by the machine filter, and out of the live view — with no liveness
evidence a row is Finished, and a session still running would be in the reading and
the run would be on its row.

### Only the rows with a run

```meta
type: sub-feature
related: [.devbook/domain/sessions/features.md#open-on-the-live-sessions]
```

One toggle narrows the list to the rows that carry a delivery run — a session that
drove one, or a run standing on its own — for the reader who came for the work rather
than for the sessions. A filter like the live view: the count says what it hid, it
composes with the view and the machine filter, and it is off again on every open.

### Only what the dashboard recorded

```meta
type: sub-feature
related: [.devbook/domain/sessions/features.md#name-the-source-that-could-not-be-read]
```

A status word is shown as the dashboard wrote it, made readable but never mapped onto
a smaller vocabulary; a figure the file does not carry is left out rather than shown
as zero; a stage the writer lost the name of is numbered rather than named after the
writer's missing value. A run file cut off mid-write costs that run and nothing else,
and a dashboard folder that cannot be read is named beside the agents that cannot be,
in the same sentence.

## Record a run as it happens

```meta
type: feature
depends-on: [.devbook/domain/sessions/features.md#delivery-runs-beside-their-sessions]
related: [.devbook/domain/sessions/domain.md#delivery-run-recording, .devbook/arc42/adr/0012-backlog-is-an-mcp-server-inside-the-desktop-app.md]
```

Let a session report its delivery run to this product while it runs, so the pane shows
the work in progress instead of a folder somebody else's tool wrote and this product
found later. A reader watching a run land here sees the same row, with the same
figures and the same stages, as one imported from a dashboard — because it is the same
shape, written by this product rather than read from another.

This closes the gap the area was built around. Until now every run in the list was
somebody else's record: the list could say what had happened, never what was
happening, and a session with no dashboard installed left no run at all. The
difference a reader notices is that a row appears when the work starts rather than
when the reader next refreshes something that had already finished.

**Asking to see the runs brings the pane forward, and answers with the application.**
A session that reports here can ask for the surface, and what it gets back is what the
window did — showing the pane, or not showing it because the area is switched off, or
nothing to show because no window is open. Never an address: this product is the
application the session is already talking to, and handing back a link would open a
second window onto the thing it is holding.

### Picking a run back up

```meta
type: sub-feature
related: [.devbook/domain/sessions/domain.md#delivery-run-recording]
```

A session that resumes work already under way continues the run it left rather than
starting a second one beside it, and is told that is what happened so it can carry on
from the first stage that is not done. Two rows for one piece of work would be the
same work counted twice, with its stages split between them — and a reader has no way
to tell that from two genuine runs.

### What a run cost, reported by the session itself

```meta
type: sub-feature
related: [.devbook/domain/sessions/domain.md#delivery-run-telemetry, .devbook/domain/sessions/features.md#only-what-the-dashboard-recorded]
```

A run recorded here shows what it cost the way a dashboard's run does: the tool calls
it made by kind and by MCP server, the agents it delegated to, the tokens it spent —
in total, delegated, and per stage — and how full its context window got. The session
reports these itself, from the moments its own tools run, rather than the run
claiming them: a tool call arriving here is a call, not the session that made it, so
the figures come from the session's side or not at all.

A session reports only to a run it is driving. One that has no run under way reports
nothing, and a run started later in it does not count the conversation that came
before it; a run another session is driving is left alone. A session that does not
report — the plugin not installed, or a host other than Claude Code — leaves its run
without these figures, and they are left out rather than shown as zero: the rule this
area already applies to a dashboard's own gaps.

## Session activity enrichment

```meta
type: feature
status: proposed
depends-on: [.devbook/domain/sessions/features.md#session-inventory]
related: [.devbook/domain/sessions/domain.md#session-activity-publishing, .devbook/domain/sessions/domain.md#session-activity-enrichment]
```

Layer externally reported session activity onto the same session list, so a reader can
see what a session has been doing without replacing the locally read record that says
the session existed.

The extra layer is optional by design. A session with no Collections MCP reporting is
still read, grouped, and counted exactly as it is today; a session with reporting adds
timeline and correlation detail to that same row.

### Optional Collections MCP reporting

```meta
type: sub-feature
status: proposed
related: [.devbook/domain/sessions/domain.md#session-activity-publishing, .devbook/domain/sessions/domain.md#collections-mcp]
```

When a session has the Collections MCP configured, it reports start, meaningful
activity, and finish updates to that MCP. When it does not, nothing about the session
surface breaks or changes shape.

Capability detection is ordinary product behavior rather than setup drama: missing
configuration is simply `disabled`, not an error that needs a banner.

### Latest meaningful activity

```meta
type: sub-feature
status: proposed
related: [.devbook/domain/sessions/domain.md#session-enrichment-summary, .devbook/domain/sessions/domain.md#session-activity-entry]
```

Show the latest human-readable activity summary, when it arrived, and any safe
correlation facts that help a person place the session — for example the orchestration
run or issue it was working against.

Meaningful rather than exhaustive. The point is to answer "what is this session doing"
without turning the session list into a transcript or a tool log.

### Reporting degrades honestly

```meta
type: sub-feature
status: proposed
related: [.devbook/domain/sessions/domain.md#reporting-capability]
```

When reporting is configured but the Collections MCP cannot be reached, the session
still appears and the reporting path reads as degraded.

That answer is more honest than either hiding the reporting layer or pretending no
external activity exists. A broken path is a fact worth surfacing, and it is separate
from the session's own `Session State`.

## Turn the area off

```meta
type: feature
```

The whole area can be switched off, and when it is there is no way in and nothing to
find — not a disabled control and not an empty surface.

One switch for the area rather than one per column or per grouping, because "should
this product show me sessions at all" is a single question.
