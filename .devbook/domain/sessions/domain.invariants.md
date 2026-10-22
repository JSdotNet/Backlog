# Invariants

```meta
status: draft
type: invariants
```

What each aggregate and domain service on [`domain.md`](domain.md) enforces, one chapter each: each rule, where it is enforced, and the unit test that proves it.

## Session Log

```meta
status: draft
type: invariants
related: [.devbook/domain/sessions/domain.md#session-log]
```

> The rules the Session Log holds every reading and every record to, including those of the delivery runs and session rows it owns. No domain type holds the log: the readers in the sessions infrastructure, and for a replicated record the Sync module and the sync infrastructure, keep these rules, so `Enforced at:` names them.

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
tests: [unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.A_transcript_becomes_a_finished_session_with_the_folder_it_states, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.A_copilot_session_updated_moments_ago_is_running, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.An_open_session_keeps_the_state_its_liveness_evidence_gives_it]
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
tests: [unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.SessionToolsTests.A_capped_list_carries_what_it_dropped_and_what_it_could_not_read, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.Only_the_most_recent_claude_sessions_are_read_and_the_rest_are_counted, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.A_list_that_fits_is_not_reported_as_capped, unit:dotnet:Backlog.Modules.Sessions.UnitTests.SessionsPaneTests.A_capped_subtitle_names_the_older_open_sessions_apart]
```

A reading that describes fewer sessions than exist says how many exist.
It also says how many of the sessions it lists are open ones older than the newest session the limit dropped, so "the most recent" stays true of the rest.

Enforced at: `AgentSessionCatalog` (`Discovered`, `Capped`, `OpenBeyondCap`)

### Invariant: Completeness ignores the view

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.SessionToolsTests.A_filtered_list_still_reports_the_catalog_s_own_truncation, unit:dotnet:Backlog.Modules.Sessions.UnitTests.SessionsPaneTests.A_capped_list_names_both_numbers]
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

### Invariant: The desktop record's facts never travel

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.SessionSyncSessionTests.The_desktop_record_facts_never_change_the_pushed_record, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.SessionSyncSessionTests.The_sidebar_title_never_reaches_the_mapped_record, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.AgentSessionRecordStoreTests.A_record_keeps_no_desktop_record_facts, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.CompositeAgentSessionSourceTests.Only_a_local_answer_carries_the_desktop_record]
```

A session's archived flag, scheduled task and sidebar title never leave the machine, are never kept on its session record, and are never carried by a Recorded or Replicated answer.
They describe this machine's Claude desktop app, which no other machine and no later reading can ask.

Enforced at: `SessionRecordMapping.ToRecord()` (no field for any of them; the record's title is the session's own), `AgentSessionRecordStore`, `CompositeAgentSessionSource.LocalFactsOnly()`

### Invariant: A session row is called what the desktop app calls it

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sessions.UnitTests.SessionsPaneTests.A_session_the_desktop_app_names_is_called_by_its_sidebar_name, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.AgentSessionAssistantSessionSourceTests.A_session_the_desktop_app_names_arrives_under_its_sidebar_name, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.ClaudeDesktopSessionReaderTests.The_sidebar_title_is_read_from_the_records_top_level, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.A_session_with_no_desktop_record_is_neither_archived_nor_open]
```

A session row's title is the session's sidebar title where its desktop record names one, and the session's own display title otherwise.
The session's display title is not changed by it.
The Dashboard's longest sessions name a session the same way; every other surface, the MCP server's included, names it by its display title.

Enforced at: `SessionRow.Title`, `AgentSessionAssistantSessionSource` (the longest sessions), `ClaudeDesktopSessionReader` (a blank or absent `title` reads null), `ClaudeSessionReader.Stamped()`

### Invariant: A session record is added to and never taken from

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.SessionRecordsTests.A_reading_that_knows_less_takes_nothing_from_the_record, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.SessionRecordsTests.Activity_is_kept_before_the_reading_and_hits_are_a_union, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.SessionRecordsTests.Human_turns_are_a_union, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.SessionRecordsTests.A_reading_without_activity_keeps_the_records, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.AgentSessionRecordStoreTests.A_second_reading_amends_the_record, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.AgentSessionRecordStoreTests.An_unreadable_record_is_never_overwritten]
```

