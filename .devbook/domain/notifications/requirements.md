# Requirements

```meta
status: proposed
type: requirements
related: [.devbook/domain/notifications/features.md]
```

> What Notifications guarantees, one chapter per capability. Each requirement is
> one SHALL sentence with the scenarios that prove it. A requirement names its tests
> once what it guarantees is built.

## Publishing a notification

```meta
status: proposed
type: requirements
related: [.devbook/domain/notifications/features.md#publishing-a-notification, .devbook/domain/notifications/domain.md#notification]
```

> What a source gets when it publishes an alert or a question.

### Requirement: A source publishes an alert or a question

```meta
status: proposed
type: requirement
```

The system SHALL let a source on a desktop instance publish an Alert or a Question with a severity of info, warning or critical, and make that instance its owner.

#### Scenario: An agent asks a question

- **Given** a Claude session runs on Desktop A
- **When** it asks "Run the migration against staging?" with the choices Yes and No
- **Then** Desktop A owns an Open question with a single-choice answer spec, and shows it locally

### Requirement: A question names its subject

```meta
status: proposed
type: requirement
```

The system SHALL refuse a Question that names no subject.

#### Scenario: A question without a subject

- **Given** a source drafts a Question with no subject
- **When** it publishes the draft
- **Then** the publish is refused, and no notification exists

### Requirement: A question offers one of four answer shapes

```meta
status: proposed
type: requirement
tests: [unit:dotnet:Backlog.Modules.Notifications.UnitTests.ReplyInvariantTests.A_question_offers_one_of_four_answer_shapes, unit:dotnet:Backlog.Modules.Notifications.UnitTests.ReplyInvariantTests.Any_other_answer_spec_is_refused, unit:dotnet:Backlog.Modules.Notifications.UnitTests.ReplyInvariantTests.The_owner_accepts_an_open_answer_instead_of_a_choice, unit:dotnet:Backlog.Modules.Notifications.UnitTests.ReplyInvariantTests.The_owner_accepts_a_choice_with_its_words, unit:dotnet:Backlog.Modules.Notifications.UnitTests.ReplyInvariantTests.The_owner_refuses_a_choice_that_takes_text_sent_without_it_and_stays_open, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.A_choice_that_takes_text_is_answered_from_the_pane_with_its_words, unit:dotnet:Backlog.Modules.Notifications.UnitTests.ReplyInvariantTests.A_question_offers_two_to_eight_set_choices, unit:dotnet:Backlog.Modules.Notifications.UnitTests.ReplyInvariantTests.The_owner_keeps_the_note_with_the_accepted_answer, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreTests.An_answer_carries_the_note_written_beside_it, unit:dotnet:Backlog.UI.Components.UnitTests.NotificationCardTests.Seven_or_eight_choices_lay_out_as_a_wrapping_grid_and_fewer_stay_one_row, unit:dotnet:Backlog.UI.Components.UnitTests.NotificationCardTests.A_question_that_takes_a_note_sends_it_with_the_choice_pressed, unit:dotnet:Backlog.Mobile.UI.UnitTests.NotificationScreenTests.Eight_choices_wrap_and_a_note_goes_with_the_choice_into_the_outbox]
```

The system SHALL accept a Question whose answer is single choice from two to eight set choices, multiple choice, open plain text, or choices plus an open answer, and nothing else; SHALL let a set choice of a single-choice question take text; and SHALL let any of these shapes take an optional plain-text note that a reply carries beside its answer.

#### Scenario: Choices with an open answer

- **Given** a source asks "Pick a name for the module, or suggest one" with three choices and an open answer
- **When** a viewer types "Signals" instead of picking a choice
- **Then** the owner accepts "Signals" as the answer

#### Scenario: A choice that takes text

- **Given** a single-choice question whose choice Revise takes text
- **When** a viewer picks Revise and types "Split the migration in two"
- **Then** the owner accepts Revise with "Split the migration in two" as the answer

#### Scenario: A choice that takes text, sent without it

- **Given** a single-choice question whose choice Revise takes text
- **When** a viewer picks Revise and types nothing
- **Then** the owner rejects the reply as not fitting the answer spec, and the viewer can reply again

#### Scenario: Eight choices and a note

- **Given** a source asks "Which rounding rule applies to invoice totals?" with eight choices and a note of at most 4,000 characters
- **When** a viewer picks "Round half even" and writes "Only for invoice totals" in "Add a note (optional)"
- **Then** the owner accepts "Round half even" as the answer, with "Only for invoice totals" as its note

