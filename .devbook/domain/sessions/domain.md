# Sessions

```meta
type: domain
```

> One chapter per Aggregate, Domain Service, Domain Event, or Shared Value
> Objects / Shared Enums grouping in this bounded context; each chapter's
> `type` records which of those it is. An Aggregate's owned Entities, Value
> Objects, and Enums are chapters directly beneath it, typed `entity`,
> `value-object`, and `enum`. Value Objects/Enums shared across multiple
> aggregates get their own chapter at the end instead of being duplicated.

## Session Log

```meta
type: aggregate
related: [.devbook/domain/dev-pc-management/domain.md#machine-registry, .devbook/domain/sessions/dependencies.md, .devbook/arc42/08-crosscutting-concepts.md#session-record-sync, .devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md]
```

Everything one environment can say about the agent sessions it has a record of.

The consistency boundary is **one environment**, not one fleet. An environment is
the only thing that can read its own agents' records, so a log that spanned several
would assert facts nobody had gathered. A view over many environments is a
composition of several Session Logs. That is why grouping by environment is a
derivation rather than a structural relationship.

Invariants:

- A session appears **once**, however many records the environment holds for it.
  An agent may leave one record per process and another per transcript. It may also
  file the same transcript under two working folders when a session moved between
  them: the session went somewhere, it did not become two. All of that is evidence
  of one session. The most recent record describes it, because it is the one the
  agent went on writing. The collapse happens before anything counts or truncates,
  so the number an environment reports is a number of sessions, never of files.
- A session's `Session State` is **derived from the evidence available**, never
  asserted. Running and Stalled both require liveness evidence; with none, a session
  is Finished. A state no evidence supports is not reported at all. There is no
  Failed, because a record that stops says nothing about why.
- The log **never fills a gap the agent left**. A fact an agent did not record is
  absent, not inferred. A repository guessed from a folder path looks exactly like a
  recorded one, and is wrong.
- The log **says how complete it is**. It may describe fewer sessions than exist,
  and then how many exist is part of the answer. That completeness belongs to the
  reading alone. The reader's chosen `Session View` removes rows after the log has
  answered, and is accounted for separately. One number for both subtractions would
  have the log confess to a shortfall the reader asked for.

**Records replicate, and none of the above changes when they do.** Local ADR 0005
puts session records in the same cloud replica as tasks. So a record gathered on one
machine can be read on another. The path is built. It is off until switched on, and
it has not yet run against provisioned infrastructure. Replication decides whether
another machine's sessions are in front of a reader. It decides nothing about what
one of them means. Every invariant survives, because the thing that replicates is a
record, not an authority:

- **Still single-writer.** A session ran on one environment, and only that
  environment holds the evidence for it, so only that environment writes its
  records. There is nothing to reconcile: no second version, no last-write-wins, and
  no lost edit. A session that moves gets a later record rather than an edit to an
  earlier one. None of that rests on good behaviour. A record is addressed first by
  the environment that gathered it, so an environment can only ever write records of
  its own. On the reading side, a reading knows how it came by each record: read
  here, or arrived from elsewhere. It offers back only what it gathered itself. An
  environment that re-published another's records would attach somebody else's work
  to itself, which is how a single-writer rule fails without anyone editing anything.
- **Still one environment per log.** A reading that spans machines is a composition
  of several Session Logs, as grouping by environment already is. A replicated record
  names the environment that gathered it, so nothing asserts a fact nobody gathered.
- **Still derived, never asserted.** `Session State` is worked out on every reading,
  from the evidence that reached the reader, and never travels on the record. A state
  on the wire would freeze the gathering machine's reading of its own clock. It would
  go on saying `running` about a session that ended before the last exchange. A
  record from another machine is stale rather than wrong, and the derivation says so
  from the timestamps it has. A record carries no liveness marker: the same record
  arrives whether the session is still going or ended a month ago. So silence past
  the `Stale Threshold` reads `finished` and never `stalled`. That is the rule this
  context already applies locally to an agent that leaves no marker. `stalled` claims
  that something is still there and quiet, and nothing in a record can tell that
  apart from something that is gone.
- **Only a whitelist travels.** Local ADR 0005 fixes it, in the table below. No
  prompt, tool output or file content ever travels, which is the whole reason a
  record can leave the machine at all.
- **The list carries this context's identity in full.** Local ADR 0005 settled that
  in the whitelist rather than in the pushing code. `Session Identity` is `agent`
  plus `session_id`, never `session_id` alone, because two agents may issue the same
  string. A record without its agent would let the receiving log merge two unrelated
  sessions, which is the failure the identity rule exists to prevent. The replica
  keys a record on the environment, the agent and the session id together. So two
  unrelated sessions have no one key under which they could arrive.
- **Retention is the store's, not this context's.** A replicated record expires
  after twelve months by container TTL. Nothing here reaps or deletes a record.