A reading amends an environment's session record without taking anything from it: a value the reading lacks keeps the recorded one, activity from before the reading's first instant is kept, and limit hits and human turns are a union.
The record has to answer once the transcript is gone, and a reading that knows less than the record would otherwise erase what only the record still holds.

Enforced at: `AgentSessionRecords.Amend()`; `AgentSessionRecordStore`, which never overwrites a record it cannot read

### Invariant: A resolved repository sits beside the recorded one

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.A_session_under_a_registered_clone_carries_the_resolved_repository_beside_the_recorded_one, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.A_session_outside_every_registered_clone_is_placed_nowhere, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.SessionToolsTests.A_recorded_repository_and_a_resolved_one_stay_apart]
```

The repository this product places a session in is held beside the repository the agent recorded, never written in its place, and is absent where no registered clone contains the session's folder.
"The agent said" and "this product placed" fail differently, and a surface has to be able to tell them apart.

Enforced at: `AgentSession.ResolvedRepository`, a separate property the source stamps after the reader has built the session

### Invariant: A session always has a last activity

```meta
status: draft
type: invariant
```

Every session carries the instant of its last activity, while its start may be unknown.
Some evidence is only a file with a timestamp on it, and that timestamp is what `Liveness Assessment` reads.

Enforced at: `AgentSession` (`LastActivityAt` is required, `StartedAt` is optional)

### Invariant: A reading since a horizon is never capped by the session limit

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.A_reading_since_a_horizon_is_every_claude_session_inside_it_and_not_the_newest_hundred, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.A_reading_since_a_horizon_is_every_copilot_session_inside_it_and_not_the_newest_hundred]
```

A reading since a horizon describes every session active at or after it, however many there are.
A count taken over the newest sessions per agent would read the limit back as a total.

Enforced at: `AgentSessionQuery.Since()`, which the readers answer uncapped

### Invariant: An open session is never dropped by the session limit

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.An_open_session_beyond_the_cap_is_still_listed_and_an_archived_one_is_not, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.A_timestamps_reading_lists_the_same_sessions_open_ones_included, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.The_catalog_counts_the_open_sessions_listed_only_because_they_are_open, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.CompositeAgentSessionSourceTests.The_open_sessions_past_the_cap_are_still_counted_after_the_merge]
```

A list reading describes every Claude session the desktop app holds open, however far down the newest sessions it sits.
Open sessions are read beside the limit rather than inside it, so the newest sessions the limit keeps are the same with or without them.

Enforced at: `ClaudeSessionReader.ReadAsync()`, which sets the open transcripts apart before it works out the room the limit leaves

### Invariant: Unknown is never open

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.ClaudeDesktopSessionReaderTests.A_record_without_the_flag_reads_null_not_false, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.AgentSessionSourceTests.A_session_with_no_desktop_record_is_neither_archived_nor_open, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.CompositeAgentSessionSourceTests.Only_a_local_answer_carries_the_desktop_record, unit:dotnet:Backlog.Modules.Sessions.UnitTests.AgentSessionViewTests.The_open_view_never_keeps_an_archived_unknown_or_run_only_row]
```

A session's archived flag is read from its desktop record and never derived from its state, and a session with no desktop record, or a record that states no flag, has none.
A session with no flag is never open.

Enforced at: `ClaudeDesktopSessionReader` (an absent `isArchived` reads null), `ClaudeSessionReader.Stamped()`, `CompositeAgentSessionSource.LocalFactsOnly()`

### Invariant: A human turn is kept as its instant alone

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.SessionSyncSessionTests.A_session_with_human_turns_travels_with_their_instants_only
```

A session keeps only the instant of each human turn, never what the person typed.

Enforced at: `AgentSessionActivity.HumanTurns`, a list of instants that the session record and the wire carry as they are; `ActivityEvent`, which carries whether a line was a human turn and nothing of its text

### Invariant: A run's status is the word its file wrote

```meta
status: draft
type: invariant
```

A delivery run's status is the status word its file states, kept verbatim and never mapped onto a smaller set.
The dashboards wrote seven spellings across two generations, and a mapping would decide what a writer meant rather than report what it wrote.

Enforced at: `DeliveryRun.Status`, a string the `DeliveryRunReader` fills from the file

### Invariant: A run that names no trigger was attended

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliverySurfaceLifecycleTests.A_run_that_says_nothing_about_its_start_reads_as_attended_and_writes_no_field
```

