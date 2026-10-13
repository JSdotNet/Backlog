# Invariants

```meta
status: draft
type: invariants
```

What each aggregate on [`domain.md`](domain.md) enforces, one chapter per aggregate: each rule, where it is enforced, and the unit test that proves it.

## Session Log

```meta
status: draft
type: invariants
related: [.devbook/domain/sessions/domain.md#session-log]
```

> The rules the Session Log holds every reading to. No domain type holds the log: the readers in the sessions infrastructure keep these rules, so `Enforced at:` names them.

### Invariant: A session appears once

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.One_session_filed_under_two_folders_is_one_row_not_two, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.Two_live_files_claiming_one_session_are_one_row, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.A_session_that_is_both_live_and_transcribed_is_listed_once_as_live, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.CompositeAgentSessionSourceTests.A_local_reading_wins_over_the_record_of_the_same_session]
```

A session appears once in the log, however many records the environment holds for it.
An agent may leave one record per process and another per transcript, or file one transcript under two working folders when the session moved between them; the session went somewhere, it did not become two.

Enforced at: `ClaudeTranscripts.Newest()`, `ClaudeSessionReader` (one live row per session id), `CompositeAgentSessionSource.GetSessionsAsync()`

### Invariant: The latest record describes the session

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.A_live_session_with_two_transcripts_is_still_one_row
```

Within one reading, when several records describe one session, the one the agent wrote most recently describes it, because it is the one the agent went on writing.

Enforced at: `ClaudeTranscripts.Newest()`, `ClaudeSessionReader` (the most recently active live file)

### Invariant: Duplicates collapse before counting

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.A_duplicate_transcript_does_not_take_up_a_place_under_the_cap
```

Records of one session collapse before anything is counted or capped, so the number a reading reports is of sessions, never of files.

Enforced at: `ClaudeTranscripts.Newest()`, `CompositeAgentSessionSource.GetSessionsAsync()`

### Invariant: State is derived, never carried

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.ReplicatedAgentSessionSourceTests.State_is_derived_from_the_timestamp_and_goes_stale_on_its_own, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.A_live_file_that_has_gone_quiet_is_stalled]
```

A session's `Session State` is worked out from the evidence on every reading, and is never stored or sent.

Enforced at: `AgentSessionStates.Of()`, `SessionRecordMapping` (no state on the wire), `RecordedAgentSessionSource`

### Invariant: Live states need evidence of life

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.A_transcript_becomes_a_finished_session_with_the_folder_it_states, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.A_copilot_session_updated_moments_ago_is_running]
```

A session reads Running or Stalled only on evidence that it is live, recent activity counting as that evidence, and reads Finished without it.

Enforced at: `ClaudeSessionReader`, `CopilotSessionReader`, `SessionRecordMapping` (silence past `StaleAfter` reads Finished, never Stalled)

### Invariant: There is no Failed state

```meta
status: draft
type: invariant
```

`Session State` has no Failed value, because a record that stops says nothing about why.

Enforced at: `AgentSessionState` (Running, Stalled, Finished)

### Invariant: The log never fills a gap

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.A_copilot_session_has_no_turn_count, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.A_live_session_with_no_transcript_yet_has_no_turn_count_or_branch, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.SessionToolsTests.A_recorded_repository_and_a_resolved_one_stay_apart]
```

A fact the agent did not record is absent from the log, never inferred.
A repository guessed from a folder path looks exactly like a recorded one, and is wrong.

Enforced at: each reader, with no single point: `CopilotSessionReader` and `ClaudeSessionReader` leave an unrecorded turn count or branch empty, and `SessionTools` keeps a resolved repository beside the recorded one

### Invariant: The log says how complete it is

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.SessionToolsTests.A_capped_list_carries_what_it_dropped_and_what_it_could_not_read, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.Only_the_most_recent_claude_sessions_are_read_and_the_rest_are_counted, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.A_list_that_fits_is_not_reported_as_capped]
```

A reading that describes fewer sessions than exist says how many exist.

Enforced at: `AgentSessionCatalog` (`Discovered`, `Capped`)

### Invariant: Completeness ignores the view

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.SessionToolsTests.A_filtered_list_still_reports_the_catalog_s_own_truncation, unit:dotnet:Backlog.Desktop.UI.UnitTests.SessionsPaneTests.A_capped_list_names_both_numbers]
```

The log's completeness counts only what the reading dropped, never the rows the reader's `Session View` removes.
One number for both would have the log confess to a shortfall the reader asked for.

Enforced at: `AgentSessionCatalog`, whose rows `AgentSessionViews.Of()` narrows only after it has answered
