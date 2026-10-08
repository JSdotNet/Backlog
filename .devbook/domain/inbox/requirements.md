# Requirements

```meta
type: requirements
status: draft
related: [.devbook/domain/inbox/features.md]
```

> What this context's features guarantee, one chapter per feature. Each
> requirement is one SHALL sentence with the scenarios that prove it.

## Columns

```meta
type: requirements
status: draft
related: [.devbook/domain/inbox/features.md#incoming-queue, .devbook/domain/inbox/features.md#add-by-hand, .devbook/domain/inbox/features.md#filter-by-content-kind, .devbook/domain/inbox/features.md#per-item-triage-actions]
```

> The requirements of the Inbox pane's three columns: the header's capture
> field, the rows, the kind pills and the detail's decisions.

### Requirement: Enter files the capture field's title

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Enter_in_the_field_files_the_title_and_empties_the_field, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Enter_on_a_blank_field_files_nothing]
```

The system SHALL file the text in the header's capture field as the title of a new unfiled item when the reader presses Enter, and empty the field.

#### Scenario: A thought typed at the desk

- **Given** the Inbox pane is open
- **When** the reader types "Ask about the Cosmos emulator" in the capture field and presses Enter
- **Then** an item titled "Ask about the Cosmos emulator" lands in the queue with no notes, and the field is empty

### Requirement: Shift+Enter opens the notes editor

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Shift_enter_opens_the_notes_editor_and_files_nothing
```

The system SHALL open a notes editor under the capture field when the reader presses Shift+Enter in it, and file nothing.

#### Scenario: Adding notes to a thought

- **Given** the reader has typed a title in the capture field
- **When** they press Shift+Enter
- **Then** a notes editor opens under the field, the title stays, and no item is filed

### Requirement: Ctrl+Enter files the title and the notes

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Ctrl_enter_files_the_title_and_the_notes_and_closes_the_notes
```

The system SHALL file the capture field's title together with the notes editor's text as one new item when the reader presses Ctrl+Enter, and empty and close both.

#### Scenario: A thought with notes

- **Given** the capture field holds a title and the notes editor holds two lines
- **When** the reader presses Ctrl+Enter
- **Then** one item is filed with that title and those two lines as its notes, and the notes editor closes

### Requirement: Escape closes the notes editor

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Escape_closes_the_notes_keeps_the_title_and_files_nothing
```

The system SHALL close the notes editor when the reader presses Escape in it, keeping the title in the capture field and filing nothing.

#### Scenario: Changing one's mind about notes

- **Given** the notes editor is open under a typed title
- **When** the reader presses Escape
- **Then** the notes editor closes, the title is still in the field, and no item is filed

### Requirement: Rows are grouped by age

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Rows_are_grouped_by_age_with_a_count_per_heading, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Today_starts_at_midnight_and_an_empty_heading_is_not_shown, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Today_is_counted_from_local_midnight, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.This_week_reaches_back_exactly_seven_days]
```

The system SHALL group the rows under the headings Today, This week and Older than a week by capture time, each with its count, and SHALL not show a heading with no rows.

#### Scenario: A mixed slice

- **Given** a slice with two items captured today, one four days ago and one nine days ago
- **When** the pane shows it
- **Then** the rows read "Today · 2", "This week · 1" and "Older than a week · 1"

#### Scenario: Nothing older than a week

- **Given** every item in the slice was captured this week
- **When** the pane shows it
- **Then** no "Older than a week" heading is shown

### Requirement: Kind pills are led by All

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Kind_pills_are_led_by_all_show_the_kinds_in_the_slice_with_counts_and_toggle_the_rows
```

The system SHALL show the kind filters as pills led by an All pill carrying the slice's count, pressed while no kind is, and SHALL clear every kind filter when All is pressed.

#### Scenario: Back to everything

- **Given** the Video pill is pressed and only videos are shown
- **When** the reader presses All
- **Then** every row of the slice is shown and All is the pressed pill

### Requirement: The decisions stay at the foot of the detail

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.The_four_decisions_sit_in_a_bar_at_the_foot_of_the_detail_with_their_keys
```

The system SHALL show Move to backlog, Move to list, Defer and Archive in a bar fixed at the foot of the detail, each naming its key, that stays in view however far the detail scrolls.

#### Scenario: A long article

- **Given** an item whose notes run past the bottom of the detail
- **When** the reader scrolls to the end of the notes
- **Then** the four decisions are still in view at the foot of the detail

### Requirement: Sources open from the header

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Sources_take_the_body_and_back_to_inbox_returns_the_queue
```

The system SHALL open the watched sources from a Sources button in the pane's header.

#### Scenario: Checking what is watched

- **Given** the Inbox pane is open
- **When** the reader presses Sources in the header
- **Then** the sources and their settings are shown

## Act on several at once

```meta
type: requirements
status: draft
related: [.devbook/domain/inbox/features.md#act-on-several-at-once]
```

> The requirements of picking several Inbox Items and acting on them together.
> The feature chapter says what the capability is and why; this says what it
> promises.

### Requirement: A shift press picks the run

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Select_puts_a_box_on_every_row_and_a_shift_click_takes_the_range_with_a_running_count
```

The system SHALL give every row between the last box pressed and a box pressed with Shift the state that box now shows, and count the picked items as it does.

#### Scenario: Extending a pick

- **Given** four items are shown and the first is picked
- **When** the reader Shift-presses the third
- **Then** the first three are picked and the bar reads "3 items selected"

#### Scenario: Giving a run back

- **Given** the first three items are picked and the third was pressed last
- **When** the reader Shift-presses the second, which is picked
- **Then** the second and third are given back and one item stays picked

### Requirement: Each item is decided by its own act

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.BatchTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Archive_across_the_selection_goes_through_one_batch_and_says_how_many]
```

The system SHALL apply a bulk Archive, Move to list, Tags or Repositories change to each picked item through the same act that changes one item, so an item that act refuses is refused and the others still change.

#### Scenario: One routed item among several archived

- **Given** three picked items, one of them already routed
- **When** the reader archives the selection
- **Then** the other two are archived and the routed item is not