A delivery run counts as fired by a schedule only when its trigger says `scheduled`; a run that names no trigger reads as attended.

Enforced at: `DeliveryRun.Scheduled`

### Invariant: Two surfaces' reports of one run are one run

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunMergingTests.Two_surfaces_reports_of_one_run_are_one_run, unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunMergingTests.Two_files_from_one_surface_are_two_runs, unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunMergingTests.A_different_skill_is_a_different_run, unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunMergingTests.A_different_worktree_is_a_different_run_and_letter_case_is_not_a_difference, unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunMergingTests.Different_folders_pair_only_on_a_shared_session, unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunMergingTests.Starts_further_apart_than_the_window_are_two_runs, unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunMergingTests.An_undated_start_never_pairs, unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunMergingTests.Session_ids_must_agree_and_none_on_either_side_is_agreement, unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunMergingTests.A_partner_that_never_arrived_does_not_take_the_next_runs_file, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliveryRunReaderTests.One_run_reported_to_two_surfaces_is_read_as_one]
```

Two run files from different dashboards are one delivery run when they name the same skill, both started within five minutes of each other, and they agree on the session: under one worktree key with session ids that do not contradict, or in two folders that both name one session id.
A pair that names different sessions stays two runs.

Enforced at: `DeliveryRunMerging.Merge()`, which the reader applies before anything else sees the catalog

### Invariant: The preferred surface's file is a merged run's record

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunMergingTests.The_dashboard_is_the_record_and_the_backlog_fills_only_its_gaps, unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunMergingTests.The_orch_dashboard_outranks_the_backlog_and_a_dashboard_nobody_listed_ranks_last]
```

A merged run's id, status word, stages and measured figures are those of the first of `delivery-surface-dashboard`, `orch-dashboard` and `backlog` that reported it; the other files only fill what it left blank and add the references it did not name.
One measurement is shown rather than the sum of two that overlap.

Enforced at: `DeliveryRunMerging.Merge()`

### Invariant: A merged run stays in the worktree the flow named

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DeliveryRunMergingTests.A_dashboard_copy_filed_under_the_main_checkout_folds_into_the_worktrees_run, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliverySurfaceLifecycleTests.A_run_the_dashboard_filed_under_the_main_checkout_is_still_this_worktrees_and_answers_to_its_own_id]
```

A merged run keeps the folder of its `backlog` file, whichever file is its record.
The dashboard server files under the main checkout, and only the `backlog` surface files under the worktree the flow named.

Enforced at: `DeliveryRunMerging.Merge()`

### Invariant: A run-only row speaks with its run's facts

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.SessionRowsTests.A_run_only_row_speaks_with_the_runs_own_facts, unit:dotnet:Backlog.Desktop.UI.UnitTests.SessionRowsTests.Scheduled_runs_with_no_session_are_one_row_per_schedule_and_repository]
```

A row with no session is titled by the schedule that fired its runs, or else by its run's worktree, and is dated by the run and placed on the environment the run file was read on.

Enforced at: `SessionRow` (`Title`, `StartedAt`, `LastActivityAt`, `EnvironmentId`)

