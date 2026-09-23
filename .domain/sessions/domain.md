# Sessions

```meta
type: domain
status: active
```

> One chapter per Aggregate, Domain Service, Domain Event, or Shared Value
> Objects / Shared Enums grouping in this bounded context; each chapter's
> `type` records which of those it is. An Aggregate's owned Entities, Value
> Objects, and Enums are chapters directly beneath it, typed `entity`,
> `value-object`, and `enum`. Value Objects/Enums shared across multiple
> aggregates get their own chapter at the end instead of being duplicated.

The Sessions context owns the record of what the AI coding agents have been doing:
which sessions an environment has run, which agent ran each one, where it worked,
when it was last active, and whether it is still going. It owns no work item, no machine
and no repository — every one of those belongs to a supplier context, and this one
only holds the identifier it was given.

It is a **read model over evidence somebody else wrote.** The agents leave records
on the environments they run on; this context reads them and says what they mean. It
never starts, stops or names a session, which is why nothing here is a command and
why the aggregate below has no invariant about state transitions — a session's state
is derived on every reading, not advanced.

An optional Collections MCP can add a second kind of evidence: sanitized activity
updates that a configured session reports as it moves. Those updates are an
**enrichment layer**, not a second authority. They say more about what a known
session was doing; they do not decide that a session existed in the first place.

Evidence can also **travel between the person's own machines**, so the environment
that ran a session need not be the one they are sitting at. That is replication,
not a third kind of evidence: a record read on the second machine is still the
record the first machine wrote. See
`.arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md`.