#### Scenario: A list that has gone

- **Given** two picked items and a list that has since been deleted
- **When** the items are moved to that list
- **Then** neither item is changed and both are reported as refused

### Requirement: A partial failure names what it did not change

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.A_partial_failure_names_the_item_it_could_not_change_rather_than_reporting_success, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.A_bulk_sentence_leads_with_what_landed_and_names_what_did_not]
```

The system SHALL report a bulk act by how many items it changed, how many were already at the value, and the title and reason of every item it could not change, and SHALL report it as a warning rather than a success when any item was refused.

#### Scenario: One of two refused

- **Given** two picked items, one of which its act refuses
- **When** the reader archives the selection
- **Then** a warning reads "1 item archived." followed by the refused item's title and reason, and that item stays shown and picked

### Requirement: Tags are added, not replaced

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Adding_tags_keeps_each_items_own_and_leaves_an_item_that_has_them_alone
```

The system SHALL add the chosen tags to each picked item's own tags, or take one named tag off each, and SHALL leave unwritten an item already at the result.

#### Scenario: Items with tags of their own

- **Given** three picked items, one already tagged `q4`, one tagged `reading`, one bare
- **When** the reader adds `q4`
- **Then** the second carries `reading` and `q4`, the third `q4`, and the first is counted as already up to date without being written

### Requirement: A decided item keeps its tags and repositories

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Assigning_repositories_replaces_them_and_names_a_routed_item_it_will_not_touch
```

The system SHALL refuse, by name, a bulk tag or repository change to an item that has been routed or archived.

#### Scenario: Repositories across an open and a routed item

- **Given** an open item targeting one repository and a routed item, both picked
- **When** the reader assigns another repository
- **Then** the open item targets only the new repository and the routed item is named as not changed

### Requirement: The selection follows what is shown

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.The_selection_survives_a_refresh_is_pruned_by_a_filter_and_clears_when_the_slice_changes
```

The system SHALL keep picked items picked across a refresh of the pane while they are still shown, drop an item from the selection when it stops being shown, and empty the selection when another slice is opened.

#### Scenario: A refresh while items are picked

- **Given** two items are picked
- **When** a sync brings in a third and the pane refreshes
- **Then** the same two items are still picked

#### Scenario: A filter hides a picked item

- **Given** a picked video and a picked note
- **When** the reader filters to videos
- **Then** only the video is picked

#### Scenario: Opening another list

- **Given** items are picked in the inbox
- **When** the reader opens a list in the side menu
- **Then** nothing is picked

## Triage from the keyboard

```meta
type: requirements
status: draft
related: [.devbook/domain/inbox/features.md#quick-triage-shortcuts, .devbook/domain/inbox/features.md#triage-mode]
```

> The requirements of triaging the Inbox from the keyboard and one item at a
> time. The feature chapter says what the keys are and why; this says what
> they promise.

### Requirement: A key does what its button does

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxKeyboardTriageTests.A_and_r_decide_the_chosen_item_the_way_its_buttons_do, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxKeyboardTriageTests.A_key_does_not_offer_an_act_the_header_withholds, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxKeyboardTriageTests.L_opens_move_to_list_and_d_offers_review_dates_that_defer_the_item]
```

The system SHALL decide the chosen item by a triage key through the same act its detail's button performs, and SHALL offer by key no act the detail withholds for that item.

#### Scenario: Archiving by key

- **Given** an unprocessed item is chosen
- **When** the reader presses a
- **Then** the item is archived

#### Scenario: A routed item

- **Given** an item already moved to the backlog is chosen
- **When** the reader presses a or d
- **Then** the item stays as it is and no dialog opens

### Requirement: A key in a field types

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxKeyboardTriageTests.Components_js_decides_at_the_keydown_that_a_key_in_a_field_or_with_a_modifier_types, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxKeyboardTriageTests.The_pane_registers_for_shortcuts_and_no_inbox_markup_arms_a_server_side_prevent_default]
```

The system SHALL treat a letter pressed in a text field, inside a dialog, with Ctrl, Alt or Meta held, or while the focus is outside the Inbox pane as that place's key and not as a shortcut.

#### Scenario: Typing a tag

- **Given** the reader is typing in the tag field
- **When** they type "a"
- **Then** the letter appears in the field and nothing is archived

### Requirement: Triage moves on after each decision

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxKeyboardTriageTests.Triage_shows_one_item_counted_and_moves_on_after_each_decision, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxKeyboardTriageTests.Past_the_last_row_triage_returns_to_the_item_skipped_on_the_way]
```

The system SHALL, in triage mode, show one item with its place among the rows shown and move on to the next row after each archive, deferral, move to a list or move to the backlog.

#### Scenario: Archiving the first of three

- **Given** triage mode is open on the first of three items
- **When** the reader archives it
- **Then** the second item is shown

#### Scenario: Deciding the last row

- **Given** the reader skipped the first item with j and is on the last
- **When** they archive it
- **Then** the skipped item is shown

### Requirement: Leaving triage keeps the reader's place

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxKeyboardTriageTests.Escape_leaves_triage_on_the_row_the_reader_stopped_at
```

The system SHALL return from triage mode to the rows with the item the session stopped at chosen and focused.

#### Scenario: Escape on the second item

- **Given** triage mode shows the second of three items
- **When** the reader presses Escape
- **Then** the list is shown with the second item chosen and focused

### Requirement: T starts triage, G goes to the tags, U undoes

```meta
type: requirement
status: draft
```

The system SHALL start triage mode on t, move the focus to the chosen item's tags on g, and undo the session's latest decision on u, leaving every other triage key as it was.

#### Scenario: Starting triage

- **Given** the list is shown with an unprocessed item chosen
- **When** the reader presses t
- **Then** triage mode opens on that item

#### Scenario: Tagging

- **Given** an unprocessed item is chosen
- **When** the reader presses g
- **Then** the focus is in the item's tag field, and pressing t there types the letter

#### Scenario: Undoing by key

- **Given** the reader archived an item a moment ago
- **When** they press u
- **Then** the item is open again

### Requirement: Triage counts the session

