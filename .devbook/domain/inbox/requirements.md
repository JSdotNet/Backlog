# Requirements

```meta
type: requirements
status: draft
related: [.devbook/domain/inbox/features.md]
```

> What this context's features guarantee, one chapter per feature. Each
> requirement is one SHALL sentence with the scenarios that prove it.

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
this machine as thumbnails that open the file, and every other file as a row
with its size and Open.

#### Scenario: A picture and a document

- **Given** an item whose capture brought a photo and a PDF, both downloaded
- **When** the person opens the item
- **Then** the photo shows as a thumbnail above the body and the PDF as a row with its size and Open

#### Scenario: A picture not on this machine

- **Given** an item with a picture that has not been downloaded
- **When** the person opens the item
- **Then** the picture shows as a row saying it is waiting, not as a thumbnail

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

#### Scenario: No files

- **Given** an item with no attachments
- **When** the person routes it to Tasks
- **Then** the new task has no attachment

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
