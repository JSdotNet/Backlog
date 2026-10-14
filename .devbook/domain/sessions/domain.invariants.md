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

> The rules the Session Log holds every reading and every record to. No domain type holds the log: the readers in the sessions infrastructure, and for a replicated record the Sync module and the sync infrastructure, keep these rules, so `Enforced at:` names them.

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

### Invariant: A session is identified by agent and session id together

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.InMemorySessionReplicaTests.Two_agents_reporting_one_session_id_keep_two_records, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.AgentSessionRecordStoreTests.Two_agents_with_one_session_id_are_two_records, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.SessionSyncSessionTests.Activity_is_matched_on_the_agent_and_the_id_together]
```

A session is identified by its agent and its session id together, never by the session id alone.
Two agents may issue the same string, and a log keyed on the id alone would silently merge two unrelated sessions.

Enforced at: `SessionRecordKey.For()` (`{machine}:{agent}:{session}`), which `InMemorySessionReplica` and `CosmosSessionReplica` key on; `AgentSessionRecordStore`, which keys the local record the same way

### Invariant: Only the gathering environment writes a session record

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.InMemorySessionReplicaTests.An_appended_record_is_stamped_with_the_scopes_machine, unit:dotnet:Backlog.Modules.Sync.UnitTests.InMemorySessionReplicaTests.Two_machines_reporting_one_session_id_keep_two_records, unit:dotnet:Backlog.Modules.Sync.UnitTests.InMemorySessionReplicaTests.Re_appending_a_session_replaces_that_machines_record, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.SessionSyncSessionTests.The_pushed_body_names_no_machine_id, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.SessionSyncSessionTests.A_replicated_session_is_never_pushed]
```

A session record is written only by the environment that gathered it, and a record that arrived through sync is never pushed again.
A second push of the same session replaces that environment's own record and reaches no other; an environment that re-published another's records would attach somebody else's work to itself.

Enforced at: `SessionRecordKey.For()`, which leads the key with the machine id; `PushSessionsCommandHandler`, which takes the machine from the sender's token because the record carries none; `SessionSyncSession`, which pushes only sessions of local or recorded origin (`IsOwn()`)

### Invariant: Only the published fields travel

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.SessionSyncSessionTests.The_pushed_body_carries_exactly_the_whitelisted_fields, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.SessionSyncSessionTests.The_pushed_body_carries_no_working_folder, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.SessionSyncSessionTests.A_session_with_human_turns_travels_with_their_instants_only]
```

A replicated session record carries exactly the fields in [the published list](domain.md#session-log) and nothing else, so no prompt, tool output or file content ever leaves the machine.
That is the whole reason a record may leave the machine at all (local ADR 0005).

Enforced at: the `SessionRecord` wire contract, which only `SessionRecordMapping.ToRecord()` builds

### Invariant: A session record is added to and never taken from

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.SessionRecordsTests.A_reading_that_knows_less_takes_nothing_from_the_record, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.SessionRecordsTests.Activity_is_kept_before_the_reading_and_hits_are_a_union, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.SessionRecordsTests.Human_turns_are_a_union, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.SessionRecordsTests.A_reading_without_activity_keeps_the_records, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.AgentSessionRecordStoreTests.A_second_reading_amends_the_record, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.AgentSessionRecordStoreTests.An_unreadable_record_is_never_overwritten]
```

A reading amends an environment's session record without taking anything from it: a value the reading lacks keeps the recorded one, activity from before the reading's first instant is kept, and limit hits and human turns are a union.
The record has to answer once the transcript is gone, and a reading that knows less than the record would otherwise erase what only the record still holds.

Enforced at: `AgentSessionRecords.Amend()`; `AgentSessionRecordStore`, which never overwrites a record it cannot read