```meta
type: requirement
status: draft
```

The system SHALL show over triage mode a progress bar and the line "n of total · m decided this session", where total is the number of items in the slice when triage began, n the place of the item shown among them, and m the decisions taken since the app opened.

#### Scenario: Halfway through

- **Given** triage began on a slice of 23 items and the reader has decided 11 of them
- **When** the twelfth item is shown
- **Then** the line reads "12 of 23 · 11 decided this session" and the bar is a little short of half full

### Requirement: AI cards come first and the suggestions are numbered after them

```meta
type: requirement
status: draft
```

The system SHALL number the AI cards shown in triage 1 and 2, at most two of them, and number the rule-based suggestions after the last card shown.

#### Scenario: Two cards and three suggestions

- **Given** triage shows an item with a duplicate card, a plan card and three suggestions
- **When** the reader presses 3
- **Then** the first suggestion is taken

#### Scenario: No cards

- **Given** no AI card is shown for the item
- **When** the reader presses 1
- **Then** the first suggestion is taken

### Requirement: Several repositories are picked at once

```meta
type: requirement
status: draft
```

The system SHALL offer in triage a repository picker with one box per configured repository, any number of which can be ticked, and SHALL move the item to the backlog in every ticked repository.

#### Scenario: Two repositories

- **Given** triage shows an item and the reader ticks Backlog and backlog-sync
- **When** they press r
- **Then** the item is moved to the backlog as one task in each of the two repositories

### Requirement: Triage shows the four decisions as buttons

```meta
type: requirement
status: draft
```

The system SHALL show in triage mode four decision buttons — Move to backlog, Move to list, Defer and Archive — each naming its key and doing what that key does.

#### Scenario: Deferring by button

- **Given** triage mode shows an unprocessed item
- **When** the reader presses the Defer button
- **Then** the review dates to defer to are offered, as d offers them

### Requirement: Up next shows what follows

```meta
type: requirement
status: draft
```

The system SHALL list beside the item in triage mode the items that follow it in the slice, in the order triage will show them.

#### Scenario: After a decision

- **Given** Up next lists B and C under item A
- **When** the reader archives A
- **Then** B is shown and Up next lists C first

### Requirement: The last decision is shown with Undo

```meta
type: requirement
status: draft
```

The system SHALL show in triage mode the session's latest decision, naming the item and what was done with it, with an Undo button that takes it back.

#### Scenario: Filing in a list

- **Given** the reader filed ".NET 11 performance" in Reading
- **When** the next item is shown
- **Then** the last decision reads ".NET 11 performance → Reading" with Undo beside it

## AI triage

```meta
type: requirements
status: draft
related: [.devbook/domain/inbox/features.md#ai-triage-cards, .devbook/arc42/adr/0023-inbox-items-may-be-read-by-the-foundry-model.md]
```

> The requirements of the model reading an item in triage. What is sent, and
> why, is settled in local ADR 0023; this says what the reader can count on.

### Requirement: The model is asked only when an item is opened in triage

```meta
type: requirement
status: draft
```

The system SHALL send an item to the Foundry model for its cards only when the reader opens that item in triage mode, at most once per item until the app closes, and never on intake, on a timer or while the list is browsed.

#### Scenario: Browsing the list

- **Given** Foundry is configured
- **When** the reader moves through ten rows in the list with j
- **Then** no item is sent to the model

#### Scenario: Going back an item

- **Given** triage showed item A, with its cards, and moved on to B
- **When** the reader presses k
- **Then** A is shown with the same cards and no new call is made

### Requirement: Only the settled fields are sent

```meta
type: requirement
status: draft
```

The system SHALL send for the item only its title, link, notes, kind, source, person, tags and repository ids, and for matching only the titles, ids, repositories and statuses of the open backlog tasks and of the other unprocessed inbox items and the names of the configured repositories and inbox lists.

#### Scenario: An item with a photo

- **Given** an unprocessed item with notes and an attached photo
- **When** it is opened in triage
- **Then** the request carries its notes and no attachment, and no closed task or archived item is in it

### Requirement: Without Foundry no AI surface is shown

```meta
type: requirement
status: draft
```

The system SHALL show no AI card and no Let AI propose the rest when Foundry is not configured, and SHALL show the rule-based suggestions as it does with Foundry.

#### Scenario: No deployment configured

- **Given** Foundry is not configured
- **When** the reader opens an item in triage
- **Then** no AI card and no Let AI propose the rest are shown, and the item's suggestions are numbered from 1

### Requirement: A failed call hides the cards

```meta
type: requirement
status: draft
```

The system SHALL show no AI card for an item whose model call failed and SHALL leave triage of that item otherwise unchanged.

#### Scenario: The endpoint cannot be reached

- **Given** Foundry is configured and its endpoint cannot be reached
- **When** the reader opens an item in triage
- **Then** the item is shown with its suggestions and four decisions, and with no AI card

## Merge into a task

```meta
type: requirements
status: draft
related: [.devbook/domain/inbox/features.md#merge-into-a-task]
```

> The requirements of folding a capture into the backlog task it repeats.

### Requirement: Merging comments on the task and archives the capture

```meta
type: requirement
status: draft
```

The system SHALL add the capture's title, link and notes to the backlog task as a comment and archive the capture with its DuplicateOf naming that task.

#### Scenario: Merging a duplicate

- **Given** a capture "Scroll jumps to top when phone edits an entry" with a link and notes, and the card proposes the task "Tasks list re-keys on remote change"
- **When** the reader takes Merge into the task
- **Then** the task carries a comment with the capture's title, link and notes, and the capture is archived as a duplicate of the task

## Undo a decision

```meta
type: requirements
status: draft
related: [.devbook/domain/inbox/features.md#undo-a-decision]
```

> The requirements of taking back a decision of this session.

### Requirement: Every decision of the session can be undone, newest first

```meta
type: requirement
status: draft
```

The system SHALL undo the session's newest decision not yet undone — an archive, a deferral, a move to a list, a move to the backlog or a merge — and return the item to the state it had before it, until the app closes.

#### Scenario: Two decisions undone

