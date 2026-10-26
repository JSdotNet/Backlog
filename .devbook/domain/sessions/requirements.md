# Requirements

```meta
status: draft
type: requirements
```

> What this context's features guarantee, one chapter per feature. Each
> requirement is one SHALL sentence with the scenarios that prove it.

## Name the source that could not be read

```meta
status: draft
type: requirements
related: [.devbook/domain/sessions/features.md#name-the-source-that-could-not-be-read]
```

> The requirements of one feature. The feature chapter itself says what the
> capability is and why it exists; this says what it promises.

### Requirement: One unreadable source costs only its own sessions

```meta
status: draft
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
status: draft
type: requirements
related: [.devbook/domain/sessions/features.md#only-what-the-dashboard-recorded]
```

> The requirements of one feature. The feature chapter itself says what the
> capability is and why it exists; this says what it promises.

### Requirement: A figure a run did not measure is left out

```meta
status: draft
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
status: draft
type: requirements
related: [.devbook/domain/sessions/features.md#what-a-run-cost-reported-by-the-session-itself, .devbook/domain/sessions/domain.md#delivery-run-telemetry]
```

> The requirements of one feature. The feature chapter itself says what the
> capability is and why it exists; this says what it promises.
> [Delivery Run Telemetry](domain.md#delivery-run-telemetry) is the reaction that
> keeps them.

### Requirement: A session's telemetry reaches only a run it drives

```meta
status: draft
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
status: draft
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
status: draft
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
status: draft
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
status: draft
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
status: draft
type: requirements
related: [.devbook/domain/sessions/features.md#share-of-the-week-and-the-weekly-limit]
```

> The requirements of one feature. The feature chapter itself says what the
> capability is and why it exists; this says what it promises.

### Requirement: A share of the weekly limit is labelled an estimate

```meta
status: draft
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
status: draft
type: requirements
related: [.devbook/domain/sessions/features.md#start-a-session-from-an-entry, .devbook/domain/sessions/domain.md#session-request-queue]
```

> The requirements of one feature. The feature chapter itself says what the
> capability is and why it exists; this says what it promises. Local ADR 0037 decides
> them. Both sides are built: the queue icons that create a request (on an entry,
> on a plan's roadmap item, and beside a selected plan filter chip), and the
> dispatcher side, the wait route and the claim, mark and list tools a Manager
> session uses.

### Requirement: Only a click in the app requests a session

```meta
status: draft
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
status: draft
type: requirement
```

The system SHALL hand a dispatcher a request's kind, entry id or plan tag, repository and clone folder, and nothing an entry says.

#### Scenario: A dispatcher receives an entry request

- **Given** an entry request for an entry whose body holds instructions
- **When** a dispatcher waiting on the listener receives the request
- **Then** the request it receives names the entry id, the repository and the clone folder and holds no text of the entry

### Requirement: A waiting dispatcher gets the oldest request first

```meta
status: draft
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
status: draft
type: requirement
```

The system SHALL refuse a call to `GET /session-requests/wait` that does not carry the MCP listener's bearer token.

#### Scenario: A call without the token

- **Given** a request is Requested
- **When** a caller on this machine waits on the route without the bearer token
- **Then** the call is refused and the request stays Requested

### Requirement: Only one dispatcher holds a request

```meta
status: draft
type: requirement
```

The system SHALL let only one dispatcher claim a request at a time.

#### Scenario: Two dispatchers claim the same request

- **Given** a request is Requested and two dispatchers received it
- **When** both claim it
- **Then** the first claim holds it and the second is refused

### Requirement: A claim that is not answered in time lapses

```meta
status: draft
type: requirement
```

The system SHALL return a Claimed request to Requested when two minutes pass without its dispatcher reporting a session.

#### Scenario: A dispatcher stops mid-dispatch

- **Given** a dispatcher claimed a request
- **When** two minutes pass without it reporting a session
- **Then** the request is Requested again and the next waiting dispatcher receives it

### Requirement: A request waits until it is answered or cancelled

```meta
status: draft
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
status: draft
type: requirement
```

The system SHALL mark a Started request Linked when the session its dispatcher reported calls `link_session` on its entry.

#### Scenario: The started session picks the entry up

- **Given** an entry request a dispatcher reported as Started with a session id
- **When** that session calls `link_session` on the entry
- **Then** the request is Linked and the entry shows the session instead of the waiting request

### Requirement: The flag off offers no session requests

```meta
status: draft
type: requirement
```

The system SHALL offer no queue icon, no wait route and no dispatcher tool while the `session-requests` flag is off.

#### Scenario: The flag is off

- **Given** the `session-requests` flag is off
- **When** the person opens a prompt entry and a dispatcher calls the wait route
- **Then** the entry offers no queue icon, the route answers `404` and the MCP endpoint lists no dispatcher tool
