# Requirements

```meta
type: requirements
```

> What this context's features guarantee, one chapter per feature. Each
> requirement is one SHALL sentence with the scenarios that prove it. A feature
> chapter says what the capability is and why it exists; this file says what it
> promises.

## Name the source that could not be read

```meta
type: requirements
related: [.devbook/domain/sessions/features.md#name-the-source-that-could-not-be-read]
```

### Requirement: One unreadable source costs only its own sessions

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.SessionsPaneTests.An_unreadable_source_is_named_above_the_rows_that_did_arrive, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.CompositeAgentSessionSourceTests.Unreadable_is_the_union_and_names_each_source_once]
```

The system SHALL show the sessions of every source it could read when another source
cannot be read, and name the one it could not read.

#### Scenario: One agent's records cannot be read

- **Given** a machine where Claude's records can be read and Copilot's cannot
- **When** the person opens the session list
- **Then** Claude's sessions are listed and Copilot is named as a source that could not be read

## Only what the dashboard recorded

```meta
type: requirements
related: [.devbook/domain/sessions/features.md#only-what-the-dashboard-recorded]
```

### Requirement: A figure a run did not measure is left out

```meta
type: requirement
tests: unit:dotnet:Backlog.Desktop.UI.UnitTests.SessionsPaneRunTests.A_run_this_product_recorded_claims_no_figure_it_could_not_measure
```

The system SHALL leave out a run's token, tool-call and context figures that nobody
measured, rather than show them as zero.

#### Scenario: A run recorded here without telemetry

- **Given** a run this product recorded, whose session reported no telemetry
- **When** the person reads the run's line and opens its fold
- **Then** the stage count is shown and no token, tool-call or context figure is shown

## What a run cost, reported by the session itself

```meta
type: requirements
related: [.devbook/domain/sessions/features.md#what-a-run-cost-reported-by-the-session-itself, .devbook/domain/sessions/domain.md#delivery-run-telemetry]
```

> [Delivery Run Telemetry](domain.md#delivery-run-telemetry) is the reaction that
> keeps these promises.

### Requirement: A session's telemetry reaches only a run it drives

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunTelemetryTests.Another_sessions_run_is_left_alone, unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunTelemetryTests.A_run_with_no_session_yet_is_adopted_by_the_first_one_that_reports, unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunTelemetryTests.A_session_in_a_subfolder_reports_to_the_worktree_above_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunTelemetryTests.A_finished_run_takes_no_more_telemetry]
```

The system SHALL record a session's telemetry only on the run in progress in that
session's worktree that lists the session, or that lists no session yet.

#### Scenario: Another session's run in the same worktree

- **Given** a run in progress that lists another session
- **When** a session working in the same worktree reports a tool call
- **Then** the run records nothing from it

#### Scenario: A run no session has joined yet

- **Given** a run in progress that lists no session
- **When** a session working in that worktree reports a tool call
- **Then** the run records the call and lists that session

### Requirement: A message's tokens are counted once

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunTelemetryTests.Tokens_are_folded_from_the_transcripts_once_per_message, unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunTelemetryTests.A_line_still_being_written_waits_for_the_next_event]
```

The system SHALL count each transcript message's tokens on the run once, however many
events follow it and however many lines the message is written over.

#### Scenario: A message written as several lines

- **Given** a run whose session wrote one message as two lines, each repeating its usage
- **When** the session reports its next event
- **Then** the run counts that message's tokens once

### Requirement: A run never counts what came before its session was seen

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunTelemetryTests.A_session_with_no_run_records_nothing_and_a_later_run_does_not_absorb_its_past, unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunTelemetryTests.Tokens_are_folded_from_the_transcripts_once_per_message]
```

The system SHALL start counting a session's transcript at the end of what was written
when telemetry first saw the session on a run.

#### Scenario: A run started late in a conversation

- **Given** a session that has already written messages with no run under way
- **When** a run starts in it and the session writes one more message
- **Then** the run counts that one message and none of the earlier ones

### Requirement: A delegated agent with no figures adds nothing

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunTelemetryTests.A_delegated_agent_without_totals_records_none, unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunTelemetryTests.A_delegated_agent_carries_the_totals_its_response_reported]
```

The system SHALL record a delegated agent's tokens and tool calls only where the Agent
call's response stated them, and record no zero in their place.

#### Scenario: A response that states no totals

- **Given** a run in progress
- **When** its session reports an Agent call whose response states no tokens and no tool calls
- **Then** the agent's record on the run carries neither figure

### Requirement: Telemetry never fails the tool call it reports on

```meta
type: requirement
tests: unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunTelemetryTests.Malformed_events_are_recorded_as_nothing
```

The system SHALL take an event it cannot attribute or read without recording anything
and without raising an error to the hook that reported it.

#### Scenario: A malformed event

- **Given** a run in progress
- **When** a hook reports an event that is not an object, or names a transcript that does not exist
- **Then** the run records nothing and the report completes without an error

## Share of the week and the weekly limit

```meta
type: requirements
related: [.devbook/domain/sessions/features.md#share-of-the-week-and-the-weekly-limit]
```

### Requirement: A share of the weekly limit is labelled an estimate

```meta
type: requirement
tests: unit:dotnet:Backlog.Desktop.UI.UnitTests.SessionsPaneDetailTests.After_a_weekly_refusal_the_share_carries_an_estimate_of_the_limit
```

The system SHALL mark every percentage of a weekly limit it shows as an estimate.

#### Scenario: After a weekly refusal

- **Given** a weekly-limit refusal has been recorded and a session has reported cost this week
- **When** the person opens that session's detail
- **Then** its share of the weekly limit is shown marked as an estimate

## Start a session from an entry

```meta
type: requirements
related: [.devbook/domain/sessions/features.md#start-a-session-from-an-entry, .devbook/domain/sessions/domain.md#session-request-queue]
```

> Local ADR 0038 decides these requirements. Both sides are built. The queue icons
> create a request on an entry, on a plan's roadmap item, and beside a selected plan
> filter chip. The dispatcher side is the wait route and the claim, mark and list tools
> a Manager session uses.

### Requirement: Only a click in the app requests a session

```meta
type: requirement
```

The system SHALL create a session request only when the person queues a session for an entry or a plan in the desktop app.

#### Scenario: A person starts a session on an entry

- **Given** the `session-requests` flag is on and a prompt entry targets one repository with a known clone folder
- **When** the person presses the entry's queue icon
- **Then** an entry request for that entry, repository and clone folder is Requested

#### Scenario: An imported entry asks for a session in its text

- **Given** an entry whose body says to start a session
- **When** the entry is imported, synced or captured
- **Then** no session request is created

### Requirement: A dispatcher is handed no entry text

```meta
type: requirement
```

The system SHALL hand a dispatcher a request's kind, entry id or plan tag, repository and clone folder, and nothing an entry says.

#### Scenario: A dispatcher receives an entry request

- **Given** an entry request for an entry whose body holds instructions
- **When** a dispatcher waiting on the listener receives the request
- **Then** the request it receives names the entry id, the repository and the clone folder and holds no text of the entry

### Requirement: A waiting dispatcher gets the oldest request first

```meta
type: requirement
```

The system SHALL answer a dispatcher waiting on `GET /session-requests/wait` with the oldest Requested request as soon as one exists.

#### Scenario: A request arrives while a dispatcher waits

- **Given** a dispatcher waiting on the route and no request Requested
- **When** the person presses the queue icon on an entry
- **Then** the waiting call answers with that request

#### Scenario: Nothing arrives

- **Given** a dispatcher waiting on the route and no request Requested
- **When** 50 seconds pass with no request
- **Then** the call answers with no request and the dispatcher waits again

### Requirement: The wait takes the MCP listener's token

```meta
type: requirement
```

The system SHALL refuse a call to `GET /session-requests/wait` that does not carry the MCP listener's bearer token.

#### Scenario: A call without the token

- **Given** a request is Requested
- **When** a caller on this machine waits on the route without the bearer token
- **Then** the call is refused and the request stays Requested

### Requirement: Only one dispatcher holds a request

```meta
type: requirement
```

The system SHALL let only one dispatcher claim a request at a time.

#### Scenario: Two dispatchers claim the same request

- **Given** a request is Requested and two dispatchers received it
- **When** both claim it
- **Then** the first claim holds it and the second is refused

### Requirement: A claim that is not answered in time lapses

```meta
type: requirement
tests: [integration:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteSessionRequestStoreTests.A_lapsed_claim_reads_as_Requested_and_is_handed_out_again, integration:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteSessionRequestStoreTests.A_late_report_from_the_lapsed_holder_writes_Started_while_nobody_else_claimed, integration:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteSessionRequestStoreTests.A_late_report_is_refused_once_another_dispatcher_claimed_in_between]
```

The system SHALL hand a Claimed request to the next dispatcher once two minutes pass without a reported session, and accept the late report of the dispatcher that claimed it until another dispatcher claims it.

#### Scenario: A dispatcher stops mid-dispatch

- **Given** a dispatcher claimed a request
- **When** two minutes pass without it reporting a session
- **Then** the request is Requested again, and the next waiting dispatcher receives and claims it

#### Scenario: A slow start is reported after the lease ran out

- **Given** a dispatcher claimed a request and no other dispatcher claimed it since
- **When** it reports its started session more than two minutes after the claim
- **Then** the request is Started with that session

#### Scenario: A late report after another dispatcher claimed

- **Given** a dispatcher's claim lapsed and a second dispatcher claimed the request
- **When** the first dispatcher reports its started session
- **Then** the report is refused and the request stays Claimed by the second dispatcher

### Requirement: A request waits until it is answered or cancelled

```meta
type: requirement
```

The system SHALL keep a request until a session answers it or the person cancels it, however long no dispatcher runs.

#### Scenario: No dispatcher runs for days

- **Given** a request was made while no dispatcher was running
- **When** a dispatcher starts days later
- **Then** it receives that request

#### Scenario: The person cancels a waiting request

- **Given** a request is Requested
- **When** the person cancels it on the entry
- **Then** the request is Cancelled and no dispatcher receives it

### Requirement: The started session links the request

```meta
type: requirement
```

The system SHALL mark a Started request Linked when the session its dispatcher reported calls `link_session` on its entry.

#### Scenario: The started session picks the entry up

- **Given** an entry request a dispatcher reported as Started with a session id
- **When** that session calls `link_session` on the entry
- **Then** the request is Linked and the entry shows the session instead of the waiting request

### Requirement: The flag off offers no session requests

```meta
type: requirement
```

The system SHALL offer no queue icon, no wait route and no dispatcher tool while the `session-requests` flag is off.

#### Scenario: The flag is off

- **Given** the `session-requests` flag is off
- **When** the person opens a prompt entry and a dispatcher calls the wait route
- **Then** the entry offers no queue icon, the route answers `404` and the MCP endpoint lists no dispatcher tool

## Answer a session's question from elsewhere

```meta
type: requirements
related: [.devbook/domain/sessions/features.md#answer-a-sessions-question-from-elsewhere, .devbook/domain/sessions/domain.md#question-relay, .devbook/arc42/adr/0041-agent-questions-reach-backlog-by-presence-and-come-back-through-the-hook-or-the-manager.md]
```

> Local ADR 0041 decides these requirements. None of them is built yet.

### Requirement: A question asked while the person is away is notified at once

```meta
type: requirement
```

The system SHALL publish as a notification a question asked while the person is away: no keyboard or mouse input for the away setting, 10 minutes by default, or a locked screen.

#### Scenario: The person left the desk

- **Given** the `notifications` flag is on and there has been no input on the desktop for 12 minutes
- **When** a Claude session asks a question through `ask` with its session id
- **Then** the question is Notified, a notification about the session is published, and `ask` answers the route `backlog`

#### Scenario: The screen is locked

- **Given** the screen of the desktop is locked
- **When** a session asks a question
- **Then** the question is Notified at once

### Requirement: A question asked at the keyboard waits before it is notified

```meta
type: requirement
```

The system SHALL keep a question asked while the person is at the keyboard in the session, and publish it only if it is still open after the notify setting, 15 minutes by default.

#### Scenario: Answered in time

- **Given** the person typed a minute ago and a session opens a question dialog
- **When** the person answers the dialog after five minutes
- **Then** nothing is published, and the question is Answered

#### Scenario: Left open

- **Given** the person typed a minute ago and a session asks in prose
- **When** 15 minutes pass with no answer
- **Then** the question is Notified and a notification about the session is published

### Requirement: Coming back never takes a question back

```meta
type: requirement
```

The system SHALL keep a Notified question published when the person returns to the keyboard.

#### Scenario: Back at the desk

- **Given** a question was Notified while the person was away
- **When** the person moves the mouse and opens Backlog
- **Then** the question stays Notified and its notification stays Open

### Requirement: An answer in the session closes the question

```meta
type: requirement
```

The system SHALL close a question answered in the session, with the answers, and close each notification it published.

#### Scenario: Answered at the desk after it was notified

- **Given** a Notified prose question
- **When** the person types the answer into the session
- **Then** the question is Answered, its notification closes showing it was answered in the session, and no delivery is made

### Requirement: A dialog asked while the person is away is answered through the hook

```meta
type: requirement
```

The system SHALL give the hook holding a dialog asked while away the accepted answers through `get_answer`, holding each call at most 55 seconds, up to the dialog wait setting, 8 hours by default.

#### Scenario: Answered on the phone

- **Given** the person is away and a session opens a question dialog with two questions
- **When** the person answers both on the phone and the owner desktop accepts the replies
- **Then** the hook's waiting `get_answer` answers with both answers, the session receives them as the dialog's answers, and the dialog never opened

#### Scenario: No answer within the wait

- **Given** a dialog's hook has waited the whole dialog wait
- **When** the wait runs out
- **Then** the question closes as timed out and the dialog opens in the session

### Requirement: Any other answer reaches the session through the Manager

```meta
type: requirement
```

The system SHALL turn the answer to a prose question or an escalated dialog into a delivery the dispatch wait route hands a waiting Manager before any session request.

#### Scenario: A prose question answered on the phone

- **Given** a Notified prose question and a Manager waiting on `GET /session-requests/wait` with a session request also Requested
- **When** the person answers on the phone and the owner desktop accepts it
- **Then** the wait route answers with the delivery, carrying the session id, the question and the answer, before the session request

#### Scenario: The session is gone

- **Given** a Manager claimed a delivery
- **When** it cannot reach the session and marks the delivery released
- **Then** the delivery is Pending again

### Requirement: A waiting session is not working

```meta
type: requirement
```

The system SHALL count a session with an open question as waiting, not working, in the dispatch capacity rule.

#### Scenario: Three sessions wait on questions

- **Given** three dispatched sessions each hold an open question
- **When** a Manager checks its capacity
- **Then** none of the three counts as working, and it may start the next session request

### Requirement: The relay sits behind the notifications flag

```meta
type: requirement
```

The system SHALL register no question and publish nothing for a session while the `notifications` flag is off.

#### Scenario: The flag is off

- **Given** the `notifications` flag is off
- **When** a session's hook calls `ask` with its session id
- **Then** `ask` answers that notifications are not available, and the session asks in place

## Schedules and their runs

```meta
type: requirements
related: [.devbook/domain/sessions/features.md#schedules-and-their-runs, .devbook/domain/sessions/domain.md#schedule-register]
```

> Local ADR 0051 decides these requirements. None is built yet.

### Requirement: A schedule is known only from its runs' reports

```meta
type: requirement
```

The system SHALL learn of a schedule and its runs only from the start and finish reports its runs send over MCP, and SHALL read no file Claude Desktop keeps for its schedules or runs.

#### Scenario: A schedule's first run

- **Given** a schedule Backlog has no report of
- **When** its first run reports its start with the schedule's id, title, repository and cron
- **Then** the schedule appears on the Schedules page with that run reading No summary yet

#### Scenario: A schedule that never ran

- **Given** a schedule in Claude Desktop that has not fired yet
- **When** the person opens the Schedules page
- **Then** the schedule is not listed

### Requirement: A run reads the state its reports and its session give it

```meta
type: requirement
```

The system SHALL read a run as Needs you, Held, Failed, Review ready or No summary yet from its reports while its session is not archived, and as Handled once its session is archived.

#### Scenario: A run that needs the person

- **Given** a weekly update run that reported two Needs-you lines
- **When** the person opens the Schedules page
- **Then** the schedule reads Needs you and its card shows the two lines

#### Scenario: A held schedule

- **Given** an issue sweep whose Friday run is not archived
- **When** Monday's run reports that it skipped itself for that reason
- **Then** the schedule reads Held, and Monday's run is a Skipped line under it

#### Scenario: The person archives the run

- **Given** a schedule that reads Needs you
- **When** the person archives the run's session in Claude Desktop
- **Then** the run reads Handled at the next reading of the session records

### Requirement: A schedule that does not start in time reads Missed

```meta
type: requirement
```

The system SHALL read a schedule as Missed when 30 minutes have passed since its cron's due time and no run reported its start.

#### Scenario: The machine was asleep

- **Given** a schedule due at 05:30 whose runs have reported before
- **When** it is 06:01 and no run has reported its start since 05:30
- **Then** the schedule reads Missed

### Requirement: A schedule that needs the person raises one alert per cause

```meta
type: requirement
```

The system SHALL raise one Alert, with its Windows notification, when a schedule turns Needs you, Held, Failed or Missed, and SHALL close it when its cause clears.

#### Scenario: A held sweep skips again

- **Given** an issue sweep that reads Held and has raised its alert
- **When** its next run skips itself too
- **Then** no second alert is raised

#### Scenario: The cause clears

- **Given** an Open alert that a weekly update needs the person
- **When** the person archives that run's session
- **Then** the alert closes

#### Scenario: Ready for review

- **Given** a run that reported work ready for review
- **When** the schedule reads Review ready
- **Then** the row shows it and no alert is raised

## Loop sessions

```meta
type: requirements
related: [.devbook/domain/sessions/features.md#loop-sessions, .devbook/domain/sessions/domain.md#loop-register]
```

> Local ADR 0051 decides these requirements. None is built yet.

### Requirement: A loop session's state is read from its check-ins

```meta
type: requirement
```

The system SHALL read a loop session as Listening when it checked in within 2 minutes, Holding when it checks in while work waits and a capacity limit is reached, Stalled when its process is alive with no check-in past its heartbeat plus 10 minutes, and Not running when it has no process or its session is archived.

#### Scenario: A Manager waits for requests

- **Given** a Manager whose wait call reached Backlog 40 seconds ago
- **When** the person opens the Sessions list
- **Then** its row in the Agents section reads Listening

#### Scenario: Usage is over the limit

- **Given** a Manager with a usage limit of 80% and three requests waiting
- **When** plan usage reads 84% at its check-in
- **Then** it reads Holding and names the usage limit

#### Scenario: A hung loop

- **Given** a Manager with a 30-minute heartbeat whose session still has a process
- **When** 41 minutes pass with no check-in
- **Then** it reads Stalled

### Requirement: The check-in answer carries the person's wish and the work waiting

```meta
type: requirement
```

The system SHALL answer every `check_in` with the work waiting for the loop, whether there is room for it, and the `pause`, `resume` or `stop` the person last set on the loop.

#### Scenario: The person pauses the Manager

- **Given** a Listening Manager
- **When** the person pauses it and it next checks in
- **Then** the answer carries `pause`, and the Manager starts no request until an answer carries `resume`

### Requirement: The app measures capacity for a loop

```meta
type: requirement
```

The system SHALL measure the dispatched sessions working, plan usage from the Claude usage cache, CPU and free memory, and SHALL count no Waiting session as working.

#### Scenario: Sessions waiting on questions

- **Given** a Manager with a limit of three sessions, and three dispatched sessions all Waiting
- **When** it checks in with a request waiting
- **Then** the answer says there is room

### Requirement: A loop that stops raises one alert

```meta
type: requirement
```

The system SHALL raise one Alert, with its Windows notification, when a loop turns Stalled, or Not running while work waits for it, and SHALL close it when the loop checks in again.

#### Scenario: The Manager stops with requests waiting

- **Given** a Manager with three session requests waiting
- **When** its session is archived
- **Then** one alert says the dispatcher stopped and how many requests wait

#### Scenario: The Brain tender stops with nothing waiting

- **Given** a Brain tender with no work waiting
- **When** its process ends
- **Then** it reads Not running and no alert is raised

### Requirement: Schedules and loop sessions leave with the Sessions area

```meta
type: requirement
```

The system SHALL offer no Schedules page, Agents section, run report tool or `check_in` tool, and raise no schedule or loop alert, while the `sessions` flag is off.

#### Scenario: The flag is off

- **Given** the `sessions` flag is off
- **When** a scheduled run's hook reports its start
- **Then** the report tool is not on the MCP endpoint, and nothing is recorded
