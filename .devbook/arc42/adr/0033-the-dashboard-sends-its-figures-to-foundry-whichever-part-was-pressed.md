# ADR 0033: The Dashboard sends its figures to Foundry, whichever part was pressed

```meta
date: 2026-10-16
related: [".devbook/arc42/adr/0023-inbox-items-may-be-read-by-the-foundry-model.md", ".devbook/arc42/05-building-block-view.md#desktop-app", ".devbook/domain/productivity/features.md", ".devbook/domain/productivity/requirements.md"]
```

When the reader asks the assistant a question with the Dashboard in scope, the
body sent to the configured Azure Foundry deployment holds one line of figures
for every part on every tab, as well as the merged pull requests and closed
issues behind them. The figures are pinned, so they are always sent. They are
the same whichever part's Ask AI was pressed, and whether the panel was opened
from a part at all. Which tab is on show and which part was pressed never go
into the body. The part reaches the model only through the question the reader
sends.

## Status

```meta
```

Accepted, 2026-10-16, by the repository owner at the Personal Validation gate
of the run that drafted it. Proposed 2026-10-15. The decision was already built:
PR #1316 (merge `9a87218c`) widened `DashboardAiContentSource` and added
`DashboardAiFigures`, and PR #1322 recorded the feature in
`domain/productivity/`. The repository owner asked for this record at that run's
Personal Validation, because the change widens what leaves the machine.

A **local** decision, numbered in the local sequence. Every bare ADR number below
means the local one.

## Context

```meta
```

**Ask AI sends an area's content, never the screen.** Every area answers
`IAiContentSource` (`Backlog.SharedKernel.Ai`). On a send, the shell asks the
area in scope for a body within `AiContentBudget.DefaultCharacters` (6,000
characters, about 1,500 tokens) and passes the body and the question to
`AzureFoundryChat`. The port's contract keeps filter text, sort, layout and
selection out of the body. Only the open record may be pinned, and the scope
that defines the area's records is stated on the first line.

**The Dashboard used to send facts only.** Before #1316 its body held the merged
pull requests and closed issues in the repositories and window on screen:
number, title, repository, date, review rounds, rework, size. The reasoning was
that a score is the dashboard's reading of the pull requests, and the assistant
should see what lies behind it.

**That left most charts unanswerable.** Plan, sessions, hours worked and spend
are not in any pull request. A question like "am I on pace" or "why is spend up"
had nothing to answer from. #1316 also gave every part an Ask AI control, which
begins the question `About the "<part>" chart: `. That invites exactly these
questions.

## Decision

```meta
```

### 1. What is sent

```meta
```

One line per insight, in the tabs' order. Each line is worded through the same
formatters the part uses, so an answer quotes what the reader can see:

| Line | Holds |
|---|---|
| Throughput | Pull requests merged, issues closed, the rework rate, the median first-review time, the median commits per pull request |
| Score | The volume and quality scores, and the target with the window it came from |
| Rework after review | Churned pull requests, commits after review, force pushes, files touched again, review rounds, changes requested, conflicted syncs |
| Tasks | Tasks completed, story points done, tasks completed without an estimate |
| Plan | The headline, the pace in use, items by outlook, points still to do, and up to five late roadmap items **by title** |
| Assistant sessions | The headline, time waiting on the reader, prompts per session, most sessions at once, usage limits hit |
| Hours worked | Hours worked, inside and outside office hours, and planned |
| Devbook drift | The verdict headline, open drift issues, and up to five of their subjects |
| Spend this month | Each provider's spend and projection, any provider projected past its budget, and whether it is an estimate |
| Spend by model | Up to five models, each with its cost and tokens |
| Needs you | Up to five items, each with its title and detail, and any rule that could not be checked |

Every list inside a line stops at five names (`DashboardAiFigures.ListLimit`)
and says how many more there are. An insight that cannot answer keeps its line
and gives the reason.

After the figures come the merged pull requests and closed issues, as before.
They are ranked against the question and fill whatever budget is left.

Not sent: the weekly brief's sentences, which repeat the figures; the tab on
show; the part that was pressed; the dashboard's layout. No token, setting or
path is sent.

### 2. The figures are pinned

```meta
```

The figures are handed to `AiContentBudget.Select` as pinned records. They are
charged before any pull request, and no wording of a question can rank one out.
The first line counts only the pull requests and issues. The line under it says
how many figure lines lead the body and that they are not entries, so the model
never reports a figure as a pull request.

### 3. Whichever part was pressed

```meta
```

The body is the whole dashboard, for every question. Ask AI on a part changes
only the question: `DashboardPane.OnAskAi` hands the part's title to the shell,
which begins the question with it. The part travels in the reader's own words,
which they can edit or delete before sending. Which tab is open is screen state,
and `IAiContentSource` keeps screen state out of the body.

### 4. When

```meta
```

Only when the reader sends a question with the Dashboard as the scope, with the
assistant switched on and Foundry configured. Pressing a part's Ask AI opens the
panel and begins the question; it sends nothing. Nothing is sent in the
background.

## Consequences

```meta
```

- **More of the reader's own data leaves the machine.** Before, a Dashboard
  question sent only what GitHub already holds. Now it also sends data that
  exists only on this machine or in the reader's provider accounts: spend per model, tokens, hours worked against
  office hours, assistant-session totals, roadmap item titles, drift issue
  subjects and Needs-you items. The reader accepts that by configuring Foundry
  and switching the assistant on, as with the Inbox in ADR 0023. An unconfigured
  app sends nothing.
- **Every Dashboard question pays a fixed price.** Eleven pinned lines are
  charged first on every send, whatever the question. The 6,000-character budget
  is unchanged, so the pull requests get what is left. A dashboard with long late
  item titles, models or Needs-you details leaves room for fewer pull requests.
  The five-name cap keeps that price from growing with the backlog. A figure line
  that gains a list must cap it the same way.
- **Two questions about the same window see the same figures.** The lines come
  from the scoped insights the parts read, out of the same caches, so an answer
  cannot disagree with the chart it is about. Spend is read for the calendar
  month and names the month on its line, because no provider reports it per
  repository, machine or window.
- **A new dashboard part owes a figure line.** A part without one is a chart its
  own Ask AI cannot answer about. Adding one widens what is sent, and this record
  is where that is accepted.

## Rejected

```meta
```

- **Facts only**, as before #1316. That keeps the body to GitHub data, but it
  leaves the model with nothing to answer from about the plan, sessions, hours or
  spend, the charts most of the new Ask AI controls sit on. A question about them
  would be answered from guesswork or not at all.
- **Only the tab on show**, or only the part pressed. That is a smaller body, and
  the pull requests would get more of the budget. But which tab is open is screen
  state, and the port forbids it in the body. The same question would get
  different answers depending on the tab. The Overview reads across the tabs
  anyway, and a question about rework is often answered by the plan or the spend.
- **Ranking the figures with the pull requests** instead of pinning them. A
  question that shares no words with a figure's line would drop it. The model
  would then read the missing line as nothing to report.