### Requirement: A notification has no deadline and no default answer

```meta
status: proposed
type: requirement
```

The system SHALL keep a Question Open until it is answered or closed, however much time passes, and SHALL never answer it on the person's behalf.

#### Scenario: A question waits overnight

- **Given** an Open question nobody answered
- **When** a day passes
- **Then** it is still Open, with no answer

## Answering from another device

```meta
status: proposed
type: requirements
related: [.devbook/domain/notifications/features.md#answering-from-another-device, .devbook/domain/notifications/domain.md#reply, .devbook/arc42/06-runtime-view.md#notification-reply]
```

> What a person gets when they answer on the phone or on another desktop.

### Requirement: A viewer's reply becomes the answer only when the owner accepts it

```meta
status: proposed
type: requirement
```

The system SHALL treat a reply from a viewer instance as a proposal, and SHALL settle the question only when the owner instance accepts that reply.

#### Scenario: Answering from the phone

- **Given** Desktop A owns an Open question, and the phone is paired
- **When** the person taps Yes on the phone
- **Then** Desktop A accepts the reply, the question is Answered, and the phone shows "Answered from this phone"

#### Scenario: The owner is offline

- **Given** Desktop A owns an Open question and is asleep
- **When** the person replies on the phone
- **Then** the phone shows "delivers on wake", and the question becomes Answered when Desktop A wakes and accepts the reply

### Requirement: The first valid reply wins

```meta
status: proposed
type: requirement
```

The system SHALL accept the first valid reply the owner processes and reject every later reply as already answered.

#### Scenario: Two replies race

- **Given** an Open question
- **When** the phone replies Yes and Desktop B replies No, and the owner processes the phone's reply first
- **Then** the answer is Yes, Desktop B's reply is rejected as already answered, and both viewers show that the phone's Yes won

### Requirement: Any paired viewer may answer unless the question says owner only

```meta
status: proposed
type: requirement
```

The system SHALL accept a valid reply from any paired viewer instance, critical questions included, unless the question says `answerOn: owner`.

#### Scenario: An owner-only question

- **Given** an Open question that says `answerOn: owner`
- **When** the person replies on the phone
- **Then** the owner rejects the reply, and the question stays Open until it is answered on Desktop A

### Requirement: An invalid reply can be sent again

```meta
status: proposed
type: requirement
```

The system SHALL reject a reply that does not fit the answer spec with its reason, and let the viewer reply again.

#### Scenario: An open answer that is too long

- **Given** an open question with a length limit of 200 characters
- **When** a viewer sends 250 characters
- **Then** the reply is rejected as too long, and the viewer can send a shorter one

### Requirement: Android is the only phone platform

```meta
status: proposed
type: requirement
tests: [integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.NotificationPushEndpointTests.A_published_notification_is_pushed_to_its_owner_once, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.NotificationPushEndpointTests.The_hub_is_sent_a_data_only_fcm_v1_message_at_high_priority, unit:dotnet:Backlog.Mobile.UI.UnitTests.PushActionsTests, unit:dotnet:Backlog.Mobile.UI.UnitTests.PushTrayTests, unit:dotnet:Backlog.Mobile.UI.UnitTests.PushMessageTests, unit:dotnet:Backlog.Mobile.UI.UnitTests.NotificationOpenRequestTests]
```

The system SHALL deliver notifications to a phone only on Android.

#### Scenario: A phone receives a question

- **Given** a paired Android phone
- **When** Desktop A publishes a single-choice question with up to three choices
- **Then** the phone shows an OS notification with the title and summary, and one action per choice

#### Scenario: A question the tray cannot answer whole

- **Given** a paired Android phone
- **When** Desktop A publishes a multiple-choice question, or one that says `answerOn: owner`
- **Then** the phone shows an OS notification with no actions, and a tap opens the question in the app

## Asking from the contexts

```meta
status: proposed
type: requirements
related: [.devbook/domain/notifications/features.md#asking-from-the-contexts, .devbook/domain/notifications/context.md]
```

> What the contexts that publish promise the person.

### Requirement: A run at Personal Validation asks to be approved

