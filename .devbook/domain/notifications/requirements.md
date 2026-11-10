# Requirements

```meta
type: requirements
related: [.devbook/domain/notifications/features.md]
```

> What Notifications guarantees, one chapter per capability. Each requirement is
> one SHALL sentence with the scenarios that prove it. A requirement names its tests
> once what it guarantees is built.

## Publishing a notification

```meta
type: requirements
related: [.devbook/domain/notifications/features.md#publishing-a-notification, .devbook/domain/notifications/domain.md#notification]
```

> What a source gets when it publishes an alert or a question.

### Requirement: A source publishes an alert or a question

```meta
type: requirement
```

The system SHALL let a source on a desktop instance publish an Alert or a Question with a severity of info, warning or critical, and make that instance its owner.

#### Scenario: An agent asks a question

- **Given** a Claude session runs on Desktop A
- **When** it asks "Run the migration against staging?" with the choices Yes and No
- **Then** Desktop A owns an Open question with a single-choice answer spec, and shows it locally

### Requirement: A question names its subject

```meta
type: requirement
```

The system SHALL refuse a Question that names no subject.

#### Scenario: A question without a subject

- **Given** a source drafts a Question with no subject
- **When** it publishes the draft
- **Then** the publish is refused, and no notification exists

### Requirement: A question offers one of four answer shapes

```meta
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
type: requirement
```

The system SHALL keep a Question Open until it is answered or closed, however much time passes, and SHALL never answer it on the person's behalf.

#### Scenario: A question waits overnight

- **Given** an Open question nobody answered
- **When** a day passes
- **Then** it is still Open, with no answer

## Answering on the owner desktop

```meta
type: requirements
related: [.devbook/domain/notifications/features.md#answering-on-the-owner-desktop, .devbook/domain/notifications/context.md#owner-screens]
```

> What the person gets in the owner desktop's notification centre.

### Requirement: The centre follows the header's repository scope

```meta
type: requirement
related: [.devbook/arc42/adr/0027-the-brain-is-a-scope-beside-the-repositories.md]
tests: [unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.One_scoped_repository_lists_only_what_resolves_to_it_and_says_how_many_wait_outside, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.Several_scoped_repositories_list_their_union, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.The_brain_scoped_lists_what_resolves_to_no_repository, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.A_notification_whose_repository_could_not_be_worked_out_is_listed_under_any_scope, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.A_scope_pressed_while_the_centre_is_open_redraws_the_list_and_picks_what_is_still_shown]
```

The system SHALL list in the owner's centre every notification while the header's scope presses nothing, those about the pressed repositories while it presses some, and those about no repository while it presses the Brain, and SHALL list one whose repository could not be worked out under every scope.

#### Scenario: Two repositories pressed

- **Given** Desktop A holds a question about "backlog", one about "fincent" and a pairing question
- **When** the person presses "backlog" and, with Ctrl, "fincent" in the header
- **Then** the centre lists both repositories' questions and not the pairing question

#### Scenario: The Brain pressed

- **Given** the same three questions
- **When** the person presses the Brain
- **Then** the centre lists the pairing question only

#### Scenario: A repository not worked out

- **Given** a question whose source Desktop A cannot read
- **When** the person presses "backlog"
- **Then** the centre still lists the question

### Requirement: The centre never hides what waits

```meta
type: requirement
tests: [unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.The_waiting_badge_counts_open_questions_only_and_follows_answers, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.One_scoped_repository_lists_only_what_resolves_to_it_and_says_how_many_wait_outside, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.Asked_by_narrows_to_a_persons_questions_and_each_option_counts_what_it_keeps, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.Hiding_settled_ones_hides_no_open_question_so_the_subtitle_counts_none_as_filtered, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.Until_the_first_load_is_back_the_subtitle_makes_no_claim_about_what_is_waiting]
```

The system SHALL count every open question on the shell's Notifications option whatever the centre's scope and filters hold, and SHALL say in the centre's subtitle how many waiting questions the scope leaves out and how many the filters hide.