- **Given** the reader deferred item A and then archived item B
- **When** they undo twice
- **Then** B is open again first, then A, each as it was before its decision

#### Scenario: After a restart

- **Given** the reader archived an item and closed the app
- **When** the app opens again
- **Then** there is no decision to undo

### Requirement: Undoing a move to the backlog deletes only unstarted tasks

```meta
type: requirement
status: draft
```

The system SHALL undo a move to the backlog by deleting the tasks it made only while none of them has started, and otherwise SHALL refuse the undo with a sentence naming the started task and change nothing.

#### Scenario: Nothing started

- **Given** an item was moved to the backlog as two tasks, neither started
- **When** the reader undoes the move
- **Then** both tasks are deleted and the item is open again

#### Scenario: One task started

- **Given** an item was moved to the backlog as two tasks and one of them, "Fix the scroll jump", has started
- **When** the reader undoes the move
- **Then** the undo is refused with a sentence naming "Fix the scroll jump", and both tasks and the item stay as they are

### Requirement: Undoing a merge keeps the comment

```meta
type: requirement
status: draft
```

The system SHALL undo a merge by restoring the capture to the state it had before it and SHALL leave the comment the merge added on the task.

#### Scenario: Merged by mistake

- **Given** a capture was merged into a task
- **When** the reader undoes the merge
- **Then** the capture is open again with no DuplicateOf, and the task still carries the comment

## Inbox zero

```meta
type: requirements
status: draft
related: [.devbook/domain/inbox/features.md#inbox-zero]
```

> The requirements of what the pane shows once the session has emptied it.

### Requirement: An emptied slice reports the session

```meta
type: requirement
status: draft
```

The system SHALL, when the open slice is empty after decisions this session, show the session's count per decision, the deferred items with a review date coming back soonest first and at most five, and links to the fullest list and to Deferred.

#### Scenario: The last item decided

- **Given** the reader moved 9 items to the backlog, filed 7, deferred 3 and archived 4 this session, and seven deferred items have a review date
- **When** the last open item of the slice is decided
- **Then** the pane shows those four counts, the five deferred items coming back soonest, and links to the fullest list and to Deferred

#### Scenario: A slice that was already empty

- **Given** a list with no open items and no decisions this session
- **When** the reader opens it
- **Then** the plain empty state is shown, with no counts

## AI triage pass

```meta
type: requirements
status: draft
related: [.devbook/domain/inbox/features.md#ai-triage-pass, .devbook/arc42/adr/0023-inbox-items-may-be-read-by-the-foundry-model.md]
```

> The requirements of the review screen the AI triage pass opens.

### Requirement: The pass proposes a decision with a reason for every item

```meta
type: requirement
status: draft
```

The system SHALL show on the review screen, for every unprocessed item of the slice the model placed, a proposed plan, duplicate, single route, list filing or archive, each with its reason.

#### Scenario: Twelve items waiting

- **Given** twelve unprocessed items and the model places ten of them
- **When** the reader asks for the pass
- **Then** the screen shows the ten proposals grouped as plans, duplicates, single routes, filings and archives, each with its reason

### Requirement: Low-confidence proposals start unaccepted

```meta
type: requirement
status: draft
```

The system SHALL start a proposal whose confidence is below 0.6 unaccepted and every other proposal accepted.

#### Scenario: A doubtful plan

- **Given** the pass proposes one plan at confidence 0.9 and another at 0.4
- **When** the review screen opens
- **Then** the first plan reads Accepted and the second reads Accept

### Requirement: Nothing changes until Apply

```meta
type: requirement
status: draft
```

The system SHALL change no item, task or list until the reader presses Apply, and SHALL then take every accepted proposal and no other.

#### Scenario: Discarding

- **Given** the review screen shows ten accepted proposals
- **When** the reader presses Discard
- **Then** every item is as it was before the pass

#### Scenario: Applying

- **Given** ten proposals are shown and the reader turns two of them off
- **When** they press Apply
- **Then** the eight accepted proposals are taken and the two others' items are unchanged

### Requirement: Items the model could not place go back to triage

```meta
type: requirement
status: draft
```

The system SHALL leave every item the model could not place unprocessed and offer it to triage from the review screen.

#### Scenario: Two left over

- **Given** the pass placed ten of twelve items
- **When** the reader looks at the review screen
- **Then** it says two items are left for them, and Triage them opens triage on those two

## Capture attachments

```meta
type: requirements
status: draft
related: [.devbook/domain/inbox/features.md#capture-attachments]
```

> The requirements of bringing a capture's files to the desktop Inbox. The
> feature chapter says what the capability is and why it exists. This chapter
> says what it promises.

### Requirement: A capture lands whatever happens to its files

```meta
type: requirement
status: draft
```

The desktop SHALL receive a capture into the Inbox even when one of its files
cannot be downloaded or kept, with that file's reason recorded on it.

#### Scenario: The download fails

- **Given** a capture from the phone that names one file
- **When** the desktop syncs and the file cannot be downloaded
- **Then** the item is in the Inbox with the file listed and the reason it failed

#### Scenario: The file cannot be written on this machine

- **Given** a capture that names one file, and a download that succeeds
- **When** the file cannot be saved into the item's attachment folder
- **Then** the item is in the Inbox and the file says this machine could not keep it

### Requirement: The item is saved before any file is downloaded

```meta
type: requirement
status: draft
```

The desktop SHALL record the files a capture names on its item, and save the
item, before it downloads any of them.

#### Scenario: A file is still on its way

- **Given** a capture that names a file
- **When** the item appears in the Inbox before the file has been downloaded
- **Then** the file is listed on the item as *Waiting to download*

### Requirement: A file is kept only when it matches what was sent

```meta
type: requirement
status: draft
```

The desktop SHALL keep a downloaded file only when its bytes match the sha256
the capture recorded for it.

#### Scenario: The bytes do not match

- **Given** a capture naming a file with a recorded sha256
- **When** the bytes the desktop downloads have a different sha256
- **Then** no file is written and the file is marked failed with its reason