### Invariant: A run-only row is Finished

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.SessionRowsTests.A_run_only_row_speaks_with_the_runs_own_facts, unit:dotnet:Backlog.Desktop.UI.UnitTests.AgentSessionViewTests.The_live_view_never_keeps_a_finished_session]
```

A row with no session reads Finished, so the live view never keeps one.
There is no liveness evidence for it: a session still running would be in the reading, and the run would have joined it.

Enforced at: `SessionRow.State`

## Delivery Run Recording

```meta
status: draft
type: invariants
related: [.devbook/domain/sessions/domain.md#delivery-run-recording]
```

> The rules `Delivery Run Recording` will not let a calling session break. The domain chapter says what the capability is and why it exists; this says what it refuses.

### Invariant: Starting a run already under way reattaches to it

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliverySurfaceLifecycleTests.Starting_again_for_the_same_skill_reattaches_rather_than_forking_the_run, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliverySurfaceLifecycleTests.A_finished_run_is_not_reattached_to, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliverySurfaceLifecycleTests.A_run_for_another_skill_is_not_reattached_to, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliverySurfaceLifecycleTests.A_run_in_another_worktree_is_not_reattached_to, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.SurfaceToolsTests.Start_run_carries_the_stage_list_over_and_reports_a_reattach]
```

Starting a run for a skill that has a run in progress in the same worktree continues that run rather than opening a second; a finished run is never reattached to.

Enforced at: `LocalDeliverySurfaceLifecycle.StartRunAsync()`

### Invariant: A session that picks a run back up is added beside the others

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliverySurfaceLifecycleTests.A_run_records_every_session_that_drove_it
```

A session that reattaches to a run is appended to the sessions the run names, never put in place of the one that started it, and a session the run already names is not added twice.

Enforced at: `LocalDeliverySurfaceLifecycle.StartRunAsync()`

### Invariant: A stage outside the run's list is refused

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliverySurfaceLifecycleTests.A_stage_index_outside_the_list_is_refused_rather_than_ignored
```

An update to a stage index outside the list fixed when the run began is refused, never ignored.
A caller one stage off would otherwise go on reporting the wrong stage for the rest of the run.

Enforced at: `LocalDeliverySurfaceLifecycle.UpdateStageAsync()`

### Invariant: An unknown trigger is refused

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliverySurfaceLifecycleTests.A_trigger_the_contract_does_not_name_is_refused
```

Starting a run with a trigger the delivery engine does not name is refused.

Enforced at: `LocalDeliverySurfaceLifecycle.StartRunAsync()`, against `DeliveryRunTriggers.All`

### Invariant: What started a run is never rewritten

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliverySurfaceLifecycleTests.A_reattach_fills_in_what_started_the_run_and_never_rewrites_it
```

A run's trigger, schedule and repository are written once: a reattach fills in only those an earlier start left out.
A scheduled run a person picks back up was still fired by its schedule.

Enforced at: `LocalDeliverySurfaceLifecycle.StartRunAsync()`

### Invariant: A recorded run is written in one status vocabulary

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliverySurfaceLifecycleTests.A_status_the_vocabulary_does_not_have_is_refused
```

A run this context records is written `in_progress` while under way and finishes only on `done`, `blocked`, `parked` or `cancelled`; any other status word is refused.
Reviving an older generation's spelling from here would make the drift permanent rather than historical.

Enforced at: `LocalDeliverySurfaceLifecycle.StartRunAsync()` and `FinishRunAsync()`, against `DeliveryRunStatuses`

## Stage Resolution

```meta
status: draft
type: invariants
related: [.devbook/domain/sessions/domain.md#stage-resolution]
```

> The rules `Stage Resolution` holds every stage it reads to.

### Invariant: A pending stage and the gate are never compared

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliveryRunStageResolutionTests.A_pending_stage_and_the_gate_are_never_compared
```

A stage is compared against its configuration only once it is past pending, and the Personal Validation gate never is.

Enforced at: `DeliveryRunStageResolution.Of()`

### Invariant: An inferred value says it was inferred

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliveryRunStageResolutionTests.Without_resolved_phases_mode_and_agent_are_inferred_from_what_ran, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliveryRunStageResolutionTests.A_stage_the_phase_map_has_no_mode_for_is_inferred_and_says_why]
```

A mode or agent worked out from what was seen, rather than read from what the run recorded, is marked inferred.
A guess presented as a record would be indistinguishable from one.

Enforced at: `DeliveryRunStageExecution.Inferred`

## Claude API Request Log

```meta
status: draft
type: invariants
related: [.devbook/domain/sessions/domain.md#claude-api-request-log]
```

> The rules the log of Claude Code's reported requests holds every request to.

### Invariant: A request is stored once

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteClaudeApiRequestStoreTests.The_same_request_twice_is_stored_once_and_the_first_copy_wins
```

A request is identified by its `request_id`, and the first copy to arrive is the one kept.
An exporter retries a batch it did not see acknowledged, so the same event arriving again is ordinary.

Enforced at: `SqliteClaudeApiRequestStore.AddAsync()`

### Invariant: A request without a request id is not kept

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Desktop.UI.UnitTests.ClaudeCodeOtlpLogsTests.A_request_without_a_request_id_is_dropped_because_it_could_not_be_stored_once
```

An event that carries no `request_id` is dropped on arrival, because it could not be told from its own retry.

Enforced at: `ClaudeCodeOtlpLogs` (the desktop app's `/v1/logs` receiver)

### Invariant: Only the api_request event is kept

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.ClaudeCodeOtlpLogsTests.The_captured_json_export_yields_its_api_requests_and_nothing_else, unit:dotnet:Backlog.Desktop.UI.UnitTests.ClaudeCodeOtlpLogsTests.Another_event_with_a_request_id_is_still_dropped]
```

Of the events Claude Code exports, only `claude_code.api_request` is kept; every other event is dropped on arrival.

Enforced at: `ClaudeCodeOtlpLogs`

### Invariant: Who made a request is not stored

```meta
status: draft
type: invariant
```

The account, the organization and the e-mail address Claude Code attaches to every event are never stored with a request.

Enforced at: `ClaudeApiRequest`, which has no field for them

### Invariant: An unknown figure stays unknown

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteClaudeApiRequestStoreTests.An_unknown_figure_is_kept_unknown_rather_than_zero
```

A figure the event did not carry is stored as absent, never as zero.

Enforced at: `ClaudeApiRequest`, whose figures are all optional; `SqliteClaudeApiRequestStore`

## Cost Attribution

```meta
status: draft
type: invariants
related: [.devbook/domain/sessions/domain.md#cost-attribution]
```

> The rules `Cost Attribution` holds every figure it gives to.

### Invariant: A stage claims only its own requests

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliveryRunCostsTests.A_stage_takes_the_owner_sessions_main_loop_requests_inside_its_window, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliveryRunCostsTests.A_stage_takes_the_sub_agent_requests_whose_agent_name_or_query_source_matches_its_workers, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliveryRunCostsTests.A_sub_agent_request_after_the_stage_ends_is_still_its_workers_while_the_call_runs, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliveryRunCostsTests.A_stage_still_under_way_has_no_end_to_its_window]
```

A stage's reported cost counts the owner session's main-loop requests inside the stage's window and the requests of the stage's own workers, inside that window or inside the worker's call, and no other request.

Enforced at: `DeliveryRunCosts.StageRequests()` and `WorkerRequests()`

### Invariant: A cost that cannot be told apart is estimated

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliveryRunCostsTests.A_stage_re_entered_after_requested_changes_is_estimated_rather_than_reported_in_part, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliveryRunCostsTests.Two_overlapping_calls_of_one_agent_claim_no_requests_and_fall_back_to_their_estimates, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliveryRunCostsTests.A_call_with_no_end_claims_its_agents_requests_in_the_stage_only_when_it_is_the_only_call, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliveryRunCostsTests.A_stage_whose_window_holds_unnamed_sub_agent_requests_is_estimated_rather_than_reported_in_part, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliveryRunCostsTests.A_named_sub_agent_request_keeps_the_stage_reported]
```

A stage re-entered after requested changes, a stage whose window holds a sub-agent's request that names no agent, a call that recorded no end unless it is its agent's only call in the stage, and two overlapping calls of one agent are given an estimated cost, never a reported one.
A reported figure over a window that holds only part of the work would read as the whole.

Enforced at: `DeliveryRunCosts.Of()` and `HasUnnamedSubAgentRequests()`

### Invariant: An estimate with an unpriced part is no figure

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliveryRunCostsTests.With_neither_telemetry_nor_a_price_there_is_no_cost_rather_than_zero, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliveryRunCostsTests.A_figure_reads_in_dollars_and_never_as_zero]
```

An estimate is absent, never zero, when any kind of token it spent has no price.
A partial sum would read as the whole.

Enforced at: `ModelPriceTable.Estimate()`; `DeliveryRunCosts.Session()` and its stage estimates, which give no figure once a part that spent tokens has none

## Cost Baseline

```meta
status: draft
type: invariants
related: [.devbook/domain/sessions/domain.md#cost-baseline]
```

> The rules `Cost Baseline` holds every comparison to.

### Invariant: A run is compared with the latest thirty finished runs of its skill

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliveryRunBaselinesTests.Only_finished_earlier_runs_of_the_same_flow_that_started_before_count_and_only_the_latest_thirty, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliveryRunBaselinesTests.A_run_of_the_same_id_in_another_worktree_is_still_an_earlier_run, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DeliveryRunBaselinesTests.One_runaway_run_does_not_move_the_median]
```

A run's usual is the median over the latest 30 runs of the same skill that finished and started before it, never including the run itself.
A run with no start is placed by its last update, and a finished status is `done`, or an older dashboard's `completed` or `success`.

Enforced at: `DeliveryRunBaselines.Earlier()`, `DeliveryRunBaselines.Median()`

## Weekly Usage

```meta
status: draft
type: invariants
related: [.devbook/domain/sessions/domain.md#weekly-usage]
```

> The rules `Weekly Usage` holds every share to.

### Invariant: Nothing reported inside the week is no share

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.WeeklyUsageTests.Nothing_reported_inside_the_week_is_no_share
```

A session or run with no reported cost inside the week has no share, never a share of zero.

Enforced at: `WeeklyUsage.ShareOf()`

## Run Attachment

```meta
status: draft
type: invariants
related: [.devbook/domain/sessions/domain.md#run-attachment]
```

> The rules `Run Attachment` holds every row it builds to.

### Invariant: A run that names its sessions joins by identity

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.SessionRowsTests.A_run_naming_its_session_joins_that_session, unit:dotnet:Backlog.Desktop.UI.UnitTests.SessionRowsTests.A_scheduled_run_whose_session_the_list_holds_joins_that_session]
```

A run that names the sessions that drove it joins the first of them the list holds, and is never attached by its worktree, even when the list holds none of them.
The run said whose it is, and a guess would put it on someone else's row.

Enforced at: `SessionRows.Of()`

### Invariant: A run that names no session joins by place and time

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.SessionRowsTests.A_run_joins_the_session_in_its_worktree_whose_window_it_overlaps, unit:dotnet:Backlog.Desktop.UI.UnitTests.SessionRowsTests.A_run_in_another_worktree_is_not_the_sessions_whatever_the_window_says, unit:dotnet:Backlog.Desktop.UI.UnitTests.SessionRowsTests.A_session_in_the_worktree_that_ended_before_the_run_began_does_not_claim_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.SessionRowsTests.A_folder_in_another_letter_case_is_the_same_worktree, unit:dotnet:Backlog.Desktop.UI.UnitTests.SessionRowsTests.A_session_started_in_a_subfolder_still_claims_its_worktrees_runs, unit:dotnet:Backlog.Desktop.UI.UnitTests.SessionRowsTests.A_session_without_a_folder_is_matched_on_the_key_its_record_carried, unit:dotnet:Backlog.Desktop.UI.UnitTests.SessionRowsTests.Of_two_overlapping_sessions_the_one_that_started_with_the_run_wins]
```

A run that names no session joins a session only when the run's worktree key equals a key derived from the session's folder or a folder above it, compared without regard to case, and the session's activity window overlaps the run's.
The worktree alone names a place; only the overlap names a time.

Enforced at: `SessionRows.Of()`

### Invariant: A run that matches nothing is never dropped

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.SessionRowsTests.Sessions_keep_their_order_and_stray_runs_follow_most_recent_first, unit:dotnet:Backlog.Desktop.UI.UnitTests.SessionRowsTests.Scheduled_runs_with_no_session_are_one_row_per_schedule_and_repository]
```

A run that joins no session becomes a row of its own, or joins its schedule's row, and is never left out of the list.
Its session may be older than the reading reached or may have run elsewhere, and neither is a reason to hide the work.

Enforced at: `SessionRows.Of()`

## Session View

```meta
status: draft
type: invariants
related: [.devbook/domain/sessions/domain.md#session-view]
```

> The rules `Session View` and the environment narrowing beside it keep.

### Invariant: Rows are narrowed before they are grouped

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sessions.UnitTests.AgentSessionViewTests.Filtering_then_grouping_is_grouping_over_what_survived, unit:dotnet:Backlog.Modules.Sessions.UnitTests.AgentSessionEnvironmentTests.Narrowing_then_viewing_then_grouping_is_grouping_over_what_survived, unit:dotnet:Backlog.Modules.Sessions.UnitTests.SessionsPaneTests.Grouping_moves_a_narrowed_lists_rows_and_removes_none]
```

Every narrowing of the session list is applied before the list is grouped, never after, so an environment with nothing left in it loses its section rather than keeping an empty one.

Enforced at: the Sessions pane's derived view (`SessionsPane.View`), which hands `AgentSessionGroups.Of()` only what the narrowings kept

### Invariant: Open admits on the desktop record, never on state

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sessions.UnitTests.AgentSessionViewTests.The_open_view_keeps_exactly_the_unarchived_rows_whatever_their_state, unit:dotnet:Backlog.Modules.Sessions.UnitTests.AgentSessionViewTests.An_open_finished_session_is_not_live, unit:dotnet:Backlog.Modules.Sessions.UnitTests.AgentSessionViewTests.The_views_are_live_open_and_all_in_that_order]
```

The Open view keeps exactly the rows whose session's archived flag is false, whatever their `Session State`, and an open Finished row is never live.

Enforced at: `AgentSessionViews.Of()` and `AgentSessionViews.IsOpen()`; `AgentSessionViews.IsLive()` reads state alone

### Invariant: An Idle row still reads Finished

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Sessions.UnitTests.SessionsPaneTests.An_open_finished_row_reads_idle_in_open_and_finished_everywhere_else
```