#### Scenario: Questions outside the scope

- **Given** Desktop A holds three open questions, one of them about "backlog"
- **When** the person presses "backlog" in the header
- **Then** the Notifications option still reads 3, and the subtitle reads "1 waiting for an answer · 2 more outside this scope"

#### Scenario: A question a filter hides

- **Given** an open question an AI agent asked and one a person asked
- **When** the person picks Person under Asked by
- **Then** the subtitle reads "1 waiting for an answer · 1 hidden by the filters"

#### Scenario: Settled ones hidden

- **Given** one open question and three settled notifications, with Show settled off
- **When** the person opens the centre
- **Then** the subtitle reads "1 waiting for an answer" and counts nothing as hidden

#### Scenario: Before the first load

- **Given** the centre has not yet worked out which repository each notification is about
- **When** the person opens it
- **Then** the subtitle reads "Loading…" until that answer returns

### Requirement: The centre lists open notifications until Show settled is pressed

```meta
type: requirement
tests: [unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.By_default_the_centre_lists_only_what_is_open, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.Show_settled_lists_the_settled_ones_after_the_open_and_pressed_again_hides_them, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.With_everything_settled_and_hidden_the_list_says_nothing_waits_and_the_detail_asks_for_no_pick]
```

The system SHALL list only the open notifications in the owner's centre until the person presses Show settled, which adds after them those answered, acknowledged or closed in the last seven days.

#### Scenario: Opening the centre

- **Given** Desktop A holds an open question and an open alert, and one answered, one acknowledged and one closed notification
- **When** the person opens the centre
- **Then** it lists the question and the alert only, and Show settled reads 3

#### Scenario: Showing the settled ones

- **Given** the same five notifications
- **When** the person presses Show settled
- **Then** the centre lists all five with the open question first, and pressing Show settled again hides the three settled ones

#### Scenario: Everything settled

- **Given** Desktop A's only notification is an answered question
- **When** the person opens the centre
- **Then** the list says nothing is waiting, and Show settled reads 1

### Requirement: Kind narrows the centre to questions or alerts

```meta
type: requirement
tests: [unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.Kind_narrows_to_the_alerts_and_clear_filters_lets_everything_back_but_keeps_group_by, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.A_remembered_kind_open_reads_as_all_with_settled_ones_hidden]
```

The system SHALL let the person narrow the owner's centre by Kind to All, Questions or Alerts, and SHALL read a Kind of Open that an older build remembered as All with settled ones hidden.

#### Scenario: Picking Alerts

- **Given** the centre lists an open question and an open alert
- **When** the person picks Alerts under Kind
- **Then** it lists the alert only

#### Scenario: A remembered Open

- **Given** an older build remembered Kind as Open on this desktop
- **When** the person opens the centre
- **Then** Kind reads All, Show settled is off, and the open question and alert are listed without the answered question

### Requirement: Each centre filter counts what it would list

```meta
type: requirement
tests: [unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.Every_tally_counts_only_what_would_be_listed_and_kind_all_counts_the_rows, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.The_settled_tally_counts_under_the_filters, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.Asked_by_narrows_to_a_persons_questions_and_each_option_counts_what_it_keeps]
```

The system SHALL show beside each option of the centre's filters how many notifications it would list under the scope, the other filters and Show settled, and beside Show settled how many settled notifications the scope and the filters keep.

#### Scenario: Tallies follow Show settled

- **Given** an open question and an open alert, and one answered, one acknowledged and one closed notification
- **When** the person presses Show settled
- **Then** Kind's All, Questions and Alerts read 5, 3 and 2 instead of 2, 1 and 1

#### Scenario: The settled tally under a filter

- **Given** the same five notifications, with Show settled off
- **When** the person picks Questions under Kind
- **Then** Show settled reads 2, the answered and the closed question

### Requirement: The centre groups its list with what waits first