### Requirement: A file no fetch could honour is left off the item

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.AttachmentIntakeTests.A_named_file_no_fetch_could_honour_is_dropped_not_thrown
```

The desktop SHALL receive a capture that names a file with no id or a malformed
digest, leaving that file off the item instead of refusing the capture.

#### Scenario: One good file and one malformed one

- **Given** a capture naming a photo and a second file whose sha256 is not a digest
- **When** the desktop syncs
- **Then** the item is in the Inbox with the photo as its only file

### Requirement: A replayed sync page downloads nothing twice

```meta
type: requirement
status: draft
```

When the same capture arrives again, the desktop SHALL download only the files
that have never been tried, while recording any file the item did not have yet.

#### Scenario: The same page arrives twice

- **Given** an item whose files were all downloaded
- **When** the same capture arrives again
- **Then** nothing is downloaded and nothing is written

#### Scenario: A failed file is left for the person

- **Given** an item with one failed file
- **When** the same capture arrives again, now naming one more file
- **Then** the new file is recorded and the failed file is not tried again

#### Scenario: The file is already on disk

- **Given** a file already in the item's attachment folder with the recorded sha256
- **When** the capture is received
- **Then** the file is marked downloaded without being downloaded again

### Requirement: A person can retry a failed file

```meta
type: requirement
status: draft
```

The Inbox SHALL download a failed file again when the person presses Retry on
it, and show either the downloaded file or the new reason.

#### Scenario: The retry succeeds

- **Given** an item showing a failed file with its reason and Retry
- **When** the person presses Retry and the download succeeds
- **Then** the file is shown as downloaded and its reason is gone

#### Scenario: The retry fails again

- **Given** an item showing a failed file
- **When** the person presses Retry and the download fails again
- **Then** the file shows the new reason and still offers Retry

### Requirement: The detail view shows every file an item arrived with

```meta
type: requirement
status: draft
```

The Inbox detail view SHALL show every file an item arrived with: pictures on
this machine of 8 MB or less as thumbnails that open the file, and every other
file, a larger picture included, as a row with its size and Open.

#### Scenario: A picture and a document

- **Given** an item whose capture brought a photo and a PDF, both downloaded
- **When** the person opens the item
- **Then** the photo shows as a thumbnail above the body and the PDF as a row with its size and Open

#### Scenario: A picture not on this machine

- **Given** an item with a picture that has not been downloaded
- **When** the person opens the item
- **Then** the picture shows as a row saying it is waiting, not as a thumbnail

#### Scenario: A picture over 8 MB

- **Given** an item with a downloaded photo larger than 8 MB
- **When** the person opens the item
- **Then** the photo shows as a file row with its size and Open, not as a thumbnail

### Requirement: An item's kind follows its files

```meta
type: requirement
status: draft
```

The desktop SHALL mark an item as an `image` when its only content is
pictures, and as a `document` when any of its files is not a picture.

#### Scenario: Only a photo

- **Given** a capture with a photo and no text or link
- **When** it lands in the Inbox
- **Then** the item's kind is `image`

#### Scenario: A PDF

- **Given** a capture with a PDF attached
- **When** it lands in the Inbox
- **Then** the item's kind is `document`

### Requirement: Routed tasks carry the item's files

```meta
type: requirement
status: draft
```

Routing an item with attachments to Tasks SHALL give every task it creates the
item's attachment folder as that task's attachment.

#### Scenario: One repository

- **Given** an item with a downloaded file, assigned to one repository
- **When** the person routes it to Tasks
- **Then** the new task's attachment is the item's attachment folder

#### Scenario: Create plan

- **Given** an item with a downloaded picture and a downloaded PDF
- **When** the person creates a plan from it
- **Then** every task the plan creates has the item's attachment folder as its attachment

#### Scenario: No files

- **Given** an item with no attachments
- **When** the person routes it to Tasks
- **Then** the new task has no attachment

## Delete

```meta
type: requirements
status: draft
related: [.devbook/domain/inbox/features.md#delete]
```

> The requirements of deleting an Inbox Item. The feature chapter says how it
> differs from Archive; this says what it promises.

### Requirement: Delete removes the item after asking

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Delete_asks_first_and_cancelling_keeps_the_item, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Confirming_delete_removes_the_item_and_clears_the_detail, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.An_archived_item_can_still_be_deleted]
```

The system SHALL offer Delete on an item in any status, ask for confirmation first, and on confirmation remove the item from every slice so it cannot be found again.

#### Scenario: Cancelling

- **Given** an item is shown in the detail
- **When** the reader presses Delete and then Cancel
- **Then** the item is still in the queue

#### Scenario: Confirming

- **Given** an item is shown in the detail
- **When** the reader presses Delete and confirms
- **Then** the item is gone from the queue and the detail shows no item

#### Scenario: Clearing the archive

- **Given** an archived item is shown in the detail
- **When** the reader looks at its acts
- **Then** Archive is not offered and Delete is

### Requirement: The phone hears about a deleted capture

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.DeleteItemTests.Deleting_an_open_item_from_the_phone_leaves_its_acknowledgement_in_the_outbox, unit:dotnet:Backlog.Modules.Inbox.UnitTests.DeleteItemTests.Once_the_outbox_sends_it_the_deleted_capture_is_forgotten, unit:dotnet:Backlog.Modules.Inbox.UnitTests.DeleteItemTests.Deleting_a_phone_item_the_phone_already_heard_about_owes_it_nothing]
```

Deleting an item that arrived through sync SHALL leave the acknowledgement archiving would, whenever the phone may still be offering the capture, and SHALL keep nothing once that acknowledgement has been sent.

#### Scenario: An open item from the phone

- **Given** an unprocessed item that arrived through sync
- **When** the reader deletes it
- **Then** the outbox holds one acknowledgement for its capture, stamped with the moment it was deleted

#### Scenario: Already acknowledged

- **Given** an archived item from the phone whose acknowledgement has been sent
- **When** the reader deletes it
- **Then** nothing of it is kept

### Requirement: A deleted capture does not come back

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.DeleteItemTests.A_replay_of_a_deleted_capture_does_not_bring_it_back, unit:dotnet:Backlog.Modules.Inbox.UnitTests.DeleteItemTests.The_phone_withdrawing_a_deleted_capture_forgets_its_acknowledgement]
```