```meta
status: proposed
type: requirement
related: [.devbook/domain/tasks/requirements.md#requirement-a-follow-up-is-filed-once-after-the-gated-entry]
tests: [unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.ValidationQuestionTests.The_first_gate_report_with_a_handoff_asks_to_approve_the_run_with_the_handoff_as_its_body, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.ValidationQuestionTests.A_gate_that_goes_in_progress_with_no_links_and_no_handoff_asks_nothing_until_a_report_carries_one, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.ValidationQuestionTests.A_handoff_longer_than_the_body_holds_is_cut_to_fit_and_ends_with_a_line_that_says_so, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.ValidationQuestionTests.A_run_that_names_its_entry_is_offered_approve_approve_with_a_follow_up_revise_and_reject, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.ValidationQuestionTests.A_run_that_names_no_entry_is_offered_approve_revise_and_reject_with_no_follow_up, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.ValidationQuestionTests.A_gate_reported_twice_with_its_handoff_asks_once, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.ValidationQuestionTests.After_a_revise_round_the_gate_asks_again_with_only_the_new_attempts_handoff, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.ValidationQuestionTests.Re_reporting_the_gate_after_it_was_approved_asks_nothing_more, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.ValidationQuestionTests.The_answer_is_recorded_on_the_run_as_its_gate_decision, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.ValidationQuestionTests.Approve_with_a_follow_up_files_one_entry_after_the_gated_entry_and_records_its_id, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.ValidationQuestionTests.A_follow_up_answer_delivered_twice_leaves_the_run_unchanged_and_files_no_second_entry, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.ValidationQuestionTests.A_follow_up_whose_gated_entry_is_gone_records_the_approval_with_the_words_and_no_follow_up, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.ValidationQuestionTests.A_replayed_answer_changes_nothing_and_the_next_gate_asks_again, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.ValidationQuestionTests.A_decision_the_desktop_recorded_after_the_question_wins_over_a_late_answer_and_files_nothing, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.ValidationQuestionTests.A_desktop_decision_taken_while_the_follow_up_is_filed_stands, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.ValidationQuestionTests.The_gate_moving_on_at_the_desktop_closes_the_unanswered_question, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.SurfaceToolsTests.Get_run_carries_the_gate_decision_the_answer_chosen_and_the_follow_up_entry]
```

The system SHALL ask "Approve the validation of *run*?" with the run's gate handoff as its body and the choices Approve, Approve + follow-up, Revise and Reject when a delivery run's Personal Validation stage first reports a handoff, and SHALL record the answer on the run as its gate decision.

#### Scenario: The handoff is the body

- **Given** a delivery run on Desktop A whose Personal Validation stage is in progress
- **When** the stage reports its review links, what to check, the acceptance criteria and the evidence paths
- **Then** Desktop A asks the question with all four as text in its body, and the title and summary carry none of it

#### Scenario: A gate that reports no handoff yet

- **Given** a delivery run whose Personal Validation stage went in progress with no links and no handoff text
- **When** nothing more is reported
- **Then** no question is asked, and the next report that carries links or handoff text asks it

#### Scenario: A handoff longer than the body holds

- **Given** a gate handoff longer than 32 KB
- **When** the question is asked
- **Then** the body holds the handoff cut to fit, ending with a line that says it was truncated

#### Scenario: Approved from the phone

- **Given** a delivery run on Desktop A reaches Personal Validation
- **When** the person taps Approve on the phone, and Desktop A accepts it
- **Then** the run records the approval, and the pull request is still opened at Desktop A

#### Scenario: Approved with a follow-up

- **Given** a delivery run that names the entry it works, at Personal Validation
- **When** the person answers Approve + follow-up with "Add the empty-state screenshot to the docs", and Desktop A accepts it
- **Then** the run records the approval with those words as its note and the id of the follow-up entry, and exactly one follow-up entry is filed after the gated entry

#### Scenario: A follow-up answer delivered twice

- **Given** an Approve + follow-up answer that already filed its follow-up entry
- **When** the answer is handed to the Sessions handler again
- **Then** the run is unchanged, and no second entry is filed

#### Scenario: Sent back to revise

- **Given** a delivery run at Personal Validation
- **When** the person answers Revise with "The toast overlaps the header"
- **Then** the run records a rejection with those words, and the flow starts a revise round

#### Scenario: Rejected

- **Given** a delivery run at Personal Validation
- **When** the person answers Reject
- **Then** the run records a rejection with none of the person's words, and `execute-plan` blocks the entry instead of starting a revise round

#### Scenario: A run that names no entry

- **Given** a delivery run whose file names neither an entry id nor a plan and item
- **When** its Personal Validation stage reports a handoff
- **Then** the question offers Approve, Revise and Reject, and no Approve + follow-up

#### Scenario: The desktop decided first

- **Given** an Open validation question
- **When** the person approves the gate at Desktop A, and an Approve + follow-up answer from the phone arrives afterwards
- **Then** the desktop's decision stands, and no follow-up entry is filed