```meta
type: requirement
tests: [unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.With_nothing_scoped_the_list_groups_by_repository_and_the_unfiled_sit_under_brain, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.With_one_repository_scoped_the_list_groups_by_system, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.The_list_groups_notifications_under_their_subject_with_the_open_question_group_first, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.Group_by_asked_by_puts_every_agents_question_in_one_group]
```

The system SHALL group the centre's list by the person's Group by choice, or when none is chosen by repository, or by system while one repository or the Brain alone is pressed, and SHALL order the groups and each group's rows open questions first, then newest first.

#### Scenario: Nothing pressed

- **Given** questions about "backlog" and "fincent" and a pairing question, and no Group by chosen
- **When** the person opens the centre with nothing pressed in the header
- **Then** the list is grouped by repository, the pairing question under Brain

#### Scenario: One repository pressed

- **Given** no Group by chosen
- **When** the person presses "backlog" in the header
- **Then** the list is grouped by system

#### Scenario: Grouped by subject

- **Given** an alert with no subject, and open questions about a session and a task
- **When** the list is grouped by subject
- **Then** the groups holding an open question come before the alert's

### Requirement: The centre remembers its choices and Clear filters resets only the filters

```meta
type: requirement
tests: [unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.The_centre_reopens_on_the_group_by_and_filters_chosen_last_on_this_device, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.Show_settled_is_remembered_on_this_device_and_the_centre_reopens_with_it, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.Clear_filters_leaves_show_settled_as_it_was, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.Kind_narrows_to_the_alerts_and_clear_filters_lets_everything_back_but_keeps_group_by, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.A_remembered_choice_this_build_does_not_know_falls_back_to_all]
```

The system SHALL remember the centre's Group by, filters and Show settled on the owner desktop, and SHALL set only the filters back to All when the person presses Clear filters.

#### Scenario: Reopening the centre

- **Given** the person grouped by subject, picked Alerts and pressed Show settled
- **When** they leave the centre and open it again
- **Then** it is grouped by subject, lists alerts only, and lists the settled ones

#### Scenario: Clearing the filters

- **Given** the centre is grouped by subject, with Questions picked and Show settled pressed
- **When** the person presses Clear filters
- **Then** Kind reads All, the list stays grouped by subject, and Show settled stays pressed

#### Scenario: A choice this build does not know

- **Given** this desktop remembered a Group by and filters this build does not offer
- **When** the person opens the centre
- **Then** it groups by repository, every filter reads All, and Clear filters is not offered

### Requirement: An answer given in the centre stays in view until another is picked

```meta
type: requirement
tests: [unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.One_answered_here_stays_listed_and_picked_until_another_is_picked, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.The_pane_opens_on_the_open_question_and_answers_it_through_the_centre]
```

The system SHALL keep a notification the person answers or acknowledges in the centre listed and picked, with the answer it got, until they pick another, even while settled ones are hidden.

#### Scenario: Answering in the centre

- **Given** two open questions, Show settled off, and the second one picked
- **When** the person presses Not now on it
- **Then** its row reads Answered with "Not now" and stays picked, and its card shows the answer

#### Scenario: Picking another

- **Given** the question just answered in the centre is still listed and picked
- **When** the person picks the other question
- **Then** the answered question leaves the list

### Requirement: A jump to a notification lands on it for that visit

```meta
type: requirement
related: [.devbook/design/interaction-guidelines.md#focus-and-selection]
tests: [unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.A_focus_on_a_settled_notification_lists_the_settled_ones_without_remembering_it, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.A_focus_on_a_notification_the_scope_and_a_filter_hide_clears_both_so_the_jump_lands, unit:dotnet:Backlog.Modules.Notifications.UnitTests.NotificationCentreScreenTests.The_pane_opens_on_the_notification_it_is_focused_on]
```

The system SHALL list and pick the notification the centre is opened on, for that visit listing settled ones and clearing whatever filter or scope hides it, and SHALL remember none of it.