The Idle badge is drawn only in the Open view, only on an open Finished row, and its title and screen-reader text still say Finished.
It is a note on the desktop record, not a fourth state.

Enforced at: `SessionsPane.IsIdle()` and `SessionsPane.IdleTitle`

## Session Grouping

```meta
status: draft
type: invariants
related: [.devbook/domain/sessions/domain.md#session-grouping]
```

> The rules `Session Grouping` holds every grouping to.

### Invariant: Grouping never removes a row

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sessions.UnitTests.AgentSessionGroupTests.Every_grouping_carries_every_session, unit:dotnet:Backlog.Modules.Sessions.UnitTests.SessionListTests.The_new_groupings_carry_every_row, unit:dotnet:Backlog.Modules.Sessions.UnitTests.SessionsPaneTests.Grouping_moves_rows_and_removes_none, unit:dotnet:Backlog.Modules.Sessions.UnitTests.AgentSessionGroupTests.Scheduled_rows_close_the_list_in_one_section_of_their_own]
```

Every row handed to a grouping comes out in exactly one of its sections, so a count taken before grouping is still correct after it.

Enforced at: `AgentSessionGroups.Of()`, which closes the Repository grouping with a "No repository" section rather than leave those rows out; `AgentSessionGroups.ScheduledApart()`, which moves the scheduled rows into one section and drops none

### Invariant: An environment is told apart by its id

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sessions.UnitTests.AgentSessionGroupTests.Two_environments_sharing_a_name_are_two_sections, unit:dotnet:Backlog.Modules.Sessions.UnitTests.AgentSessionEnvironmentTests.A_renamed_machine_is_one_environment_under_its_newest_name, unit:dotnet:Backlog.Modules.Sessions.UnitTests.AgentSessionEnvironmentTests.Narrowing_is_on_the_id_and_not_the_name]
```

