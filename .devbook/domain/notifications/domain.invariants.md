# Invariants

```meta
status: proposed
type: invariants
```

What each aggregate on [`domain.md`](domain.md) enforces, one chapter per aggregate, with the unit tests that prove each rule built so far.

## Notification

```meta
status: proposed
type: invariants
related: [.devbook/domain/notifications/domain.md#notification]
```

> The rules the Notification holds its kind, its answer and its lifecycle to.

### Invariant: Only the owner instance changes a notification

```meta
status: proposed
type: invariant
tests: [unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationInvariantTests.Only_the_owner_instance_closes_a_notification, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationInvariantTests.Only_the_owner_instance_accepts_a_reply, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationInvariantTests.Only_the_owner_instance_acknowledges_an_alert]
```

Every state change and every accepted answer comes from the owner instance that published the notification (`notification.not_owner`). One writer per document is what lets the replica carry it without merge rules.

Enforced at: all mutations

### Invariant: A question has an answer spec and a subject

```meta
status: proposed
type: invariant
tests: [unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationInvariantTests.A_question_without_a_subject_is_refused, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationInvariantTests.A_question_without_an_answer_spec_is_refused, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationInvariantTests.An_alert_may_have_no_subject_and_carries_no_answer_spec]
```

A notification of kind Question carries an answer spec and a subject (`notification.question_incomplete`). An alert carries no answer spec, and may have no subject.

Enforced at: constructor

### Invariant: Kind and severity come from their fixed sets

```meta
status: proposed
type: invariant
tests: [unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationInvariantTests.An_unknown_kind_is_refused, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationInvariantTests.An_unknown_severity_is_refused]
```

The kind is Alert or Question, and the severity is info, warning or critical (`notification.unknown_kind`, `notification.unknown_severity`).

Enforced at: constructor

### Invariant: No deadline and no default answer

```meta
status: proposed
type: invariant
tests: [unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationInvariantTests.Neither_the_draft_nor_the_notification_carries_a_deadline_or_a_default_answer, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationInvariantTests.A_question_nobody_answered_is_still_open_however_much_time_passes]
```

A notification carries no deadline and no default answer, so time alone never settles a question (`notification.deadline_not_supported`).

Enforced at: constructor

### Invariant: State moves only forward from Open

```meta
status: proposed
type: invariant
tests: [unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationInvariantTests.An_alert_moves_once_from_open_to_acknowledged, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationInvariantTests.Only_an_alert_is_acknowledged, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationInvariantTests.Only_a_question_is_answered, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationInvariantTests.A_closed_notification_stays_closed]
```

A notification starts Open and moves once, to Acknowledged, Answered or Closed (`notification.already_settled`). Only an alert is acknowledged, and only a question is answered.

Enforced at: `Acknowledge()`, `Accept()`, `Close()`, `CloseForSubject()`

### Invariant: A question has at most one answer

```meta
status: proposed
type: invariant
tests: [unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationInvariantTests.The_first_valid_reply_becomes_the_answer_and_every_later_one_is_rejected, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationsTests.Two_replies_racing_settle_on_exactly_one_answer]
```

The first valid reply the owner processes becomes the answer, and every later reply is rejected as already answered (`notification.already_answered`).

Enforced at: `Accept()`

### Invariant: Title and summary stay within their bounds

```meta
status: proposed
type: invariant
tests: [unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationInvariantTests.A_title_holds_at_most_80_characters, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationInvariantTests.A_summary_holds_at_most_180_characters, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationInvariantTests.A_body_holds_at_most_32_KB_of_Markdown]
```

The title holds at most 80 characters and the summary at most 180, both plain text (`notification.content_too_long`). The body holds at most 32 KB of Markdown.

Enforced at: constructor

### Invariant: An origin stays within its bounds

```meta
status: proposed
type: invariant
tests: [unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationOriginTests.A_part_past_its_bound_is_refused, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationOriginTests.An_origin_without_a_system_is_refused, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationOriginTests.A_part_that_runs_over_a_line_is_refused, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationOriginTests.A_link_that_is_not_http_or_https_is_refused]
```