#### Scenario: A jump to a closed notification

- **Given** a closed question, with Show settled off
- **When** the centre opens on it, as Open in Backlog opens it
- **Then** the question is picked with its reason, Show settled reads pressed, and the remembered choices still have it off

#### Scenario: A jump past the scope and a filter

- **Given** "backlog" is pressed in the header and Alerts is picked under Kind
- **When** the centre opens on a question about "fincent"
- **Then** the question is listed and picked, and Kind reads All

## Answering from another device

```meta
type: requirements
related: [.devbook/domain/notifications/features.md#answering-from-another-device, .devbook/domain/notifications/domain.md#reply, .devbook/arc42/06-runtime-view.md#notification-reply]
```

> What a person gets when they answer on the phone or on another desktop.

### Requirement: A viewer's reply becomes the answer only when the owner accepts it

```meta
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
type: requirement
```

The system SHALL accept the first valid reply the owner processes and reject every later reply as already answered.

#### Scenario: Two replies race

- **Given** an Open question
- **When** the phone replies Yes and Desktop B replies No, and the owner processes the phone's reply first
- **Then** the answer is Yes, Desktop B's reply is rejected as already answered, and both viewers show that the phone's Yes won

### Requirement: Any paired viewer may answer unless the question says owner only

```meta
type: requirement
```

The system SHALL accept a valid reply from any paired viewer instance, critical questions included, unless the question says `answerOn: owner`.

#### Scenario: An owner-only question

- **Given** an Open question that says `answerOn: owner`
- **When** the person replies on the phone
- **Then** the owner rejects the reply, and the question stays Open until it is answered on Desktop A

### Requirement: An invalid reply can be sent again

```meta
type: requirement
```

The system SHALL reject a reply that does not fit the answer spec with its reason, and let the viewer reply again.

#### Scenario: An open answer that is too long

- **Given** an open question with a length limit of 200 characters
- **When** a viewer sends 250 characters
- **Then** the reply is rejected as too long, and the viewer can send a shorter one

### Requirement: A dictated answer is sent only when the person sends it

```meta
type: requirement
tests: [unit:dotnet:Backlog.Mobile.UI.UnitTests.NotificationScreenTests.Dictating_an_open_answer_adds_the_words_and_sends_nothing_until_send, unit:dotnet:Backlog.Mobile.UI.UnitTests.NotificationPagerScreenTests.Dictating_on_the_pager_fills_the_box_and_waits_for_send]
```

The system SHALL add the words heard while the person dictates into an answer box to that box, and SHALL send the reply only when the person presses Send.

#### Scenario: Dictating an open answer on the phone

- **Given** an Open question with an open answer that the phone may answer
- **When** the person dictates "ja" into the answer box
- **Then** the box reads "ja" and no reply is queued, and pressing Send queues the reply

### Requirement: A viewer acknowledges an alert through its owner

```meta
type: requirement
related: [.devbook/domain/notifications/domain.md#acknowledgement, .devbook/arc42/adr/0039-notifications-are-an-owned-document-kind-and-push-only-nudges.md]
```

The system SHALL let any paired viewer acknowledge an open alert, mark it Acknowledged only when its owner takes the acknowledgement, then move it from every device's open stack to settled in its list.

#### Scenario: Dismissing an alert on the phone

- **Given** Desktop A owns an open alert, and the phone and Desktop B are paired
- **When** the person taps Dismiss on the alert in the phone's pager
- **Then** the alert leaves the phone's stack at once, Desktop A marks it Acknowledged, and it leaves Desktop A's open notifications, its Windows notification and Desktop B's open notifications
- **And** the phone's list keeps it, reading "Acknowledged on this phone"

#### Scenario: The owner is offline

- **Given** Desktop A owns an open alert and is asleep
- **When** the person taps Dismiss on the phone
- **Then** the alert leaves the phone's stack, the phone's list reads "delivers on wake", and Desktop A marks it Acknowledged when it wakes