This subject was first modelled inside
[Dev PC Management](../dev-pc-management/domain.md#machine-registry) as
Copilot Session Tracking on the Machine, when Copilot was the only agent the
machines ran. Two agents run on them now, and "which agent, in which repository, for
how long" is a different question in a different language from "how is this PC
configured" — so the subject moved out to here and Dev PC Management stopped
modelling it.

## Session Log

```meta
type: aggregate
status: active
related: [.domain/dev-pc-management/domain.md#machine-registry, .domain/sessions/dependencies.md, .arc42/08-crosscutting-concepts.md#session-record-sync, .arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md]
```

Everything one environment can say about the agent sessions it has a record of.

The consistency boundary is **one environment**, not one fleet: an environment is
the only thing that can read its own agents' records, so a log that spanned several
would be asserting facts nobody had gathered. A view over many environments is a
composition of several Session Logs, which is why grouping by environment is a
derivation rather than a structural relationship.

Invariants:

- A session appears **once**, however many records the environment holds for it. An
  agent may leave one record per process and another per transcript, and may file the
  same transcript under two working folders when a session moved between them — a
  session that changed folder went somewhere, it did not become two. All of that is
  evidence of one session, and the most recent of those records is the one that
  describes it, because it is the one the agent went on writing. The collapse happens
  before anything counts or truncates, so how many sessions an environment holds is a
  number of sessions and never a number of files.
- A session's `Session State` is **derived from the evidence available**, never
  asserted. Running and Stalled both require liveness evidence; with none, a session
  is Finished. A state no evidence supports is not reported at all — there is no
  Failed here, because a record that stops says nothing about why.
- The log **never fills a gap the agent left**. A fact an agent did not record is
  absent, not inferred: a repository guessed from a folder path is indistinguishable
  from a recorded one and wrong.
- The log **says how complete it is**. It may describe fewer sessions than exist, and
  when it does, how many exist is part of the answer rather than a detail the reader
  is left to discover. That completeness is the *reading's* and only the reading's:
  what a reader's chosen `Session View` then leaves off the screen is theirs, is
  applied after the log has answered, and is accounted for separately. Reporting one
  number for both subtractions would have the log confess to a shortfall the reader
  asked for.

**Records replicate; none of the above changes when they do.** Local ADR 0005 puts
session records in the same cloud replica as tasks, so a record gathered on one
machine can be read on another. That path is built rather than intended now,
though it is off unless switched on and has run against nothing provisioned —
which decides whether another machine's sessions are in front of a reader, and
decides nothing about what one of them means. Every invariant survives intact,
because the thing that replicates is a record, not an authority:

- **Still single-writer.** A session ran on one environment and only that
  environment holds the evidence for it, so only that environment writes records
  for it. There is nothing to reconcile: no second version, no last-write-wins,
  and no lost-edit failure mode. A session that moves gets a later record rather
  than an edit to an earlier one, which is the shape this context already has.
  None of that rests on good behaviour: a record is addressed by the environment
  that gathered it before it is addressed by anything else, so an environment can
  only ever write records of its own. The matching rule on the
  reading side is that a reading knows how it came by each record — read here, or
  arrived from elsewhere — and offers back only what it gathered itself. An
  environment that re-published another's records would be attaching somebody
  else's work to itself, which is the one way a single-writer rule fails without
  anyone editing anything.
- **Still one environment per log.** A reading that spans machines is a
  composition of several Session Logs, exactly as grouping by environment already
  is. A replicated record names the environment that gathered it, so nothing
  asserts a fact nobody gathered.
- **Still derived, never asserted.** `Session State` is worked out on every
  reading from the evidence that reached the reader, and never travels on the
  record: a state on the wire would freeze the gathering machine's reading of its
  own clock and go on saying `running` about a session that ended before the last
  exchange. A record whose environment is a machine away is stale rather than
  wrong, and the derivation says so from the timestamps it has. What it can say is
  narrower than what a local reading can. A record carries no liveness marker —
  the same record arrives whether the session is still going or ended a month ago
  — so silence past the `Stale Threshold` reads `finished` and never `stalled`,
  which is the rule this context already applies to the agent that leaves no
  marker locally. `stalled` is the claim that something is still there and quiet,
  and nothing in a record can tell that apart from something that is gone.
- **Only a whitelist travels**, fixed by local ADR 0005 and standing at nineteen
  fields: the `Session Identity` in full — the agent as well as the session id —
  the environment's id and the name that environment goes by, the repository
  **alias** rather than the working folder, the resolved repository's alias on the
  same terms (`#working-location`), the branch, the activity window, the turn and
  duration counts, the agent's active runs and waits as pairs of timestamps, the
  session's title, the delivery dashboards' worktree key, the usage-limit
  refusals the agent recorded, where the agent was run from, the pull requests the
  session linked, and the tokens it spent per model. Never prompts, never tool output, never file contents — which is
  the whole reason a record can leave the machine at all. The runs and waits are
  the same kind of fact as the activity window — instants, not content — and they
  travel so a reading machine can measure a session it never held the transcript
  for; a record may carry them, may carry an empty list where the agent's own
  format cannot mark a boundary, or may carry nothing where the gathering
  environment had no parsable activity record, and a reader keeps those three
  apart rather than reading an absence as a measurement of zero. A
  field not on the list does not travel, so `working_folder` does not: it
  describes one machine's disk and means nothing on the other. Its worktree key
  does — the leaf and a one-way hash — so a record can still meet the delivery
  runs filed under that folder. The title travels by the owner's decision of
  2026-09-23, although an agent writes it out of what the person typed: a session
  is recognised by its title, on another machine and once its transcript is gone,
  and by nothing else the record carries. A record from a device that predates the
  field is headed by its session id. A limit hit is the assistant's own record of
  a refusal — when, which bucket, when it resets, and what it said about paid
  overage — and never the refusal's sentence; it travels because a usage limit is
  the account's, so a refusal on one machine bounds the week on every other. The turn count keeps this
  context's own rule about gaps: an agent that records none sends none, rather
  than sending zero, because zero is a claim about a session where absence is
  merely the truth about a record. The duration is the one field carried for the
  transport's sake and read back from nowhere — a reading derives it from the two
  timestamps it already holds, so a third field cannot come to disagree with the
  operands it was computed from.
- **The list carries this context's identity in full, and local ADR 0005 settled
  it there rather than in the pushing code.** `Session Identity` is `agent` plus
  `session_id`, never `session_id` alone, because two agents may issue the same
  string; a record that travelled without its agent would let the receiving log
  merge two unrelated sessions — exactly the failure the identity rule exists to
  prevent. The agent travels, and the replica keys a record on the environment,
  the agent and the session id together, so two unrelated sessions are not merely
  kept apart by a receiving log that behaves — there is no one key under which
  they could arrive.
- **Retention is the store's, not this context's.** A replicated record expires
  after twelve months by container TTL. Nothing here reaps, and nothing here
  deletes a record.
- **An environment keeps a record of each of its own sessions, and the record
  answers once the transcript is gone.** The assistant cleans its transcripts away —
  Claude after a month by default — and a session read from nothing is a session the
  log no longer holds. So every reading amends a local `Session Record`, independent
  of sync: added to, never taken from — a value the reading lacks keeps what the
  record held, earlier runs and waits are kept, limit hits are a union. The answer
  for one session is chosen in this order: the files as read now, then this
  environment's record, then what sync carried. With sync on, the records an
  environment pushed also come back down the feed and are kept as a second copy; a
  record read back is never pushed again, so this does not make the log
  multi-writer, and those are shown under the environment's own identity rather
  than the machine id the service issued.

**Replication is not the Collections MCP and changes nothing about it.** The MCP
stays exactly what it already is: optional, reached through an anti-corruption
layer, never authoritative for whether a session existed. It supplies a second
kind of evidence about a session this context already knows; replication moves
records this context already holds. Neither stands in for the other, and a
replicated record with no activity stream is a perfectly good record — one that
may now carry the runs and waits its gathering environment folded, which are a
measure of the session's time and not the stream of what it did.

### Agent Session

```meta
type: entity
status: active
```

One working session of one agent. Identified by `Session Identity` — the agent plus
the identifier that agent gave the session — because a session id is only unique
within the agent that issued it.

Holds the environment it ran on — its display name plus its `EnvironmentId`, the
product-wide device identity issued by the shared kernel
(`.domain/tasks/naming.md#device`) rather than anything this context owns — a
display title, its `Working Location`, its `Activity Window`, and its
`Session State`. `EnvironmentId` is what a machine dimension keys on: the display
name alone can change without the session having moved to a different machine, so
a reader grouping or correlating sessions by machine reads the id, not the name.
When external activity evidence exists, the same entity also carries a `Session
Activity Stream`, a `Session Enrichment Summary`, and a `Reporting Capability`
reading for that stream.

Its lifecycle is not this context's to run: it appears when an agent first leaves a
record, and it stops changing when the agent stops writing.

### Session Identity

```meta
type: value-object
status: active
```

What makes a session that session: `agent` plus `session_id`. Equality by both.
Never by `session_id` alone — two agents may issue the same string, and an
environment reading both would silently merge two unrelated sessions.

### Working Location

```meta
type: value-object
status: active
```

Where the session was working: `working_folder`, optional `repository` in
`owner/name` form, and optional `branch`. Equality by all three.

Both fields are optional because of what the agents actually write down, not
because the value is unimportant. Copilot records the repository and the branch
outright. Claude records the branch — it states one in the transcript it keeps —
and never the repository, in any path: the folder is the only thing there is to go
on, and a repository read off a path leaf would be a wrong fact where absence is a
true one. So a Claude session carries a branch and no repository, and a Copilot
session carries both. Absent means "the agent did not say", and about the
repository Claude always says nothing.

A replicated session is the one case where the folder is missing for a different
reason. It was recorded, on the machine that ran the session, and it deliberately
did not travel — a path describes that machine's disk and would be acted on by
the machine that read it. Absent there means "cannot be shown here", not "was
never written down", and either way nothing on this side reconstructs one: the
log fills no gap an agent left, and it fills none the boundary made either.

Beside the recorded repository, and never in its place, the location may carry a
**resolved repository** (as of 2026-09-22): the `owner/name` of the registered
repository whose clone directory contains the working folder, on the environment
that read the session. This is not the inference the paragraph above rules out. A
path leaf is a guess about which of GitHub's several `Backlog`s a folder is a
clone of; a registered clone directory is a fact the person stated on the
Repositories screen, and a folder is either under it or not. It is still a fact
about the reading environment's registrations rather than about the session,
which is why it is a second field: the two fail differently, and a surface must
be able to tell "the agent said" from "this product placed". It resolves on the
environment that ran the session — the only one holding both the folder and the
clone — travels with the record, and is held as it arrived everywhere else. Where
no registered clone contains the folder it is absent, and absent still means
only that.

### Activity Window

```meta
type: value-object
status: active
```

When the session was alive: optional `started_at` and a required `last_activity_at`.
Equality by both. The start is optional because some evidence is only a file with a
timestamp on it; the last activity is not, because that timestamp always exists and
is what `Liveness Assessment` reads.

### Session Activity Stream

```meta
type: value-object
status: active
```

Optional externally reported activity about one known session, ordered as that
session reported it.

The stream is keyed by `Session Identity`, not by a collection row id, because the
context cares which session the activity belongs to and not how the storage happened
to key it. It is optional because a session without Collections MCP reporting is
still a perfectly good session record.

### Session Activity Entry

```meta
type: value-object
status: active
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
status: active
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
status: active
related: [.domain/sessions/features.md#delivery-runs-beside-their-sessions]
```

One orchestrated delivery run — a flow or orchestration skill driven through a
dashboard — as the run file records it. Identified by the run id within the worktree
folder it was filed under.

Two things write that file. A dashboard this product only reads from, which is where
every run came from at first; and this product itself, through `Delivery Run
Recording`, for a session that chose Backlog as the surface it reports to. The shape
is one shape either way, which is the point: a run being recorded here and one
imported from a dashboard's folder are the same kind of thing to everything that
reads them, and which of the two wrote it is provenance rather than a difference in
kind.

Holds what the file states: which dashboard wrote it, the worktree key it was filed
under, the skill, the title, the status word verbatim, the change kind, the
`Delivery Run Reference`s it names, when it started and was last updated, its stages with their status, duration
and how many times each was marked done, its token usage in total, for delegated
agents and per stage with the models seen, the owner session's context gauge with its
peak, and its tool activity summed by category and by MCP server. It also carries the
environment its file was read on, stamped by the source the way a session's is: a
run file found here was written here.

The worktree key is the dashboards' own — the folder's leaf and eight hex characters
of a hash of its lower-cased path — and it is one-way: the path is not recoverable
from it, but the same key can be derived from a session's `Working Location`, which is
what `Run Attachment` does.

**A run can name the sessions that drove it, and then no guess is made.** As of
2026-09-23 a run started through `Delivery Run Recording` with the caller's session id
carries it in `sessionIds`; a session that picks the run back up is appended beside it,
never over it. Where a run names a session the list holds, it joins that session by
identity; only a run that names none — every dashboard file written before, and any
writer that does not send one — is attached by worktree key and overlapping activity.

*Delivery* run, with the adjective: this context already has a run, the stretch of
transcript in which an agent was producing, derived here from the agent's own record.
This one is another tool's record of work it was tracking, with a status that tool
assigned. The two share a word and nothing else.

Its status is never derived from a reading: the dashboards wrote seven spellings for
it between two generations, and mapping them onto fewer members would be deciding
what a writer meant rather than reporting what it wrote. A surface that needs a chip
normalises at the last moment, where a new spelling degrades to a plain label rather
than to a dropped row.

**Reading accepts every spelling; writing uses one.** The asymmetry is deliberate and
is not a gap to close. A run imported from a dashboard is somebody else's record and
is reported as written, `completed` and `success` included. A run this context records
is its own, and has no such excuse — it writes `in_progress` while under way and ends
on `done`, `blocked`, `parked` or `cancelled`. Reviving an older generation's spelling
from here would make the drift permanent rather than historical.

### Delivery Run Recording

```meta
type: domain-service
status: active
related: [.domain/sessions/domain.md#delivery-run, .domain/sessions/features.md#record-a-run-as-it-happens]
```

The capability a session uses to record a `Delivery Run` here as it happens, rather
than this context finding one afterwards in a folder another tool wrote. Eight
operations, named by the delivery engine and not by this context: bring the surface
forward, start a run, record a prompt, set a run's context, update a stage, finish a
run, list runs, get one.

**The names belong to the engine.** A session finds a surface by matching operation
names against the tools it has, so these are a wire contract with something outside
this repository: renaming one is not a rename, it is a surface that stops being
found, with the session reporting no surface attached — which is also a normal
outcome, and so indistinguishable from the fault.

**Scope is a worktree the caller states, never a folder this product is sitting in.**
The process recording a run is the application's, and one clone's worktrees share a
repository while having as many directories. The caller names the folder its run is
about, and the worktree key is derived from it — the same derivation `Run Attachment`
runs from the other side, which is what puts a recorded run and the session that drove
it on one row.

**Starting a run for a skill already under way reattaches to it.** A session that
picks work back up says so by starting again, and a second file would not be a second
run — it would be the one run, listed twice, with its stages split between them. A run
that has finished is not reattached to.

**A stage is addressed by its position, not its name.** The whole stage list is fixed
when the run begins, so the surface can show what is still to come; a name is a label
on a position rather than a key, and two stages may carry one name. An index outside
the list is refused rather than ignored, because a caller one stage off is reporting
the wrong stage for the rest of the run and a silent nothing leaves it doing so.

**It records what it observes and claims nothing else.** The model a run resolved is
something the run states, so it is kept. What a run consumed is measured by watching a
session's own tool calls, which this product does not see — it sees a call arrive, not
the session that made it — so those figures are absent, and absent is shown as left
out rather than as zero.

### Delivery Run Reference

```meta
type: value-object
status: active
related: [.domain/sessions/features.md#the-work-a-run-is-linked-to]
```

One piece of work a `Delivery Run` is linked to: the Backlog entry it was started
from, a tracker issue it was working, or a pull request it opened. Carries which of
those it is, what to call it, the item's own title where the file recorded one, its
address where it has one, and the repository it belongs to. Equality by all of them.

A list on the run rather than a single tracker item, because a run has more than one
link and they are not the same link — it starts from an item and ends in a pull
request — and a reader wants to reach either. The address is optional and its absence
is a fact rather than a gap: a Backlog entry is in this product, not on a page, and
nothing here invents a URL for one. Such a reference carries the plan it belongs to
beside its id, because the pair is what identifies an entry across plan versions, and
reaching one is the surrounding application's to do — this context names the entry and
never opens it. A surface shows it as the run's own outcome rather than as a chip of
its own: the entry is the work the run was answering about, and two controls to one
place is one too many.

No state rides on a reference. Whether the issue is open or the pull request merged is
a reading of the tracker, and this context has not performed one — a `Session Log`
that reported one would be filling a gap nobody looked into, which is the invariant it
already holds for a session's own fields.

### Session Row

```meta
type: value-object
status: active
related: [.domain/sessions/domain.md#run-attachment, .domain/sessions/features.md#delivery-runs-beside-their-sessions]
```

One row of the session list: an `Agent Session` and the `Delivery Run`s it drove, or
a single `Delivery Run` the list holds no session for. The unit the list shows,
because a session and a run describe the same work from two sides and either may be
the one this environment still has.

Every fact read off a row comes from whichever source has it, and the row says nothing
either source did not: a run-only row is titled by its worktree, dated by the run,
placed on the environment the file was read on, and Finished — there is no liveness
evidence for it. A session row's repository is the session's, and where the agent
recorded none, the tracker item a run of it named: a recorded fact from a second
source, not the guess from a path the Session Log forbids.

### Delivery Run Catalog

```meta
type: value-object
status: active
```

What a reading of one environment's dashboards answers with: every `Delivery Run`
every readable folder holds, and the dashboards or folders that could **not** be read,
by name. No count of what was left out, because nothing is: a run is work somebody
started on purpose, there are hundreds rather than thousands, and most of them attach
to rows the list already has. Equality by both parts.

### Session Catalog

```meta
type: value-object
status: active
```

What a reading of one environment answers with: the `Agent Session` list it will
describe, the sources it could **not** read, by name, and how many sessions it
discovered before any limit was applied.

Three parts rather than one list, because "no sessions" and "could not look" are
different answers and a reader who cannot tell them apart will go looking for a
fault that is not there. Equality is by all three.

### Agent

```meta
type: enum
status: active
```

Which assistant ran the session: `claude`, `copilot`.

A type on the session rather than a list per vendor. A list per vendor would make
"what has been running here" unanswerable without first knowing how many vendors
there are.

### Session State

```meta
type: enum
status: active
```

How far along a session is, as far as the evidence goes:

- `running` — the agent is present on the environment and something moved recently.
- `stalled` — still registered as present, but nothing has moved for longer than the
  `Stale Threshold`. A left-open window, usually.
- `finished` — over. Only its record is left.

Three values, and deliberately not five. Starting, waiting and failed are all
states an agent could be in and none is derivable from what it leaves behind, so
this context does not claim them.

### Reporting Capability

```meta
type: enum
status: active
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
status: active
related: [.domain/sessions/domain.md#delivery-run, .domain/sessions/domain.md#session-identity, .domain/sessions/features.md#under-the-session-that-drove-it]
```

Builds the list of `Session Row`s from the sessions and the runs: which `Agent
Session` each `Delivery Run` ran in, and which runs stand on their own. A pure
function over what it is given, like `Session Grouping`.

Two conditions, and both are needed. **The worktree:** the run's key must equal the
key derived from the session's `Working Location` — or from a folder above it, since
a session may be started below the worktree's top level — compared without regard to
letter case, because the dashboard slugs git's casing of the leaf and a session
records the folder as it was launched in. **The window:** that session's `Activity
Window` must overlap the run's. The worktree alone names a place, and a worktree is
worked in by several sessions over its life; only the overlap names a time. Where more
than one session passes both, the one whose start is nearest the run's own wins — the
session that opened the run started with it. A run whose file does not date its start
is placed by its last update, the one moment it is known to have existed.

A run that matches nothing becomes a row of its own, never dropped: its session may be
older than the `Session Limit` keeps or may have been read on another environment, and
neither is a reason to hide the work. The rows come back in the order the sessions
were given, then the run-only rows most recently updated first; ordering for display
is `Session Grouping`'s job.

## Liveness Assessment

```meta
type: domain-service
status: active
related: [.domain/sessions/domain.md#session-state, .domain/sessions/flow.md]
```

Decides whether a session with liveness evidence is `running` or `stalled`, by
comparing its `last_activity_at` against the `Stale Threshold`.

A service rather than a property of the session, because the answer depends on the
current time and an `Agent Session` holds no clock. That is also why the threshold is
this context's and not a rendering concern: "how long is too long" is a policy, and a
policy that lived in a control library would be a product rule in the one place
nobody would look for it.

Invocation semantics: query-oriented; evaluated per reading, never persisted. A
session is not moved into `stalled` by anything — it simply reads as stalled while
the silence lasts, and reads as running again the moment the agent writes.

## Session View

```meta
type: domain-service
status: active
related: [.domain/sessions/domain.md#session-log, .domain/sessions/domain.md#session-state, .domain/sessions/features.md#open-on-the-live-sessions, .domain/sessions/naming.md#session-view]
```

Narrows a set of `Agent Session`s to the ones a reader currently wants in front of
them: the live ones, or all of them.

It decides nothing about liveness. The `Session Log`'s invariant on derived
`Session State` has already settled that question, and this service does no more than
read the answer — which is why "live" here can only mean running or stalled. A second
place that worked out for itself whether a session was still going would be a second
definition of live, free to drift from the first, and the two would disagree first on
exactly the sessions a reader most needs to trust.

A service of its own rather than one more member of `Session Grouping`, and that is
the part worth arguing. Grouping guarantees that every session in is a session out; a
"live" grouping would falsify that guarantee while sitting in the same strip as
environment and agent, leaving the reader one control whose options sometimes
rearrange the list and sometimes shorten it. Two operations with two guarantees is the
honest shape: exactly one of them removes sessions, and the surface can therefore say
which one did.

**View first, then grouping.** A set is narrowed and then carved up, never the reverse,
so an environment with nothing live on it loses its section rather than keeping an
empty one. The view also does not reorder — ordering is the grouping's answer to give,
and a narrowing that sorted would be a second answer to what "most recently active
first" means.

Pure, like `Session Grouping`: no clock, no I/O, no state. The clock was already spent
by `Liveness Assessment`, which is what lets a reader change view without anything
being read again — and what makes two views of one reading two renderings of the same
facts rather than two readings that might disagree.

Invocation semantics: query/composition-oriented; invoked per view, never stored. Which
view is in force is the reader's choice, is not part of this context's state, and is
not part of what a reading returns — a surface holding it starts at the live view every
time it is opened.

## Session Grouping

```meta
type: domain-service
status: active
related: [.domain/sessions/domain.md#session-log, .domain/sessions/features.md#session-grouping]
```

Carves a set of `Agent Session`s into named groups — one per environment, or one per
agent — each ordered most recently active first, with the groups themselves in a
stable order.

A service because grouping spans sessions rather than belonging to any one of them,
and a pure one: no clock, no I/O, no state. Two properties it guarantees, both of
which a reader relies on without being told:

- **Grouping rearranges and never filters.** Every session in, every session out. A
  count taken before grouping is still correct after it.
- **Group order does not depend on group size.** Environments sort by name and agents
  in the order the `Agent` enum declares them, so a section does not move under the
  reader as sessions come and go.

Invocation semantics: query/composition-oriented; invoked per view, never stored.
Which grouping is in force is the reader's choice and is not part of this context's
state.

## Session Activity Publishing

```meta
type: domain-service
status: proposed
related: [.domain/sessions/features.md#session-activity-enrichment, .domain/sessions/flow.md, .domain/sessions/dependencies.md]
```

Emits sanitized activity updates for a running session to the optional Collections
MCP when that session has reporting configured and reaches a meaningful milestone.

The service publishes the beginning of a session, meaningful activity deltas while it
runs, and a final update when it finishes. It never publishes prompts, transcripts,
tool output bodies, or secrets; it publishes only the coarse facts this context is
willing to read back later as activity evidence.

Invocation semantics: event-triggered policy/process-manager behavior; invoked from
session milestones, queued asynchronously, retried without blocking session work, and
allowed to degrade to local-only behavior when reporting is unavailable.

## Session Activity Enrichment

```meta
type: domain-service
status: proposed
related: [.domain/sessions/features.md#session-activity-enrichment, .domain/sessions/flow.md, .domain/sessions/dependencies.md]
```

Layers optional `Session Activity Stream` evidence onto the locally read `Agent
Session` so the session row can say more than the local record alone can say.

The service de-duplicates entries by external `event_id`, orders them by per-session
sequence and occurrence time, and derives the `Session Enrichment Summary` and
`Reporting Capability` that belong on the session. Local evidence stays authoritative
for whether the session exists, where it worked, and what `Session State` it reads as;
external activity enriches and never replaces those facts.

Invocation semantics: query/composition-oriented; evaluated per reading when external
activity evidence exists, and skipped entirely when it does not.

## Shared Value Objects

```meta
type: shared-value-objects
status: active
```

> Value Objects used by more than one aggregate in this bounded context.

Sessions has a single aggregate; every value object is documented under it.
This chapter is reserved for the day a second aggregate arrives — a fleet-level view
would be the likely first one.

## Shared Enums

```meta
type: shared-enums
status: active
```

> Enums used by more than one aggregate in this bounded context.

Single aggregate, so `Agent` and `Session State` are documented under it. Reserved
for the same reason as the chapter above.
