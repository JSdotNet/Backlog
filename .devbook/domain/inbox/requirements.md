# Requirements

```meta
type: requirements
status: draft
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