The system SHALL treat a capture it deleted and has not yet acknowledged as already known when the replica sends it again, and SHALL forget the acknowledgement when the replica withdraws the capture itself.

#### Scenario: A replayed page

- **Given** a capture from the phone was deleted before its acknowledgement was pushed
- **When** the next pull carries the capture again
- **Then** no item is created

#### Scenario: The phone triaged it too

- **Given** a capture from the phone was deleted before its acknowledgement was pushed
- **When** the next pull carries the capture's tombstone
- **Then** the acknowledgement is dropped unsent

## Queue health

```meta
type: requirements
status: draft
related: [.devbook/domain/inbox/features.md#queue-health-bar]
```

> The requirements of the Inbox's queue health bar. What Monitoring shows of
> the same numbers is Monitoring's to promise.

### Requirement: The bar splits the whole unprocessed queue by age

```meta
type: requirement
status: draft
```

The system SHALL show in the side menu the number of unprocessed items in every list and a bar in three parts counting those captured under 3 days ago, from 3 days up to the fourteen-day stale threshold, and over it with the oldest one's age — leaving deferred, routed and archived items out.

#### Scenario: A mixed queue

- **Given** four unprocessed items captured two hours, five days, fifteen days and twenty days ago, one of them filed in a list, and a deferred and an archived item older than all four
- **When** the pane opens
- **Then** the side menu reads "4 open", "1 under 3 days", "1 from 3 to 14 days" and "2 over 14 days — oldest 20 days"

#### Scenario: Nothing waiting

- **Given** no unprocessed items
- **When** the pane opens
- **Then** the side menu reads "Nothing waiting" and shows no bar

## Classification and enrichment

```meta
type: requirements
status: draft
related: [.devbook/domain/inbox/features.md#classification-and-enrichment]
```

> What the suggestion row promises the reader triaging an item.

### Requirement: Nothing is applied until the reader takes it

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.An_open_items_suggestions_are_numbered_chips_and_nothing_is_applied_until_one_is_taken
```

The system SHALL show every suggestion for an open item as a chip, and change the item only when the reader takes one.

#### Scenario: Suggestions on screen

- **Given** an open item for which a tag, a repository and a destination are suggested
- **When** the reader opens it
- **Then** three numbered chips show, and the item has no new tag, no repository and is still unprocessed

### Requirement: One key takes a suggestion

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.A_digit_on_the_detail_or_on_the_rows_takes_the_chip_with_that_number, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Pressing_a_chip_adds_its_tag_and_the_chip_leaves_the_row, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Taking_the_backlog_suggestion_routes_the_item_and_taking_archive_archives_it]
```

The system SHALL take the suggestion whose chip shows a digit when the reader presses that digit in the list or the detail, and apply it through the act the chip names.

#### Scenario: Taking a repository

- **Given** an open item whose second chip suggests a repository
- **When** the reader presses 2
- **Then** the item is assigned that repository and the chip leaves the row

#### Scenario: Taking the backlog

- **Given** an open item whose chip suggests moving it to the backlog
- **When** the reader presses that chip's digit
- **Then** the item is routed to the backlog and no suggestions remain

### Requirement: Typing is never taken as a suggestion

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.A_digit_typed_into_a_picker_or_with_a_modifier_held_takes_nothing
```

The system SHALL leave every suggestion alone when a digit is typed into a field or pressed with a modifier held.

#### Scenario: A digit in the tag picker

- **Given** an open item with a suggestion numbered 1
- **When** the reader types 1 into the tag picker
- **Then** the suggestion is still offered and the item is unchanged

### Requirement: A turned-down suggestion does not come back

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.A_dismissed_suggestion_is_recorded_and_does_not_come_back_after_a_reload, unit:dotnet:Backlog.Infrastructure.Sqlite.UnitTests.SqliteInboxRepositoryTests.The_suggestions_a_reader_turned_down_come_back_with_the_item]
```

The system SHALL record a suggestion the reader turns down on the item and never offer it for that item again.

#### Scenario: After a reload

- **Given** an open item with a tag suggestion and a destination suggestion
- **When** the reader turns the tag down and the inbox reloads
- **Then** only the destination is offered, now numbered 1

### Requirement: A suggestion that cannot be taken says why

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.A_knowledge_suggestion_says_why_it_cannot_be_taken_has_no_number_and_can_still_be_dismissed
```

The system SHALL show a suggestion to keep an item as knowledge without a number, with the reason it cannot be taken, and let the reader turn it down.

#### Scenario: Collected material

- **Given** an open article with a knowledge suggestion
- **When** the reader presses 1
- **Then** nothing happens, and the chip still offers its ×

### Requirement: Routing rules are kept only when every line reads

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsInboxRoutingRulesTests, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.InboxRoutingRulesStoreTests]
```

The system SHALL keep the routing rules typed on the Repositories page only when every line reads as `pattern => owner/repo`, and otherwise name the line and keep the rules in use.

#### Scenario: A line without an arrow

- **Given** one rule in use
- **When** the reader adds a second line with no `=>`
- **Then** the page names line 2 and the one rule stays in use

## Route to Tasks

```meta
type: requirements
status: draft
related: [.devbook/domain/inbox/features.md#route-to-tasks]
```

> The requirements of moving one item to the backlog.

### Requirement: Sibling tasks name each other and share a tag

```meta
type: requirement
status: draft
```

The system SHALL make one task per repository for an item assigned to several, give each task the body line "Same capture in: <repository> — <title>" for every other task it made, and tag all of them with `#from-inbox-` followed by the last eight hex digits of the item's id.

#### Scenario: Two repositories

- **Given** an item whose id ends in `9c41d07e`, assigned to Backlog and backlog-sync
- **When** it is moved to the backlog
- **Then** two tasks are made, each carrying a "Same capture in:" line naming the other's repository and title, and both are tagged `#from-inbox-9c41d07e`

#### Scenario: One repository