Grouping and narrowing by environment key on the `EnvironmentId`, never on the display name.
A machine can be renamed and two machines can share a name.

Enforced at: `AgentSessionGroups.Of()` (the Environment grouping), `AgentSessionEnvironments.On()`

### Invariant: A row's day is read against the moment the list was read

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Sessions.UnitTests.SessionListTests.By_when_days_are_counted_on_the_readers_clock
```

Only the When grouping reads a clock and a time zone, and both are handed in: the reader's zone, and the moment the list was read rather than the moment it is drawn.
So a press in the filter bar never moves a row from one day into another.

Enforced at: `AgentSessionGroups.Of()` (its `now` and `zone` parameters); the Sessions pane, which passes the instant of its reading

## Session Request Queue

```meta
status: draft
type: invariants
related: [.devbook/domain/sessions/domain.md#session-request-queue]
```

> The rules the queue holds every session request to (local ADR 0037). The record and its queue are built: `SessionRequest` and the pure transitions in `SessionRequests`, in the Sessions abstractions, and `SqliteSessionRequestStore`, which applies each transition inside one immediate transaction. The Start session and Start plan session actions are built too, in the Tasks pane and the roadmap item's dialog, through `SessionRequestActions`. The wait route and the dispatcher tools are later items of the `session-dispatch` plan.

### Invariant: A request carries no entry text

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Sessions.UnitTests.SessionRequestsTests.A_new_entry_request_is_Requested_and_carries_only_its_target_repository_and_folder
```

A session request holds its kind, an entry id or a plan tag, a repository, a clone folder, its state, lease and claiming dispatcher, the session id it was answered with and when it was asked, and no field for anything an entry says.
Entry text arrives through sync, imports and capture, so a brief built from it would run whatever anyone who can write an entry wrote.

Enforced at: `SessionRequest`, which has no field for entry text (ADR 0037 §1)

### Invariant: Only the app creates a request

```meta
status: draft
type: invariant
```

A request is created only by the desktop app's own Start session action; no MCP tool, import, sync or capture creates one.

Enforced at: `ISessionRequestStore.RequestAsync()`, which no MCP tool calls (ADR 0037 §1)

### Invariant: One open request per entry or plan

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sessions.UnitTests.SessionRequestsTests.A_Claimed_or_Started_request_also_blocks_a_duplicate, unit:dotnet:Backlog.Modules.Sessions.UnitTests.SessionRequestsTests.A_Linked_or_Cancelled_request_does_not_block_a_new_one, unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteSessionRequestStoreTests.A_second_ask_for_an_entry_with_an_open_request_returns_the_open_one_and_stores_nothing]
```

A second ask for an entry or a plan that already has a request in Requested, Claimed or Started answers that request and queues nothing; once it is Linked or Cancelled, a new ask queues a new one.
A second click while the first waits must not start the same work twice.

Enforced at: `SessionRequests.OpenDuplicateOf()`, applied by `SqliteSessionRequestStore.RequestAsync()` inside one immediate transaction

### Invariant: Only a Requested request can be claimed

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sessions.UnitTests.SessionRequestsTests.A_second_claim_on_a_held_request_is_refused, unit:dotnet:Backlog.Modules.Sessions.UnitTests.SessionRequestsTests.A_Started_Linked_or_Cancelled_request_cannot_be_claimed, unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteSessionRequestStoreTests.Two_dispatchers_claiming_at_once_get_one_claim_between_them]
```

