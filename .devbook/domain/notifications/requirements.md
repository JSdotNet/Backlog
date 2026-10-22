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
```

The system SHALL accept a Question whose answer is single choice, multiple choice, open plain text, or choices plus an open answer, and nothing else.

#### Scenario: Choices with an open answer

- **Given** a source asks "Pick a name for the module, or suggest one" with three choices and an open answer
- **When** a viewer types "Signals" instead of picking a choice
- **Then** the owner accepts "Signals" as the answer

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
```

The system SHALL deliver notifications to a phone only on Android.

#### Scenario: A phone receives a question

- **Given** a paired Android phone
- **When** Desktop A publishes a question
- **Then** the phone shows an OS notification with the title and summary, and set choices as notification actions

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
tests: [unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.TaskReplicaMergeSubjectTests.A_task_marked_done_on_the_phone_closes_its_notifications_on_the_owner, unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.TaskSubjectClosureTests.Marking_a_task_done_closes_the_notifications_about_it, unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.TaskSubjectClosureTests.A_task_that_comes_back_closes_nothing_and_ending_it_again_closes_again, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.SessionSubjectClosureTests.A_session_seen_open_and_then_archived_closes_its_notifications_once, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.SessionSubjectClosureTests.Finishing_a_run_closes_the_notifications_about_it_whatever_it_finished_on, unit:dotnet:Backlog.Desktop.UI.UnitTests.PullRequestSubjectsTests.A_reading_that_finds_it_merged_or_closed_closes_the_notifications_about_it_once, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.RemarkSubjectClosureTests.A_resolve_synced_in_from_another_device_closes_them_the_same_way, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.PairingSubjectsTests.A_code_that_expires_closes_the_notifications_about_its_pairing, unit:dotnet:Backlog.HostComposition.UnitTests.WebHarnessHostTests.The_desktop_harness_composes_a_subject_probe_for_every_context_that_owns_subjects]
```

The system SHALL close every Open notification about a subject when the context that owns the subject reports that it ended, wherever the end happened.

#### Scenario: A task done on the phone

- **Given** an Open question about task T-123, owned by Desktop A
- **When** the person marks T-123 Done on the phone, and the change syncs to Desktop A
- **Then** Desktop A closes the question with the reason "Task done"