- **Given** an item assigned to one repository
- **When** it is moved to the backlog
- **Then** one task is made, with no "Same capture in:" line and no `#from-inbox-` tag

## Route a batch to Tasks

```meta
type: requirements
status: draft
related: [.devbook/domain/inbox/features.md#route-a-batch-to-tasks, .devbook/domain/inbox/domain.md#batch]
```

> The requirements of routing several Inbox Items to Tasks as one plan import.

### Requirement: A batch is one plan

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.RouteBatchToBacklogTests.A_selection_routes_as_one_inbox_batch_and_each_item_keeps_only_its_own_entries, unit:dotnet:Backlog.Modules.Inbox.UnitTests.RouteBatchToBacklogTests.A_list_route_is_tagged_with_the_list_name_and_leaves_the_list_standing, unit:dotnet:Backlog.Modules.Inbox.UnitTests.RouteBatchToBacklogTests.Every_batch_is_a_new_plan, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.The_list_confirm_says_the_deferred_items_stay_when_there_are_any, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.InboxBacklogTargetTests.A_batch_is_one_document_whose_entries_parse_back_with_the_tag_the_id_and_the_repository]
```

The system SHALL route a batch through one plan import whose entries all carry a plan tag new to that batch: `+inbox-batch-` and eight hex digits for a selection, and the list's name with eight hex digits for a list.

#### Scenario: Moving a selection

- **Given** three open items are picked
- **When** the reader chooses Move to backlog
- **Then** one import creates their entries, all tagged `+inbox-batch-` and the same eight hex digits

#### Scenario: Moving a list

- **Given** the list "Reading list" holds two open items
- **When** the reader confirms Move list to backlog
- **Then** both items' entries are tagged `+reading-list-` and eight hex digits, and the list is still there

#### Scenario: A list with deferred items

- **Given** the list "Reading list" holds two open items and one deferred item
- **When** the reader opens Move list to backlog
- **Then** the confirmation says its 1 deferred item stays with the list, and confirming routes only the two open items

### Requirement: Each item keeps its own routing

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.RouteBatchToBacklogTests.Each_item_goes_with_its_own_facts_and_repositories, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.InboxBacklogTargetTests.Each_item_gets_back_only_its_own_entries_in_repository_order, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.InboxBacklogTargetTests.A_batch_names_each_entrys_own_item_as_its_source_and_nothing_else, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.InboxBacklogTargetTests.An_item_the_import_answered_for_only_in_part_is_named_with_what_was_made_and_the_rest_are_routed, unit:dotnet:Backlog.Modules.Inbox.UnitTests.RouteBatchToBacklogTests.A_save_that_fails_after_the_import_names_the_entries_already_made_and_keeps_the_rest]
```

The system SHALL create, for each item of a batch, the draft entries Route to Tasks would create for it alone — one per repository, each naming that item as its source — and record on each item only the entries it became.

#### Scenario: Two repositories on one item

- **Given** a batch of two items, one assigned to two repositories
- **When** the batch is routed
- **Then** that item records its two entries and the other item records its one

### Requirement: A refused batch routes nothing

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.RouteBatchToBacklogTests.A_refusal_from_tasks_routes_nothing_and_says_the_whole_batch_was_refused, unit:dotnet:Backlog.Modules.Inbox.UnitTests.RouteBatchToBacklogTests.A_refusal_from_tasks_is_wrapped_only_for_the_items_that_were_sent, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.A_batch_tasks_refused_moves_nothing_and_the_toast_says_so_once, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.InboxBacklogTargetTests.A_refusal_from_tasks_comes_back_as_tasks_gave_it_beside_the_items_left_out]
```

The system SHALL leave every item of a batch unrouted when the import refuses the batch, and say that the whole batch was refused and every item is still in the Inbox.

#### Scenario: Tasks refuses the plan

- **Given** three picked items
- **When** the import refuses the document
- **Then** all three are still open in the queue and the message says nothing was routed

### Requirement: A decided item is left out and named

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.RouteBatchToBacklogTests.Routed_archived_and_missing_items_are_refused_up_front_and_the_rest_still_go, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.A_decided_item_in_the_selection_is_named_and_never_sent_and_the_rest_still_go]
```

The system SHALL leave an already routed or archived item out of a batch before the import is asked, name it, and route the rest.

#### Scenario: One routed item among three

- **Given** three picked items, one already routed
- **When** the reader chooses Move to backlog
- **Then** the other two are routed and the routed one is named as refused

### Requirement: An item the batch cannot carry is left out and named

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.InboxBacklogTargetTests.Notes_that_would_split_or_merge_the_document_leave_only_that_item_out, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.InboxBacklogTargetTests.With_every_item_left_out_tasks_is_not_asked, unit:dotnet:Backlog.Modules.Inbox.UnitTests.RouteBatchToBacklogTests.An_item_the_target_leaves_out_is_named_with_its_own_reason_and_the_rest_still_go, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.An_item_left_out_of_the_batch_is_named_like_any_refusal_and_the_rest_still_go]
```

The system SHALL leave out of a batch, and name with its reason, an item whose notes carry a top-level heading or an unclosed code fence, and route the rest.

#### Scenario: A clipped article with its own heading

- **Given** three picked items, one whose notes open with `# Summary`
- **When** the reader chooses Move to backlog
- **Then** the other two are routed and the third is named with the reason it was left out

### Requirement: A batch never registers a repository

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.InboxBacklogTargetTests.An_item_naming_a_repository_the_workspace_does_not_know_is_left_out_and_never_registered, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.InboxBacklogTargetTests.A_known_repository_is_known_whatever_its_case_or_spacing]
```

The system SHALL leave out of a batch, and name, an item assigned to a repository the workspace does not know, and never register that repository.

#### Scenario: A repository removed since the item was assigned

- **Given** two picked items, one assigned to a repository no longer in the workspace
- **When** the batch is routed
- **Then** the other item is routed, the first is named, and the repository is still not in the workspace

### Requirement: One route at a time

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.A_batch_in_flight_holds_the_single_route, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.A_single_route_in_flight_holds_the_batch_route]
```