#### Scenario: The phone is offline

- **Given** the phone has no network
- **When** the person taps Dismiss on an open alert
- **Then** the acknowledgement waits in the device outbox, the alert leaves the phone's stack, and the list reads "Sending" until the outbox delivers it

#### Scenario: The alert closed first

- **Given** an acknowledgement from the phone is on its way
- **When** the publisher closes the alert before Desktop A takes the acknowledgement
- **Then** the alert stays Closed with its reason, the acknowledgement is rejected as closed, and the phone shows it as Closed

#### Scenario: Refused by cloud sync

- **Given** an acknowledgement in the phone's outbox
- **When** cloud sync refuses it
- **Then** the alert returns to the phone's stack with the reason, and the person can dismiss it again

#### Scenario: Dismissed on two devices

- **Given** an open alert owned by Desktop A
- **When** the phone and Desktop B both acknowledge it
- **Then** Desktop A takes the first, the second changes nothing, and every device shows it as Acknowledged

### Requirement: The owner publishes the repository a notification is about

```meta
type: requirement
related: [.devbook/domain/notifications/context.md#owner-screens, .devbook/arc42/adr/0039-notifications-are-an-owned-document-kind-and-push-only-nudges.md, .devbook/arc42/adr/0027-the-brain-is-a-scope-beside-the-repositories.md]
```

The system SHALL publish with each notification the owner instance pushes the repository it is about, as the alias the owner's header scope shows, worked out by the rule the owner's centre keeps, or the Brain for a notification about no repository, and SHALL publish neither when the owner could not work it out.

#### Scenario: A run's validation question

- **Given** Desktop A aliases its clone of `JSdotNet/Backlog` as "Backlog"
- **When** a run in that clone reaches Personal Validation and Desktop A pushes its question
- **Then** the published question names the repository "Backlog"

#### Scenario: A pairing question

- **Given** a device redeems Desktop A's pairing code
- **When** Desktop A pushes "Pair *device name*?"
- **Then** the published question is the Brain's

#### Scenario: The repository cannot be worked out

- **Given** Desktop A cannot read the source that would name a notification's repository
- **When** it pushes the notification
- **Then** the published notification names no repository and is not the Brain's, and reads as not known

#### Scenario: An owner from before the repository was published

- **Given** a notification an older owner pushed without a repository
- **When** the phone reads it
- **Then** its repository reads as not known

### Requirement: The phone filters its notifications by system and repository

```meta
type: requirement
related: [.devbook/domain/notifications/domain.md#origin, .devbook/domain/notifications/context.md#owner-screens]
```

The system SHALL let the person narrow both views of the phone's Notifications tab by From, the system a notification came from, and by Repository, the repository its owner published or the Brain, and SHALL keep a notification whose repository is not known under every Repository choice.

#### Scenario: Picking a system

- **Given** the phone holds open questions from Claude Code and from spec-manager
- **When** the person picks Claude Code under From
- **Then** the list shows only Claude Code's notifications, and the pager's stack, its position and its end screen count only Claude Code's waiting questions

#### Scenario: Picking the Brain

- **Given** the phone holds a pairing question and a question about the repository "Backlog"
- **When** the person picks Brain under Repository
- **Then** both views show the pairing question and not the one about "Backlog"

#### Scenario: A repository not known

- **Given** a notification whose repository is not known
- **When** the person picks any choice under Repository
- **Then** the notification is still shown

### Requirement: Each phone filter counts what it keeps and shows only a real choice

```meta
type: requirement
```

The system SHALL show beside each option of a phone filter how many notifications it would keep under the other filter, and SHALL not show a filter with fewer than two options.

#### Scenario: Counts under the other filter

- **Given** Repository is set to "Backlog", which holds two Claude Code notifications and one from spec-manager
- **When** the person opens From
- **Then** Claude Code reads 2 and spec-manager reads 1