### Requirement: A redeemed pairing code is confirmed on the desktop that showed it

```meta
status: proposed
type: requirement
tests: [unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.PairingSubjectsTests.A_device_this_desktops_code_paired_is_asked_about_here_only_as_allow_or_refuse, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.PairingSubjectsTests.Refuse_revokes_the_device_and_ends_the_pairing, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.PairingSubjectsTests.Allow_keeps_the_device_and_ends_the_pairing, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.PairingSubjectsTests.A_code_redeemed_in_its_last_seconds_is_still_asked_about, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.PairingSubjectsTests.The_probe_calls_a_pairing_ended_only_when_this_process_saw_it_end, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.PairingSubjectsTests.A_refusal_replayed_after_a_restart_revokes_from_the_source_key_and_a_device_already_gone_counts]
```

The system SHALL ask "Pair *device name*?" with Allow and Refuse, answerable only on the desktop whose pairing code the device redeemed, and SHALL revoke the device when the answer is Refuse.

#### Scenario: A device the person does not know

- **Given** Desktop A shows a pairing code
- **When** a device called "Unknown" redeems it, and the person answers Refuse on Desktop A
- **Then** the device is revoked, and the pairing ends

### Requirement: A remark left elsewhere raises an alert

```meta
status: proposed
type: requirement
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.RemarkNotificationStoreTests.A_remark_left_on_another_device_raises_an_info_alert_about_it_linking_to_its_chapter, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.RemarkNotificationStoreTests.A_remark_typed_here_a_draft_or_one_already_known_raises_no_alert, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.RemarkNotificationStoreTests.A_first_pull_of_old_remarks_raises_no_alert]
```

The system SHALL raise an info alert about a remark left on a Devbook chapter on another device, linking to the chapter, and SHALL raise none for a remark typed on the desktop itself.

#### Scenario: A remark from the laptop

- **Given** the person leaves a remark on "Storage and sync" on Desktop B
- **When** the remark syncs to Desktop A
- **Then** Desktop A raises an info alert about the remark that links to the chapter

## Closing a notification

```meta
status: proposed
type: requirements
related: [.devbook/domain/notifications/features.md#closing-a-notification, .devbook/domain/notifications/domain.md#notification-state, .devbook/domain/notifications/domain.md#subject]
```

> When a notification stops needing attention without being answered.

### Requirement: The publisher closes a notification

```meta
status: proposed
type: requirement
```

The system SHALL let the source that published a notification close it, and SHALL reject a reply that reaches the owner after that as closed.

#### Scenario: The agent no longer needs the answer

- **Given** an Open question from a Claude session
- **When** the session closes the question
- **Then** every viewer shows it as Closed, and a reply sent afterwards is rejected as closed

### Requirement: A notification closes when its subject ends

```meta
status: proposed
type: requirement
tests: [unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.TaskReplicaMergeSubjectTests.A_task_marked_done_on_the_phone_closes_its_notifications_on_the_owner, unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.TaskSubjectClosureTests.Marking_a_task_done_closes_the_notifications_about_it, unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.TaskSubjectClosureTests.A_task_that_comes_back_closes_nothing_and_ending_it_again_closes_again, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.SessionSubjectClosureTests.A_session_seen_open_and_then_archived_closes_its_notifications_once, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.SessionSubjectClosureTests.Finishing_a_run_closes_the_notifications_about_it_whatever_it_finished_on, unit:dotnet:Backlog.Desktop.UI.UnitTests.PullRequestSubjectsTests.A_reading_that_finds_it_merged_or_closed_closes_the_notifications_about_it_once, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.RemarkNotificationStoreTests.A_resolve_synced_in_from_another_device_closes_them_the_same_way, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.PairingSubjectsTests.A_code_that_expires_unredeemed_closes_the_notifications_about_its_pairing, unit:dotnet:Backlog.HostComposition.UnitTests.WebHarnessHostTests.The_desktop_harness_composes_a_subject_probe_for_every_context_that_owns_subjects, unit:dotnet:Backlog.HostComposition.UnitTests.WebHarnessHostTests.The_desktop_harness_composes_an_answer_handler_for_every_context_that_asks]
```

The system SHALL close every Open notification about a subject when the context that owns the subject reports that it ended, wherever the end happened.

#### Scenario: A task done on the phone

- **Given** an Open question about task T-123, owned by Desktop A
- **When** the person marks T-123 Done on the phone, and the change syncs to Desktop A
- **Then** Desktop A closes the question with the reason "Task done"