- **An environment keeps a [Session Record](#session-record) of each of its own
  sessions.** The record answers once the transcript is gone. The assistant
  cleans its transcripts away; Claude does so after a month by default. A session
  read from nothing is a session the log no longer holds. So every reading amends the
  local record, whether sync is on or not. A record is added to and never taken
  from:
  - a value the reading lacks keeps what the record held;
  - earlier runs and waits are kept;
  - limit hits are a union.

  The answer for one session comes from the files as read now, then from this
  environment's record, then from what sync carried. With sync on, the records an
  environment pushed also come back down the feed, and are kept as a second copy. A
  record read back is never pushed again, so this does not make the log
  multi-writer. Those copies show under the environment's own identity rather than
  the machine id the sync service issued.

| Field on the wire | Why it travels, and on what terms |
| --- | --- |
| agent and session id | The `Session Identity` in full. |
| environment id | Stamped by the sync service from the sender's token, never sent by the device. |
| environment name | The name that environment goes by. |
| repository alias | The alias, never the working folder. |
| resolved repository alias | On the same terms ([Working Location](#working-location)). |
| branch | As recorded. |
| started at, last activity at | The `Activity Window`. |
| turn count | Absent where the agent records none. Zero would be a claim about the session, where absence is the truth about the record. |
| duration | Carried for the transport only. A reading derives it from the two timestamps, so it cannot disagree with them. |
| runs and waits | [Activity runs](#activity-run) and [waits](#wait) as pairs of timestamps, so a reading machine can measure a session whose transcript it never held. |
| human turns | The instant of each [human turn](#human-turn), never its text (local ADR 0019). Roadmap Planning counts the person's hours from them on every paired machine. |
| title | By the owner's decision of 2026-09-23, although the agent writes it from what the person typed. A session is recognised by its title, on another machine and after its transcript is gone. A record from a device older than the field is headed by its session id. |
| worktree key | The folder's leaf and a one-way hash, so a record still meets the delivery runs filed under that folder. |
| usage-limit refusals | When, which bucket, when it resets, and what it said about paid overage, never the refusal's sentence. A usage limit is the account's, so a refusal on one machine bounds the week on every other. |
| where the agent was run from | Such as the desktop app or the command line. |
| pull requests the session linked | Repository, number and address. |
| tokens per model | Counts, never content. |

A field not in the table does not travel. So `working_folder` does not: it describes
one machine's disk and means nothing on the other.

Runs, waits and human turns are instants, the same kind of fact as the activity
window. A record keeps three cases apart, and a reader never reads an absence as a
measurement of zero:

- a list of intervals;
- an empty list, where the agent's own format cannot mark a boundary;
- nothing, where the gathering environment had no parsable activity record.

**Replication is not the Collections MCP and changes nothing about it.** The
[Collections MCP](#collections-mcp) stays optional, reached through an
anti-corruption layer, and never authoritative for whether a session existed. It
supplies a second kind of evidence about a session this context already knows.
Replication moves records this context already holds. Neither stands in for the
other. A replicated record with no activity stream is a complete record. It may
carry the runs and waits its gathering environment folded, which measure the
session's time and are not a stream of what it did.

### Agent Session

```meta
type: entity
aliases: [AgentSession, session, session_id]
```

One working session of one agent. Identified by `Session Identity`, the agent plus
the identifier that agent gave the session, because a session id is only unique
within the agent that issued it.

Holds the [environment](#environment) it ran on, as its display name and its
`EnvironmentId`. It also holds a display title, its `Working Location`, its
`Activity Window`, and its `Session State`. A machine dimension keys on
`EnvironmentId`, never on the display name, because the name can change while the
session stays on the same machine. When external activity evidence exists, the
entity also carries a `Session Activity Stream`, a `Session Enrichment Summary`, and
a `Reporting Capability` reading for that stream.

Its lifecycle is not this context's to run. It appears when an agent first leaves a
record, and it stops changing when the agent stops writing.

### Session Identity

```meta
type: value-object
aliases: [SessionIdentity]
```

What makes a session that session: `agent` plus `session_id`. Equality by both.
Never by `session_id` alone: two agents may issue the same string, and an
environment reading both would silently merge two unrelated sessions.

### Working Location

```meta
type: value-object
aliases: [WorkingLocation, cwd, working_folder]
```

Where the session was working. It has four fields, and equality is by all four:

- `working_folder`;
- `repository`, optional, in `owner/name` form, as the agent recorded it;
- `resolved_repository`, optional, as this product placed it (below);
- `branch`, optional.

The repository and the branch are optional because of what the agents actually
write down, not because the value is unimportant. Copilot records the repository
and the branch outright. Claude records the branch, which it states in its
transcript, and never the repository, in any path. The folder is the only thing to
go on, and a repository read off a path leaf would be a wrong fact where absence is
a true one. So a Claude session carries a branch and no repository, and a Copilot
session carries both. Absent means "the agent did not say".

A replicated session is the one case where the folder is missing for a different
reason. The machine that ran the session recorded it, and it deliberately did not
travel: a path describes that machine's disk, and the reading machine would act on
it. Absent there means "cannot be shown here", not "was never written down". Either
way nothing on this side reconstructs one. The log fills no gap an agent left, and
none the boundary made either.

The **resolved repository** sits beside the recorded repository, never in its
place. It is the `owner/name` of the registered repository whose clone directory
contains the working folder, on the environment that read the session. This is not
the inference ruled out above. A path leaf is a guess about which of GitHub's
several `Backlog`s a folder is a clone of. A registered clone directory is a fact
the person stated on the Repositories screen, and a folder is either under it or
not. It is still a fact about the reading environment's registrations rather than
about the session, which is why it is a second field. The two fail differently, and
a surface must be able to tell "the agent said" from "this product placed".

It resolves on the environment that ran the session, the only one holding both the
folder and the clone. It travels with the record and is held as it arrived
everywhere else. Where no registered clone contains the folder it is absent, and
absent still means only that.

### Activity Window

```meta
type: value-object
```

When the session was alive: optional `started_at` and a required `last_activity_at`.
Equality by both. The start is optional because some evidence is only a file with a
timestamp on it. The last activity is required, because that timestamp always exists
and is what `Liveness Assessment` reads.

### Session Activity Stream

```meta
type: value-object
aliases: [SessionActivityStream, activity stream, session updates]
```

Optional externally reported activity about one known session, ordered as that
session reported it.

The stream is keyed by `Session Identity`, not by a collection row id, because the
context cares which session the activity belongs to and not how the storage happened
to key it. It is optional because a session without Collections MCP reporting is
still a complete session record.

### Session Activity Entry

```meta
type: value-object
aliases: [SessionActivityEntry, event_id, sequence, activity summary]
```

One externally reported milestone about a session: what kind of activity it was, when
it happened, its per-session sequence, and a short summary fit to be shown back to a
person.

Identity inside the stream is by external `event_id`; order is by the session's own
sequence and then by occurrence time. Both are needed: duplicates must collapse, and
late arrivals must still land in the right place.

### Session Enrichment Summary

```meta
type: value-object
aliases: [SessionEnrichmentSummary, latest activity, enrichment]
```

The one-line answer derived from a `Session Activity Stream`: the latest meaningful
activity text, when external activity last arrived, and what correlation facts can be
shown beside the base session row.

It is a value object because it is a reading over the stream, not a second entity. A
new activity entry replaces the summary by deriving a later one rather than by
editing a stored record.

### Delivery Run

```meta
type: entity
related: [.devbook/domain/sessions/features.md#delivery-runs-beside-their-sessions]
aliases: [DeliveryRun, run, orchestration run, flow run]
```

One orchestrated delivery run, as the run file records it. A run is a flow or
orchestration skill driven through a dashboard. It is identified by the run id
within the worktree folder it was filed under.

Two things write that file:

- a dashboard this product only reads from;
- this product itself, through `Delivery Run Recording`, for a session that chose
  Backlog as the surface it reports to.

The file has one shape either way. A run recorded here and one imported from a
dashboard's folder are the same kind of thing to everything that reads them. Which
of the two wrote it is provenance, not a difference in kind.

It holds what the file states:

- which dashboard wrote it, and the worktree key it was filed under;
- the skill, the title, the status word verbatim, and the change kind;
- the `Delivery Run Reference`s it names;
- when it started and when it was last updated;
- its stages, each with its status, duration, how many times it was marked done, and
  its own record of how it ran;
- per stage, every sub-agent call its `insights` saw, with that call's time, tokens
  and tool calls where recorded, and how many tool calls the stage made;
- its token usage in total, for delegated agents, and per stage, with the models seen;
- the owner session's context gauge, with its peak;
- its tool activity summed by category and by MCP server;
- the sessions that drove it, what started it, and a sweep's verdicts (below).

`insights` is the run file's list of the tool calls and delegated agents the driving
session reported, each filed under the stage it ran in.

The run also carries the environment its file was read on, stamped by the source the
way a session's is: a run file found here was written here.

The worktree key is the dashboards' own: the folder's leaf and eight hex characters
of a hash of its lower-cased path. It is one-way, so the path cannot be recovered from
it. The same key can be derived from a session's `Working Location`, which is what
`Run Attachment` does.

**A run can name the sessions that drove it, and then no guess is made.** A run
started through `Delivery Run Recording` with the caller's session id carries it in
`sessionIds`. A session that picks the run back up is appended beside it, never over
it. Where a run names a session the list holds, it joins that session by identity.
Only a run that names none is attached by worktree key and overlapping activity:
every dashboard file written before the field existed, and any writer that does not
send one.

**A run can say what started it, and a sweep what it found.** Where the writer said,
a run carries its trigger, `attended` or `scheduled`. It also carries the schedule
that fired it and the repository it worked in. A run that says nothing reads as attended. A devbook
sync sweep closes its run with its verdicts: one row per sync unit, with each
chapter's own verdict, kept as sent. The repository is what places a scheduled run,
since every scheduled run cuts a worktree of its own.

**One run reported to two surfaces is one run here.** A flow reports its run to every
surface it is bound to, and each surface files it under an id of its own. So one piece
of work can be two files, in two dashboards' folders. The reading folds such a pair
into one `Delivery Run` before anything else sees them. Two files are one run when all
of these hold:

- they name the same skill;
- they come from different dashboards, since two files from one surface are two runs;
- both are dated, and they started within five minutes of each other;
- they agree on the session, in one of two ways:
  - they sit under the same worktree key, compared without regard to case, and their
    session ids do not contradict: they share one, or neither names any;
  - they sit in two different folders and both name one session id.

A shared session crosses folders because the dashboard server keys its folder by its
own working directory. That is the repository's main checkout, whichever worktree the
flow ran in. The backlog surface keys by the worktree the flow named. A session id is
identity and a folder key is not, so a shared session is enough across folders and two
files naming none are not. Where partners of both kinds qualify, the one in the same
folder is taken. A pair that names different sessions stays two runs.

The record is the file of the first of `delivery-surface-dashboard`, `orch-dashboard`
and `backlog` to have reported the run. Its id, status word, stages and measured
figures are the run's. The others fill only what it left blank and add the references
it did not name. The merged run keeps the folder of the `backlog` file, the one the
flow named, so it stays in its worktree's list. Which surfaces reported it is kept
beside which one wrote the file, as provenance.

The window is five minutes because a surface reattaches a run of the same skill in the
same worktree rather than opening a second. So one surface cannot hold two runs that
start close together. What the window prevents is a run whose partner never arrived
pairing with the next run's file from the other surface.

*Delivery* run, with the adjective, because this context already has an
[activity run](#activity-run): a stretch of transcript in which an agent was
producing. A delivery run is another tool's record of work it was tracking, with a
status that tool assigned. The two share a word and nothing else.

Its status is never derived from a reading. The dashboards wrote seven spellings for
it across two generations, and mapping them onto fewer members would decide what a
writer meant rather than report what it wrote. A surface that needs a chip normalises
at the last moment, where a new spelling degrades to a plain label rather than to a
dropped row.

**Reading accepts every spelling; writing uses one.** The asymmetry is deliberate. A
run imported from a dashboard is somebody else's record and is reported as written,
`completed` and `success` included. A run this context records is its own. It writes
`in_progress` while under way, and ends on `done`, `blocked`, `parked` or
`cancelled`. Reviving an older generation's spelling from here would make the drift
permanent rather than historical.

### Delivery Run Recording

```meta
type: domain-service
related: [.devbook/domain/sessions/domain.md#delivery-run, .devbook/domain/sessions/features.md#record-a-run-as-it-happens]
```

The capability a session uses to record a `Delivery Run` here as it happens, rather
than this context finding one afterwards in a folder another tool wrote. It has eight
operations, named by the delivery engine and not by this context:

1. bring the surface forward;
2. start a run;
3. record a prompt;
4. set a run's context;
5. update a stage;
6. finish a run;
7. list runs;
8. get one run.

**The names belong to the engine.** A session finds a surface by matching operation
names against the tools it has, so these names are a wire contract with something
outside this repository. Renaming one is not a rename: the surface stops being found.
The session then reports no surface attached, which is also a normal outcome, and so
cannot be told from the fault.

**Scope is a worktree the caller states, never a folder this product is sitting in.**
The process recording a run is the application's. One clone's worktrees share a
repository and have as many directories. The caller names the folder its run is about,
and the worktree key is derived from it. `Run Attachment` runs the same derivation from
the other side, which puts a recorded run and the session that drove it on one row.

**Starting a run for a skill already under way reattaches to it.** A session that picks
work back up says so by starting again. A second file would not be a second run: it
would be the one run, listed twice, with its stages split between them. A run that has
finished is not reattached to.

**A stage is addressed by its position, not its name.** The whole stage list is fixed
when the run begins, so the surface can show what is still to come. A name is a label
on a position rather than a key, and two stages may carry one name. An index outside
the list is refused rather than ignored. A caller one stage off would otherwise go on
reporting the wrong stage for the rest of the run.

**What started a run is written once.** Starting a run takes the trigger, the schedule
and the repository. A trigger the engine does not name is refused. A reattach fills in
only what an earlier start left out, because a scheduled run a person picks back up was
still fired by its schedule. Finishing a run takes a sync sweep's verdicts. They are
kept on the run, and, where the run named its repository, filed per chapter as the
Devbook context's `Sync Verdict`.

**It records what it observes and claims nothing else.** The model a run resolved is
something the run states, so it is kept. What a run consumed is not something an
operation can say, because a call arrives without the session that made it. Those
figures come from `Delivery Run Telemetry` or are absent, and absent is shown as left
out rather than as zero.

### Delivery Run Telemetry

```meta
type: domain-service
related: [.devbook/domain/sessions/domain.md#delivery-run-recording, .devbook/domain/sessions/features.md#what-a-run-cost-reported-by-the-session-itself]
aliases: [DeliveryRunTelemetry, hook telemetry, telemetry forwarder]
```

The measured half of a `Delivery Run`. A coding session reports events about itself,
and this service attributes them to the run the session is driving. The events are a
tool starting and finishing, a delegated agent, the context being compacted, and a
turn or the session ending. `Delivery Run Recording` keeps what the run is told; this
service keeps what is measured.

**The run is found from the event, never from a pointer.** The event names the session
and the folder it runs in. The worktree is that folder, or the nearest one above it
with runs filed. The run is the one in progress there that lists the session. Failing
that, it is one in progress that lists no session yet, which the session then joins. A
worktree whose runs in progress all belong to another session takes nothing from this
one.

**Tokens are read from the session's transcript, once each.** The event carries no
usage. The transcript it names does, with each delegated agent's in a file of its own
beside it. Each file is read on from where the last event left it, so a message is
counted once. That holds for a message written as several lines, which repeats its
usage on each. A session first seen mid-way starts at the end of what is written: a gap
is lost rather than a whole conversation counted as the run's.

**A delegated agent carries what its call reported.** The Agent call's response states
the tokens the sub-agent moved and the tool calls it made. Both are kept on the agent's
record, under the names the dashboard writes them. A response that states neither adds
nothing rather than a zero.

**Best effort, and never the session's problem.** An event that cannot be attributed,
or a transcript that cannot be read, records nothing and raises nothing. The reporter is
a hook, and must never fail the tool call it reports on.

### Stage Resolution

```meta
type: domain-service
related: [.devbook/domain/sessions/domain.md#delivery-run, .devbook/domain/sessions/features.md#how-each-stage-of-a-run-ran]
aliases: [DeliveryRunStageResolution, stage execution]
```

How each stage of a `Delivery Run` ran, read off the run. It reads the stage's own
record of how it ran, the sub-agents seen in it, and the [phase map](#phase-map) the
run resolved before it ran. It ports the delivery engine's own run view, so this
product and that view say the same thing about one run.

Each value is taken from the first source in its row that has one:

| Value | Read from, in order |
| --- | --- |
| Mode | Personal Validation is always the gate. Otherwise the mode the run recorded, reading `delegated` as `delegate` and `forked` as `fork`. Otherwise `delegate` where a sub-agent worked in the stage, and `inline` for the rest; a mode found this way is marked inferred. |
| Worker | The agent the phase map binds, when it is among the sub-agents that ran. Then the agent behind a `delivery:runner-<effort>` effort runner: the configured agent, or `general-purpose` where none is. Then the longest-running call. A stage that recorded running inline with no agent ran in the owner session, whatever was configured. |
| Model and effort | What the stage recorded running, then the runner's, then the configuration's. |
| Configured | The phase map's entry for the stage under the qualifier it ran under, then under any name its title goes by. Its origin is the entry's own origin, or else the team default with the overlays the run context lists merged over it. |
| Sub-agent calls | Each call the run's `insights` saw, with its own time, tokens, tool calls and outcome. Where they saw none, each call the stage's record lists, except the slices the owner session worked itself. Then each agent the stage declared that neither shows running. |

**Configured against ran.** A stage is compared on its agent, its model's family and
its effort. It is compared only once it is past pending, and the gate never is. An
effort is compared only where the run recorded what ran. A configured agent that did
not resolve is a difference too.

**Inferred.** Where the run records no resolved phases, mode and agent are inferred
from what was seen. The stage says so rather than presenting a guess as a record.

A call under the gate is a [revise round](#revise-round)'s.

### Claude API Request Log

```meta
type: domain-service
related: [.devbook/arc42/adr/0024-claude-code-telemetry-arrives-on-the-mcp-listener.md, .devbook/domain/sessions/features.md#cost-as-claude-code-reports-it, .devbook/domain/sessions/domain.md#delivery-run-telemetry]
aliases: [ClaudeApiRequest, IClaudeApiRequestStore, ClaudeCodeOtlpLogs, cost receiver]
```

The model requests Claude Code reported making, as it reported them. There is one
record per `claude_code.api_request` event that its OpenTelemetry exporter sends to the
desktop app at `/v1/logs` (local ADR 0024). Each record names the session, the prompt,
the sub-agent and the skill it ran under. It holds the model, the effort, Claude Code's
cost estimate in whole micro-dollars, the four token counts and the duration.

**A request is kept once.** The API's `request_id` is its identity. An exporter retries
a batch it did not see acknowledged, so the same event arriving again is ordinary, and
the first copy is the one kept. An event without a `request_id` is not kept at all,
because it could not be told from its own retry.

**Only this event is kept.** Claude Code sends many events, such as prompts, tool
results, hook runs and errors. Every one but `claude_code.api_request` is dropped on
arrival. Of the one kept, the identity attributes Claude Code adds to every event are
not stored: the account, the organization and the e-mail address.

**An unknown figure stays unknown.** A field the event did not carry is stored as
absent, never as zero, which is the rule every measured figure in this context
follows.

The records are this machine's: they are kept in the app's local database and do not
replicate. `Delivery Run Telemetry` counts tokens from the transcript. This log holds
the figures Claude Code itself reported, cost among them.

### Cost Attribution

```meta
type: domain-service
related: [.devbook/domain/sessions/domain.md#claude-api-request-log, .devbook/domain/sessions/domain.md#stage-resolution, .devbook/domain/sessions/domain.md#model-price-table, .devbook/domain/sessions/features.md#cost-as-claude-code-reports-it, .devbook/domain/sessions/features.md#a-price-table-for-runs-without-telemetry]
aliases: [DeliveryRunCosts, CostFigure, DeliveryRunStageCost, cost attribution]
```

Says what a session, each stage of a delivery run and each sub-agent call cost. A
figure is the cost in whole micro-dollars and which of two sources it came from:

- **reported**: the requests in the `Claude API Request Log`, where Claude Code
  reported them;
- **estimated**: the run's own token counts at the `Model Price Table`, where it did
  not. Every estimate is marked as one.

| Unit | Reported cost | Estimated cost |
| --- | --- | --- |
| Session | The sum of every request it reported. | Its own tokens per model, read from its transcript, plus the sub-agent share of each of its runs' stages. A row with no transcript of its own sums its runs' whole stages. |
| Stage | The owner session's main-loop requests inside the stage's window, plus its workers' requests (below). | The owner session's share at the stage's model where it ran inline, else at the owner's. The sub-agents' share at the one model their calls ran on; calls on several models are each priced at their own. |
| Sub-agent call | Its agent's requests from the call's end less its duration to its end, two seconds either way for the two clocks. | The stage's sub-agent tokens, in proportion to the tokens the call reported. |

**A stage's window** runs from its latest start to its completion; a stage still under
way has no end yet. The owner session is each session the run names, or the one it was
joined to by worktree and time. Only the main loop's requests count by time. A request
a sub-agent made has an `agent.name`, or a `query_source` of `agent:<name>`. It counts
only when that agent is one of the stage's workers, matched the way `Stage Resolution`
matches agents: the same name, or one the other with a plugin prefix. A worker's
request counts inside the stage's window or inside that worker's own call. So a
sub-agent still running after its stage was marked done is still its stage's.

**When the reported cost cannot be told apart, the unit is estimated:**

- A stage re-entered after requested changes. Its start is stamped again on every
  pass, so its window holds the latest pass only, and a figure over it would read as
  the whole stage.
- A call that recorded no end, unless it is the stage's one call of that agent.
- Two calls of one agent whose times overlap, such as a fan-out.

**An estimate is no figure at all when a part that spent tokens could not be
priced**, because a partial sum would read as the whole. With no request and no price,
there is no cost. It is never shown as zero.

### Cost Baseline

```meta
type: domain-service
related: [.devbook/domain/sessions/domain.md#cost-attribution, .devbook/domain/sessions/features.md#each-stage-against-its-usual-cost]
aliases: [DeliveryRunBaselines, CostAgainstUsual, CostBand, DeliveryRunCostComparison, DeliveryRunCostInsight, usual cost, cost baseline]
```

Reads a run's cost against its usual, from the figures `Cost Attribution` gives the run
and the runs before it. The median is used rather than the mean, because one runaway run
would otherwise move the baseline for the next thirty.

**The earlier runs** are runs of the same skill id that finished, marked done, and started
before this one. The latest 30 count, and the run itself never does. A run with no start
is placed by its last update.

**A stage** is matched to the earlier runs' stages by name. Its figures are its cost, its
share of what the run's priced stages cost, and its ratio to the median cost of that
stage in the earlier runs that finished it and priced it. A stage that has not finished
has no ratio. Neither has a stage that fewer than five of those runs priced.

**The bands** come from the ratio:

| Ratio | Reads | Drawn |
| --- | --- | --- |
| 0.8 or below | lower than usual | green |
| within 5% of 1, either side | typical | "typical" |
| 1.5 or above | higher than usual | highlighted |
| anything between | the plain ratio | plain, such as "1.2×" |

A ratio is written to one decimal, "2.4×". It gets two where one decimal would round it
across a band's edge, so 1.46 is "1.46×" rather than "1.5×" without the highlight. It
also gets two below 0.1.

**The run** is compared like for like. An earlier run is a sample only when it finished
and priced every stage this run priced, and its total is the sum of those same stages.
Summed over whatever each earlier run priced, a run from before per-stage tokens were
counted would read as a cheap run. The median would sink, and every later run would read
many times its usual while each of its stages read typical. The run needs at least five
samples and is compared only once it has left in-progress.

**The insight** names the run's ratio and the stage that explains most of the
difference. When the run cost more than usual, that is the stage whose cost exceeds its
own median by the most. When it cost less, it is the stage furthest below its median. A
typical run names no stage. The stage's re-entries are its done count less one, which
counts the implement–review loop and every revise round. Review rounds are to join it
once the run records them.

### Weekly Usage

```meta
type: domain-service
related: [.devbook/domain/sessions/domain.md#cost-attribution, .devbook/domain/sessions/domain.md#claude-api-request-log, .devbook/domain/sessions/features.md#share-of-the-week-and-the-weekly-limit]
aliases: [WeeklyUsage, UsageWeek, WeeklyLimit, UsageShare, share of the week]
```

Says what part of the week a session or a run makes up, and, once a refusal has sized
it, what part of the weekly limit. Reported cost only: the share is of the week's
telemetry cost, and a token estimate divided by a reported sum would compare two
measurements.

**The week** is the seven days ending at the reset time that the latest all-models
weekly refusal carried. With none, it ends at the latest Fable refusal's reset time. A
reset that has passed rolls forward a whole week at a time, because the allowance
renews on the same beat each week. The reset instant belongs to the next week. With no
refusal carrying a reset time, the week is the seven days up to now. The total is every
request reported inside the week up to the moment it was read. A share counts nothing
later, so a session still running cannot outgrow the total it is divided by.

**A weekly limit** is sized by the most recent refusal of its kind. Its size is the cost
reported from seven days before that refusal's reset time up to the refusal. A refusal
that carried no reset time counts from seven days before the refusal itself. Fable's
limit counts the requests to a Fable model only. A refusal with no reported cost before
it sizes nothing. The percentage of a limit is always an estimate and labelled one.
Nothing the product reads states the limit's size, and requests made on another machine
never reach this one.

**A session's share** is its own requests inside the week. **A run's** is the requests
its stages and their sub-agent calls claim, matched as `Cost Attribution` matches them.
Nothing reported inside the week is no share, never zero.

### Model Price Table

```meta
type: value-object
related: [.devbook/domain/sessions/features.md#a-price-table-for-runs-without-telemetry]
aliases: [ModelPriceTable, ModelPrice, IModelPriceStore, model prices]
```

The rates the person entered in Settings, one row per model: input, output, cache read
and cache write, each in US dollars per million tokens, each optional. Empty until filled
in. A model a run names finds its row in this order:

1. by the same id;
2. by the same model written another way, such as "Opus 5.5" for `claude-opus-5-5`;
3. by its family, such as `opus`.

A context suffix such as `[1m]` is ignored. An estimate needs a rate for every kind of
token spent, and has none otherwise. The table is this machine's, kept in the app's
local database beside the request log, and does not replicate.

### Delivery Run Reference

```meta
type: value-object
related: [.devbook/domain/sessions/features.md#the-work-a-run-is-linked-to]
aliases: [DeliveryRunReference, linked work, tracker item]
```

One piece of work a `Delivery Run` is linked to: the Backlog entry it was started from,
a tracker issue it was working, or a pull request it opened. It carries:

- which of those it is, and what to call it;
- the item's own title, where the file recorded one;
- its address, where it has one;
- the repository it belongs to;
- for a Backlog entry, the plan and the entry's stored id, where something named them.

Equality is by all of them.

A run holds a list of references rather than a single tracker item, because it has more
than one link and they are not the same link. It starts from an item and ends in a pull
request, and a reader wants to reach either.

The address is optional, and its absence is a fact rather than a gap. A Backlog entry is
in this product, not on a page, and nothing here invents a URL for one. Such a reference
names the entry in one or both of two ways:

- the plan and item, where the prompt's plan item marker named them. That pair
  identifies an entry across plan versions.
- the entry's stored id, where the line the app puts on a copied entry named it.

An imported entry copied out of the app carries both markers. The stored id is the surer
of the two, so a surface that opens the entry tries it first. Opening the entry is the
surrounding application's job; this context names the entry and never opens it.

A reference can also reach a run from outside its file. Where the run names no entry,
the surrounding application supplies, by session id, the entry that linked the run's
session. So this context never reads Tasks for it. A surface shows that entry as the
run's own outcome rather than as a chip of its own. The entry is the work the run was
answering about, and two controls to one place is one too many.

No state rides on a reference. Whether the issue is open or the pull request merged is
a reading of the tracker, and this context has not performed one. A `Session Log` that
reported one would be filling a gap nobody looked into, which its invariants already
forbid for a session's own fields.

### Session Row

```meta
type: value-object
related: [.devbook/domain/sessions/domain.md#run-attachment, .devbook/domain/sessions/features.md#delivery-runs-beside-their-sessions]
aliases: [SessionRow, row]
```

One row of the session list. It is one of three things:

- an `Agent Session` and the `Delivery Run`s it drove;
- a single `Delivery Run` the list holds no session for;
- every such run that one schedule fired in one repository.

The row is the unit the list shows, because a session and a run describe the same work
from two sides, and either may be the one this environment still has.

Every fact read off a row comes from whichever source has it, and the row says nothing
either source did not. A schedule's row is titled by the schedule, since each of its
runs cut a worktree nobody reopens. Any other run-only row is titled by its worktree,
dated by the run, and placed on the environment the file was read on. A run-only row is
Finished, because there is no liveness evidence for it. A session row's repository is
the session's. Where the agent recorded none, it is the tracker item a run of it named:
a recorded fact from a second source, not the guess from a path the Session Log forbids.

A row also answers four questions the list shows on it. Each answer comes from
whichever side recorded it, and never from a default:

- **Model**: the model the session spent the most output on, else the first model a
  run of it observed.
- **Effort**: the reasoning effort the owner session ran at. It is read from the latest
  stage of a run on the row that ran inline, in the owner session, and recorded its
  effort. A delegated stage's effort is its sub-agent's and says nothing about the
  session. Telemetry, once it is received, is the better source. With neither, the
  effort is not recorded.
- **Output**: the session's own output tokens, summed over its models, else its runs'
  totals.
- **Duration**: from its start to its last activity, where the start is dated.

### Delivery Run Catalog

```meta
type: value-object
```

What a reading of one environment's dashboards answers with. It holds every `Delivery
Run` every readable folder holds, and names the dashboards or folders that could
**not** be read. It carries no count of what was left out, because nothing is. A run
is work somebody started on purpose, there are hundreds rather than thousands, and most
attach to rows the list already has. Equality by both parts.

### Session Catalog

```meta
type: value-object
aliases: [AgentSessionCatalog]
```

What a reading of one environment answers with: the `Agent Session` list it will
describe, the sources it could **not** read, by name, and how many sessions it
discovered before any limit was applied.

Three parts rather than one list, because "no sessions" and "could not look" are
different answers. A reader who cannot tell them apart will go looking for a fault
that is not there. Equality is by all three.

### Agent

```meta
type: enum
aliases: [AgentSessionKind, agent, agent_kind]
```

Which assistant ran the session: `claude`, `copilot`.

A type on the session rather than a list per vendor. A list per vendor would make
"what has been running here" unanswerable without first knowing how many vendors
there are.

### Session State

```meta
type: enum
aliases: [AgentSessionState, state]
```

How far along a session is, as far as the evidence goes:

- `running` — the agent is present on the environment and something moved recently.
- `stalled` — still registered as present, but nothing has moved for longer than the
  `Stale Threshold`.
- `finished` — over. Only its record is left.

Starting, waiting and failed are left out on purpose. An agent could be in any of
them, but none can be derived from what it leaves behind, so this context does not
claim them.

### Reporting Capability

```meta
type: enum
aliases: [reporting status, capability, enabled, degraded]
```

Whether a session can report activity to the optional Collections MCP:

- `enabled` — the capability is configured and reachable enough to accept updates.
- `disabled` — no Collections MCP reporting is configured for this session or environment.
- `degraded` — reporting is configured, but delivery is currently failing or the MCP
  cannot be reached.

This is about the reporting path and not about the session's own health. A running
session can have degraded reporting, and a finished session can still have an enabled
path that already delivered its last update.

## Run Attachment

```meta
type: domain-service
related: [.devbook/domain/sessions/domain.md#delivery-run, .devbook/domain/sessions/domain.md#session-identity, .devbook/domain/sessions/features.md#under-the-session-that-drove-it]
aliases: [SessionRows, attached run, run-only row, stray run]
```

Builds the list of `Session Row`s from the sessions and the runs. It decides which
`Agent Session` each `Delivery Run` ran in, and which runs stand on their own. It is a
pure function over what it is given, like `Session Grouping`.

```mermaid
flowchart TD
    A[Delivery run] --> B{Names sessions<br/>in sessionIds?}
    B -->|yes| C{Does the list hold<br/>one of them?}
    C -->|yes| D[Joins the first it holds]
    C -->|no| F{Fired by a schedule?}
    B -->|no| E{A session in the same worktree,<br/>active while the run was open?}
    E -->|yes| G[Joins the one whose start<br/>is nearest the run's]
    E -->|no| F
    F -->|yes| H[One row per schedule<br/>and repository]
    F -->|no| I[A run-only row]
```

**Identity first.** A run that names its sessions joins by identity, with no worktree
or window test. Where it names sessions and the list holds none of them, it is never
attached by its worktree. The run said whose it is, and a guess would put it on
someone else's row.

**A run that names no session needs both a place and a time.**

- **The worktree.** The run's key must equal the key derived from the session's
  `Working Location`, or from a folder above it, since a session may be started below
  the worktree's top level. Keys are compared without regard to letter case. The
  dashboard slugs git's casing of the leaf, and a session records the folder as it was
  launched in. A replicated record has no working folder, so it is matched on the
  worktree key its record carried.
- **The window.** That session's `Activity Window` must overlap the run's. A worktree is
  worked in by several sessions over its life, so the worktree alone names a place, and
  only the overlap names a time.

Where more than one session passes both tests, the one whose start is nearest the run's
own wins, because the session that opened the run started with it. A run whose file
does not date its start is placed by its last update, the one moment it is known to
have existed.

A run that matches nothing becomes a row of its own and is never dropped. Its session
may be older than the `Session Limit` keeps, or may have been read on another
environment, and neither is a reason to hide the work. The rows come back in the order
the sessions were given, then the schedules' rows, then the other run-only rows, most
recently updated first. Ordering for display is `Session Grouping`'s job.

## Liveness Assessment

```meta
type: domain-service
related: [.devbook/domain/sessions/domain.md#session-state, .devbook/domain/sessions/flow.md]
```

Decides whether a session with liveness evidence is `running` or `stalled`, by
comparing its `last_activity_at` against the `Stale Threshold`.

A service rather than a property of the session, because the answer depends on the
current time and an `Agent Session` holds no clock. The threshold itself is a policy
of this context; see [Stale Threshold](#stale-threshold).

Invocation semantics: query-oriented; evaluated per reading, never persisted. Nothing
moves a session into `stalled`. It reads as stalled while the silence lasts, and reads
as running again the moment the agent writes.

## Session View

```meta
type: domain-service
related: [.devbook/domain/sessions/domain.md#session-log, .devbook/domain/sessions/domain.md#session-state, .devbook/domain/sessions/domain.md#session-row, .devbook/domain/sessions/features.md#open-on-the-live-sessions, .devbook/domain/sessions/features.md#sessions-from-another-machine, .devbook/domain/sessions/features.md#only-the-rows-with-a-run]
aliases: [AgentSessionView, view, live, all, AgentSessionEnvironments]
```

Narrows a set of `Session Row`s to the ones a reader currently wants in front of
them: the live ones, or all of them. A row is a session with its runs, or a run the
list holds no session for, and the view narrows both alike. A run-only row is always
Finished, so the live view never keeps one.

```mermaid
flowchart LR
    A[Session rows] --> B[The shell's<br/>repository scope]
    B --> C[Environment]
    C --> D[With delivery runs]
    D --> E[View: Live or All]
    E --> F[Session Grouping]
```

**Narrow first, then group.** A surface applies the narrowings in the order drawn above,
and only then carves what is left into groups. Never the reverse, so an environment
with nothing live on it loses its section rather than keeping an empty one. The shell's
repository scope is the shell's, not this context's; the surface applies it first. Each
narrowing only removes rows, so their order does not change which rows remain. It does
decide what Live's tally counts: the live rows among what the other narrowings kept.
No narrowing reorders. Ordering is the grouping's answer to give, and a narrowing that
sorted would be a second answer to what "most recently active first" means.

The view decides nothing about liveness. The `Session Log`'s invariant on derived
`Session State` has already settled that, and this service only reads the answer. That
is why "live" here can only mean running or stalled. A second place that worked out
for itself whether a session was still going would be a second definition of live,
free to drift from the first. The two would disagree first on exactly the sessions a
reader most needs to trust.

A service of its own rather than one more member of `Session Grouping`. Grouping
guarantees that every row in is a row out. A "live" grouping would break that guarantee
while sitting in the same strip as environment and agent. The reader would then have
one control whose options sometimes rearrange the list and sometimes shorten it.

**The environment narrowing sits beside it.** `AgentSessionEnvironments` narrows the
same rows to the ones on one environment, or hands every row back when none is named.
It keys on the environment's id rather than its name, because a machine can be renamed
and two can share a name. It offers only the environments the rows name, in the order
a grouping by environment draws its sections. It is a separate narrowing rather than a
third view, because a view answers a question about liveness and the environment
answers one about place. Folded together, "Live" would mean "live here" on some presses
and "live anywhere" on others.

**Grouping is the one operation that never removes rows.** The narrowings are the ones
that do: the environment, the view, and the surface's own
[with delivery runs](features.md#only-the-rows-with-a-run) toggle. Because only the
narrowings shorten the list, the surface can say what they left out.

Pure, like `Session Grouping`: no clock, no I/O, no state. `Liveness Assessment` already
spent the clock. So a reader can change view without anything being read again, and two
views of one reading are two renderings of the same facts. `All` hands back the list it
was given, as an environment narrowing with no environment named does.

Invocation semantics: query/composition-oriented; invoked per view, never stored. Which
view is in force is the reader's choice. It is not part of this context's state, nor of
what a reading returns. A surface holding it starts at the live view every time it is
opened.

One thing besides the reader changes the view. When a surface is asked to focus one
session and the current narrowings hide that session's row, the surface switches to
`All`, turns the "with runs" toggle off and clears the environment. A request to show a
session that then shows an empty list would answer a question nobody asked.

## Session Grouping

```meta
type: domain-service
related: [.devbook/domain/sessions/domain.md#session-log, .devbook/domain/sessions/domain.md#session-row, .devbook/domain/sessions/domain.md#environment, .devbook/domain/sessions/features.md#session-grouping]
aliases: [AgentSessionGrouping]
```

Carves a set of `Session Row`s, run-only rows included, into sections. Each section is
ordered most recently active first, and the sections sit in a stable order. There are
five groupings:

| Grouping | Section key | Sections, in order | Heading | On the session list |
| --- | --- | --- | --- | --- |
| When | Live state, then the day last active on the reader's clock | Live now (Running or Stalled, whatever day they started), Earlier today, Yesterday, Past week, Older | The bucket's name | Yes, and the list opens on it |
| Repository | The repository the row shows, ignoring case | By name; the rows that name no repository close the list under "No repository" | The spelling its most recent row carries | Yes |
| Environment | The environment's id | By name, ignoring case, then by id | The name its most recent row carries | Yes, as Machine |
| None | None | One section, holding every row | None | No |
| Agent | The agent's name | The order the `Agent` enum declares | The agent's name | No |

A bucket or a section with nothing in it is not drawn. No rows means no sections at
all, not one empty section. Because the Environment heading is the most recent row's
name, a renamed machine shows the name it has now. Two machines that share a name are
two sections.

Each section carries the key that made it one, so a surface can tell apart two
sections whose headings match.

A service because grouping spans sessions rather than belonging to any one of them,
and a pure one: no I/O and no state. Only When reads a clock and a time zone, and both
are handed in: the moment the list was read, not the moment it is drawn. So a press in
the filter bar never moves a row from one day into another. It guarantees two
properties that a reader relies on without being told:

- **Grouping rearranges and never filters.** Every session in, every session out. A
  count taken before grouping is still correct after it.
- **Group order does not depend on group size.** When's buckets keep their own order,
  repositories and environments sort by name, and agents follow the `Agent` enum. So a
  section does not move under the reader as sessions come and go.

Invocation semantics: query/composition-oriented; invoked per view, never stored.
Which grouping is in force is the reader's choice and is not part of this context's
state.

## Session Activity Publishing

```meta
type: domain-service
status: proposed
related: [.devbook/domain/sessions/features.md#session-activity-enrichment, .devbook/domain/sessions/flow.md, .devbook/domain/sessions/dependencies.md]
```

Emits sanitized activity updates for a running session to the optional Collections
MCP when that session has reporting configured and reaches a meaningful milestone.

The service publishes the beginning of a session, meaningful activity deltas while it
runs, and a final update when it finishes. It never publishes prompts, transcripts,
tool output bodies, or secrets. It publishes only the coarse facts this context is
willing to read back later as activity evidence.

Invocation semantics: event-triggered policy/process-manager behavior. It is invoked
from session milestones, queued asynchronously, and retried without blocking session
work. It may fall back to local-only behavior when reporting is unavailable.

## Session Activity Enrichment

```meta
type: domain-service
status: proposed
related: [.devbook/domain/sessions/features.md#session-activity-enrichment, .devbook/domain/sessions/flow.md, .devbook/domain/sessions/dependencies.md]
```

Layers optional `Session Activity Stream` evidence onto the locally read `Agent
Session` so the session row can say more than the local record alone can say.

The service de-duplicates entries by external `event_id`, and orders them by
per-session sequence and occurrence time. It derives the `Session Enrichment Summary`
and `Reporting Capability` that belong on the session. Local evidence stays
authoritative for whether the session exists, where it worked, and what `Session
State` it reads as. External activity enriches those facts and never replaces them.

Invocation semantics: query/composition-oriented; evaluated per reading when external
activity evidence exists, and skipped entirely when it does not.

## Shared Value Objects

```meta
type: shared-value-objects
```

> Value Objects used by more than one aggregate in this bounded context.

Sessions has a single aggregate; every value object is documented under it.
This chapter is reserved for a second aggregate.

## Shared Enums

```meta
type: shared-enums
```

> Enums used by more than one aggregate in this bounded context.

Single aggregate, so `Agent` and `Session State` are documented under it. Reserved
for the same reason as the chapter above.

## Ubiquitous Language

```meta
type: ubiquitous-language
```

### Environment

```meta
type: term
aliases: [environment, machine_name, environment_id]
related: [.devbook/domain/sessions/domain.md#session-log, .devbook/domain/environment/domain.md#environment, .devbook/domain/tasks/domain.md#device]
```

Where an agent ran. Today that is a development PC, named as that machine names
itself.

It also carries a stable identity beside the name it displays: `EnvironmentId`, the
product-wide device identity issued by the shared kernel
([Device](../tasks/domain.md#device)), which no bounded context owns. The display
name can change without changing which environment it is, which is why grouping keys
on the id rather than on the name.

**This is not the Environment context's Environment.** That one is a launchable
destination, such as a local harness, a staging app or a cloud dashboard, and belongs
to a person's shortcuts. This one is the place a session happened. The words collide
and the concepts do not. The `related` link above exists so that a reader who lands on
either finds the other rather than assuming they are the same thing.

It is also not Dev PC Management's **Machine**, though today every environment is
one. A Machine is a registered, wakeable, compliance-tracked box. An environment is
wherever an agent can run, and nothing in this model stops that being a container or
a hosted runner later. Where the two name the same box, that is a lookup; see
`dependencies.md`.

### Stale Threshold

```meta
type: term
aliases: [StaleAfter]
related: [.devbook/domain/sessions/domain.md#liveness-assessment]
```

How long a session with liveness evidence may be silent before it reads as `stalled`
rather than `running`.

A policy, and therefore this context's rather than a rendering detail. The control
library that draws a stalled chip refuses to own it for exactly that reason. Deciding
"how long is too long" needs a clock and a product opinion, and neither belongs in a
component.

### Collections MCP

```meta
type: term
status: proposed
aliases: [collections mcp, collection store, activity collection]
related: [.devbook/domain/sessions/dependencies.md]
```

The optional MCP-backed collection a session can report sanitized activity into and the
Sessions context can read back from.

It is a transport and storage capability, not the owner of session meaning. The
session model stays in this context even when the activity passes through that store.

### Session Limit

```meta
type: term
aliases: [AgentSessionLimits, PerAgent]
related: [.devbook/domain/sessions/features.md#say-how-much-was-left-out]
```

How many sessions per agent a reading for a list will describe.

Per agent, not overall. An environment can hold hundreds of records, and the agents do
not hold them in equal numbers. A single overall limit would be filled by whichever
agent kept more history, and would quietly empty a list whose purpose is showing both.

A limit on how much is described, never a claim about how much exists. The number
discovered travels back beside the sessions, and a reading that dropped anything says
so. It applies to the list's reading only; a reading since a horizon
([Session History](#session-history)) has no limit.

### Session History

```meta
type: term
aliases: [AgentSessionLimits.History, ReplicatedSessionLimits.History, AgentSessionQuery.Since]
related: [.devbook/domain/sessions/features.md#say-how-much-was-left-out, .devbook/domain/sessions/domain.md#session-limit]
```

How far back a reading since a horizon is promised to reach on every source: 26
weeks, the longest period the Dashboard offers.

The Dashboard's longest window and this promise move together. A window longer than
the history would show figures for weeks no source was asked to keep. The weeks before
the history are never read, so a 26-week window has no earlier 26 weeks to compare
with.

A reading since a horizon describes every session active at or after it, however
many. So a consumer that counts is never handed the session limit as its total. A
source that keeps another environment's records has to keep two sets: the newest
sessions up to the limit, and everything inside the history whether or not it is among
them. A fleet count is only as complete as the least complete store behind it.

### Human Turn

```meta
type: term
status: draft
aliases: [human turn, HumanTurn]
related: [.devbook/domain/sessions/domain.md#session-log, .devbook/domain/roadmap/domain.md#working-stretch, .devbook/domain/roadmap/domain.md#actual-hours, .devbook/arc42/adr/0019-roadmap-counts-the-working-week.md]
```

A turn the person made in a session: starting the session, or a follow-up prompt.
The person's answer to a question the agent asked is one too. In a Claude
transcript a start or a prompt is a user turn whose `origin.kind` is `human`, and
an answer is the tool result of the agent's `AskUserQuestion` call.

A turn that automation sent is not a human turn. Neither is a task notification,
a subagent's hand-back, a scheduled run or any other tool result. That is what sets it
apart from a prompt: a prompt ends a [wait](#wait) whoever sent it, and most prompts
are sent by an SDK or a skill.

Sessions keeps only the instant of each human turn, never its text. A session
whose transcript does not record who made a turn has no human turns; a Copilot
session is one. Roadmap Planning draws the person's
[working stretches](../roadmap/domain.md#working-stretch) from these instants, and
Productivity's [Hours worked](../productivity/features.md#hours-worked) part
counts the same stretches.

### Activity Run

```meta
type: term
aliases: [AgentActivityRun]
related: [.devbook/domain/sessions/domain.md#session-log, .devbook/domain/sessions/domain.md#wait, .devbook/domain/roadmap/domain.md#working-stretch]
```

A stretch in which an agent was producing. This context derives it from the agent's
own transcript: a run of transcript events with no gap longer than five minutes. Runs
of one session never overlap each other. Runs of two sessions may, and that overlap
is part of what they measure.

It is not a [Delivery Run](#delivery-run), which is another tool's record of a piece of
work. Every activity run is an instant pair, never content, so it may travel on a
replicated record.

### Wait

```meta
type: term
aliases: [AgentActivityWait]
related: [.devbook/domain/sessions/domain.md#activity-run, .devbook/domain/sessions/domain.md#human-turn]
```

A stretch in which the agent had stopped and nothing had prompted it yet. A wait is a
gap that ended in a prompt. A gap that ended in anything else is idle or abandoned time,
and is not a wait.

A prompt ended it, not necessarily a person. Most prompts that end a gap are an SDK or
a skill sending the next message, so a wait never claims that a person kept the agent
waiting. A [human turn](#human-turn) is what says a person acted. Copilot cannot mark
where a wait begins, so a Copilot session carries an empty list of waits.

### Session Record

```meta
type: term
aliases: [AgentSessionRecord, AgentSessionRecords]
related: [.devbook/domain/sessions/domain.md#session-log, .devbook/domain/sessions/features.md#sessions-from-another-machine]
```

What this machine keeps about one of its own sessions after the agent's files are gone:
the session as last read, with its working folder and title, and what its transcript
was last folded into. That is its runs, waits, limit hits and human turns. It is kept
on the machine whether sync is on or not, and is keyed on the `Session Identity`.

The record never leaves the machine as itself, so the sync whitelist does not apply to
it. What sync carries is the whitelisted record built from it. How a reading amends the
record, and when the record answers for a session, are the
[Session Log](#session-log)'s rules.

### Phase Map

```meta
type: term
aliases: [phases]
related: [.devbook/domain/sessions/domain.md#stage-resolution]
```

The delivery engine's configuration of how each phase of a flow runs: the agent, the
model and the effort it binds per phase, kept under `phases` in the repository's
`.devbook/config.json`. An entry may carry a **qualifier** after a colon, which narrows
it to one kind of change. `phase-drafting:domain`, for example, binds the drafting phase
for a change to the `domain/` folder. A person may merge an overlay of their own over
the team default. A run records the phase map it resolved, and
[Stage Resolution](#stage-resolution) compares each stage against it.

This context reads the phase map off a run and never writes or resolves one.

### Revise Round

```meta
type: term
related: [.devbook/domain/sessions/domain.md#stage-resolution, .devbook/domain/sessions/domain.md#cost-baseline]
```

One more pass a run makes after a person asks for changes at the gate, the Personal
Validation stage. Work done under the gate is a revise round's. A stage re-entered for
a revise round counts it among its re-entries.