#### Scenario: One system only

- **Given** every notification on the phone came from Claude Code
- **When** the person opens the Notifications tab
- **Then** no From filter is shown

### Requirement: The phone remembers its filters until the person clears them

```meta
type: requirement
```

The system SHALL keep the phone's From and Repository choices on the device across both views and across restarts, and SHALL set both back to All when the person presses Clear filters.

#### Scenario: Filters survive a restart

- **Given** the person picked Claude Code under From
- **When** they close the app and open it again on the Notifications tab
- **Then** From still reads Claude Code, in the pager and in the list

#### Scenario: A filter that leaves nothing

- **Given** the person picked a repository whose notifications have all been closed
- **When** the tab has nothing left to show
- **Then** it shows an empty state with Clear filters instead of "No notifications", and pressing Clear filters shows every notification again

### Requirement: Phone filters never hide what waits

```meta
type: requirement
```

The system SHALL count every open question in the tab's waiting count and SHALL push every notification to the phone, whatever the phone's filters hold.

#### Scenario: A question outside the filter

- **Given** the person picked spec-manager under From
- **When** Desktop A publishes a Claude Code question
- **Then** the phone's tray shows it, and the waiting count counts it, though neither view lists it

### Requirement: Android is the only phone platform

```meta
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
type: requirements
related: [.devbook/domain/notifications/features.md#asking-from-the-contexts, .devbook/domain/notifications/context.md]
```

> What the contexts that publish promise the person.

### Requirement: A run at Personal Validation asks to be approved

```meta
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
type: requirements
related: [.devbook/domain/notifications/features.md#closing-a-notification, .devbook/domain/notifications/domain.md#notification-state, .devbook/domain/notifications/domain.md#subject]
```

> When a notification stops needing attention without being answered.

### Requirement: The publisher closes a notification

```meta
type: requirement
```

The system SHALL let the source that published a notification close it, and SHALL reject a reply that reaches the owner after that as closed.

#### Scenario: The agent no longer needs the answer

- **Given** an Open question from a Claude session
- **When** the session closes the question
- **Then** every viewer shows it as Closed, and a reply sent afterwards is rejected as closed

### Requirement: A notification closes when its subject ends

```meta
type: requirement
tests: [unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.TaskReplicaMergeSubjectTests.A_task_marked_done_on_the_phone_closes_its_notifications_on_the_owner, unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.TaskSubjectClosureTests.Marking_a_task_done_closes_the_notifications_about_it, unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.TaskSubjectClosureTests.A_task_that_comes_back_closes_nothing_and_ending_it_again_closes_again, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.SessionSubjectClosureTests.A_session_seen_open_and_then_archived_closes_its_notifications_once, unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.SessionSubjectClosureTests.Finishing_a_run_closes_the_notifications_about_it_whatever_it_finished_on, unit:dotnet:Backlog.Desktop.UI.UnitTests.PullRequestSubjectsTests.A_reading_that_finds_it_merged_or_closed_closes_the_notifications_about_it_once, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.RemarkNotificationStoreTests.A_resolve_synced_in_from_another_device_closes_them_the_same_way, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.PairingSubjectsTests.A_code_that_expires_unredeemed_closes_the_notifications_about_its_pairing, unit:dotnet:Backlog.HostComposition.UnitTests.WebHarnessHostTests.The_desktop_harness_composes_a_subject_probe_for_every_context_that_owns_subjects, unit:dotnet:Backlog.HostComposition.UnitTests.WebHarnessHostTests.The_desktop_harness_composes_an_answer_handler_for_every_context_that_asks]
```

The system SHALL close every Open notification about a subject when the context that owns the subject reports that it ended, wherever the end happened.

#### Scenario: A task done on the phone

- **Given** an Open question about task T-123, owned by Desktop A
- **When** the person marks T-123 Done on the phone, and the change syncs to Desktop A
- **Then** Desktop A closes the question with the reason "Task done"