The system SHALL not start a single route while a batch route is running, nor a batch route while a single route is running.

#### Scenario: Moving the shown item during a batch

- **Given** a batch is being routed
- **When** the reader presses Move to backlog on the item the detail shows
- **Then** nothing more is routed and the button stays disabled until the batch finishes

### Requirement: Tasks is refreshed once per batch

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Move_to_backlog_across_the_selection_routes_it_as_one_batch_and_refreshes_tasks_once, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeInboxWiringTests]
```

The system SHALL refresh the Tasks pane once after a batch is routed, however many items it held.

#### Scenario: Five items routed

- **Given** five picked items
- **When** they are routed as a batch
- **Then** the Tasks pane reloads once and shows all their entries

## Order a batch with the drafter

```meta
type: requirements
status: draft
related: [.devbook/domain/inbox/features.md#order-a-batch-with-the-drafter, .devbook/domain/inbox/domain.md#dependency-tier]
```

> The requirements of asking the plan drafter which item of a batch waits on which.

### Requirement: The drafter is asked only on the press

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Asking_the_AI_adds_its_order_as_switches_marked_inferred_and_confirm_routes_them, unit:dotnet:Backlog.Modules.Inbox.UnitTests.InferBatchOrderTests.The_batch_is_carried_in_the_draft_request_with_each_item_and_the_bare_tag, unit:dotnet:Backlog.Modules.Inbox.UnitTests.InferBatchOrderTests.One_routable_item_has_nothing_to_order_and_the_drafter_is_not_asked, unit:dotnet:Backlog.Modules.Inbox.UnitTests.InferBatchOrderTests.Create_plan_still_asks_about_one_item_with_no_batch]
```

The system SHALL make no model call about a batch until the reader presses Ask the AI to order, and one call per press.

#### Scenario: Opening the panel

- **Given** two picked items and a configured drafter
- **When** the reader routes them and the panel opens
- **Then** the drafter has not been asked, and only the stated dependencies are shown

#### Scenario: Asking

- **Given** the panel is open for two items
- **When** the reader presses Ask the AI to order
- **Then** the drafter is asked once, with both items and the batch's plan tag

### Requirement: Without a drafter the ask says why it cannot run

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Ask_the_AI_to_order_is_shown_disabled_with_its_reason_when_no_drafter_is_configured, unit:dotnet:Backlog.Modules.Inbox.UnitTests.InferBatchOrderTests.Without_a_drafter_the_order_is_not_configured_and_nothing_is_read, unit:dotnet:Backlog.Modules.Inbox.UnitTests.InferBatchOrderTests.A_drafter_that_is_not_available_gives_its_own_reason]
```

The system SHALL show Ask the AI to order disabled, titled with the drafter's reason, when no drafter is available.

#### Scenario: No drafter configured

- **Given** no plan drafter is configured
- **When** the reader opens the panel for two items
- **Then** Ask the AI to order is shown, disabled, and its title gives the drafter's reason

### Requirement: An answer reaching outside the batch is refused whole

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.InboxBacklogTargetTests.A_drafted_order_is_read_as_edges_between_the_items_and_tasks_is_not_asked, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.InboxBacklogTargetTests.A_drafted_order_naming_a_repository_outside_the_batch_is_refused_whole, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.InboxBacklogTargetTests.A_drafted_order_naming_an_entry_outside_the_batch_is_refused_whole, unit:dotnet:Backlog.Modules.Inbox.UnitTests.InferBatchOrderTests.The_panels_repositories_are_the_ones_the_answer_may_name, unit:dotnet:Backlog.Modules.Inbox.UnitTests.InferBatchOrderTests.An_answer_the_target_refuses_adds_nothing, unit:dotnet:Backlog.Modules.Inbox.UnitTests.InferBatchOrderTests.A_drafter_failure_comes_back_as_it_is, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.A_refused_order_is_named_in_the_panel_and_adds_no_switch]
```

The system SHALL offer none of the drafter's order when its answer names a repository none of the batch's items goes to, or an entry or an `after:` that is not one of the batch's items, and say why in the panel.

#### Scenario: A made-up repository

- **Given** a batch of two items routed to `acme/api`
- **When** the drafter's answer names `acme/web`
- **Then** no dependency is added and the panel says the order names a repository outside this batch

#### Scenario: A made-up item

- **Given** a batch of two items
- **When** the drafter's answer puts one after an id that is not in the batch
- **Then** no dependency is added and the panel says the order names an entry that is not in this batch

#### Scenario: Nothing is imported

- **Given** a batch of two items
- **When** the drafter's answer is read
- **Then** Tasks is not asked and no entry exists until the reader confirms

### Requirement: Inferred dependencies are proposals a person can turn off

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxRouteDraftTests.The_drafters_dependencies_are_added_on_after_the_stated_ones_and_never_twice, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxRouteDraftTests.An_ask_that_adds_nothing_or_is_refused_says_so_and_confirm_waits_while_it_is_out, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Asking_the_AI_adds_its_order_as_switches_marked_inferred_and_confirm_routes_them, unit:dotnet:Backlog.Modules.Inbox.UnitTests.InferBatchOrderTests.Each_edge_read_back_is_an_inferred_dependency_on_the_other_item]
```

The system SHALL add each dependency the drafter infers between two items of the batch as a switch marked AI-inferred and on, never add one the panel already shows between the same two items, and route only the switches left on.

#### Scenario: A new dependency

- **Given** the panel shows no dependency between A and B
- **When** the drafter puts B after A
- **Then** a switch marked AI-inferred puts B after A, on, and Confirm routes B with `after:` A

#### Scenario: One the item already states

- **Given** B's notes link to A's source, so the panel already puts B after A
- **When** the drafter also puts B after A
- **Then** the panel still shows one switch for that pair, the stated one

#### Scenario: Turned off

- **Given** an AI-inferred switch putting B after A
- **When** the reader turns it off and confirms
- **Then** B is routed with no `after:` A

#### Scenario: While the ask is out

- **Given** the reader has pressed Ask the AI to order
- **When** the drafter has not answered yet
- **Then** Confirm is disabled until it does