A claim succeeds only on a request in Requested, and moves it to Claimed in the same write; a claim on any other state is refused.
So two dispatchers waiting at once never both start one request.

Enforced at: `SessionRequests.Claim()`, applied by `SqliteSessionRequestStore.ClaimAsync()` inside one immediate transaction (ADR 0037 §3)

### Invariant: A lapsed claim returns the request to Requested

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sessions.UnitTests.SessionRequestsTests.A_claim_reads_as_Claimed_until_its_lease_ends_and_as_Requested_from_then, unit:dotnet:Backlog.Modules.Sessions.UnitTests.SessionRequestsTests.A_lapsed_claim_can_be_claimed_by_the_next_dispatcher, unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteSessionRequestStoreTests.A_lapsed_claim_reads_as_Requested_and_is_handed_out_again]
```

A claim holds the request for two minutes; a request still Claimed when its lease ends reads as Requested again, and its claim is no longer the dispatcher's.
A dispatcher that stopped mid-dispatch does not keep the request from the next one.

Enforced at: `SessionRequest.StateAt()`, which every read of `SqliteSessionRequestStore` and every transition in `SessionRequests` asks (ADR 0037 §3)

### Invariant: A request never expires

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sessions.UnitTests.SessionRequestsTests.A_Requested_request_never_expires, unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteSessionRequestStoreTests.A_Requested_request_is_still_waiting_a_year_on]
```

A Requested request stays Requested however long no dispatcher runs; only the person's cancel ends it unanswered.

Enforced at: `SessionRequests` and `SqliteSessionRequestStore`, which have no expiry (ADR 0037 §3)

### Invariant: Linked and Cancelled are final

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sessions.UnitTests.SessionRequestsTests.A_Started_Linked_or_Cancelled_request_cannot_be_cancelled, unit:dotnet:Backlog.Modules.Sessions.UnitTests.SessionRequestsTests.Only_a_Started_request_can_be_Linked, unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteSessionRequestStoreTests.Cancel_is_written_and_a_Cancelled_request_is_never_handed_out]
```

A Linked or Cancelled request changes no further: it cannot be claimed, reported, linked again or cancelled.

Enforced at: `SessionRequests.Claim()`, `Report()`, `Link()` and `Cancel()` (ADR 0037 §3)