An [origin](domain.md#origin) names its system. Its system holds at most 40
characters, its place 120, who asked 80 and how 40, each one line of plain text
(`notification.content_too_long`, `notification.origin_invalid`). Its link is an
absolute `http` or `https` address.

Enforced at: constructor

## Reply

```meta
status: proposed
type: invariants
related: [.devbook/domain/notifications/domain.md#reply]
```

> The rules a reply must meet before the owner accepts it as the answer.

### Invariant: A reply fits the answer spec

```meta
status: proposed
type: invariant
tests: [unit:dotnet:Backlog.Modules.Notifications.UnitTests.ReplyInvariantTests.A_single_choice_reply_picks_exactly_one_offered_choice, unit:dotnet:Backlog.Modules.Notifications.UnitTests.ReplyInvariantTests.A_multiple_choice_reply_picks_offered_choices_within_the_limits, unit:dotnet:Backlog.Modules.Notifications.UnitTests.ReplyInvariantTests.An_open_reply_is_plain_text_within_the_length_limit, unit:dotnet:Backlog.Modules.Notifications.UnitTests.ReplyInvariantTests.Choices_with_open_take_one_choice_or_the_open_answer_but_not_both, unit:dotnet:Backlog.Modules.Notifications.UnitTests.ReplyInvariantTests.A_choice_that_takes_no_text_is_refused_with_text, unit:dotnet:Backlog.Modules.Notifications.UnitTests.ReplyInvariantTests.A_choices_text_runs_to_2000_characters_when_the_question_has_no_open_answer, unit:dotnet:Backlog.Modules.Notifications.UnitTests.ReplyInvariantTests.A_choices_text_keeps_within_the_open_answers_limit, unit:dotnet:Backlog.Modules.Notifications.UnitTests.ReplyInvariantTests.A_note_goes_beside_a_choice_or_an_open_answer_within_its_limit, unit:dotnet:Backlog.Modules.Notifications.UnitTests.ReplyInvariantTests.A_question_without_a_note_takes_no_note]
```

A reply holds only choice ids the answer spec offers, within its count limits, text only where the spec allows it, as the open answer or with a choice that takes text, and within its length, and a note only where the spec takes one and within its length (`reply.invalid`). A choice's text keeps to the open answer's limit, or to 2,000 characters when the question has no open answer. A note never stands in for a choice's words: the words travel as the reply's text, the note beside them.

Enforced at: `Accept()` on the Notification

### Invariant: A choice that takes text comes with text

```meta
status: proposed
type: invariant
tests: [unit:dotnet:Backlog.Modules.Notifications.UnitTests.ReplyInvariantTests.A_choice_that_takes_text_comes_with_text, unit:dotnet:Backlog.Modules.Notifications.UnitTests.ReplyInvariantTests.The_owner_refuses_a_choice_that_takes_text_sent_without_it_and_stays_open, unit:dotnet:Backlog.Modules.Notifications.UnitTests.ReplyInvariantTests.A_choices_words_and_a_note_travel_apart]
```

A reply that picks a choice which takes text carries non-empty plain text with it (`reply.invalid`). Without it, Approve + follow-up would file an entry with nothing to do.

Enforced at: `Accept()` on the Notification

### Invariant: A reply to a closed question is rejected

```meta
status: proposed
type: invariant
tests: [unit:dotnet:Backlog.Modules.Notifications.UnitTests.ReplyInvariantTests.A_reply_after_the_question_closed_is_rejected_as_closed]
```

A reply that reaches the owner after the question closed is rejected as closed (`reply.closed`).

Enforced at: `Accept()` on the Notification

### Invariant: An owner-only question takes only the owner's reply

```meta
status: proposed
type: invariant
tests: [unit:dotnet:Backlog.Modules.Notifications.UnitTests.ReplyInvariantTests.An_owner_only_question_rejects_a_viewers_reply_and_takes_the_owners]
```

When the answer spec says `answerOn: owner`, a reply from any viewer instance is rejected (`reply.owner_only`). Without it, any paired viewer may answer.

Enforced at: `Accept()` on the Notification

### Invariant: A reply comes from the same owner

```meta
status: proposed
type: invariant
tests: [integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.NotificationSyncEndpointTests.A_reply_from_a_device_under_another_owner_is_refused, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.NotificationSyncTests.A_reply_stamped_with_another_owner_is_rejected_and_never_reaches_the_gate]
```

A reply is accepted only from a device under the same owner as the notification (`reply.foreign_owner`). The sync service checks it first, and the owner instance checks it again.

Enforced at: the sync reply endpoint, and the owner desktop's notification exchange before `Accept()`

The sync service stores a reply only in its poster's own partition, against a notification found there. The owner's exchange compares the account the replica stamped on the reply with its own and rejects a mismatch before the reply reaches `Accept()`, which then checks that it is addressed to this owner instance (`reply.wrong_instance`).
