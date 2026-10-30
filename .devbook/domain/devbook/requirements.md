# Requirements

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md]
```

> What this context's features guarantee, one chapter per feature. Each
> requirement is one SHALL sentence with the scenarios that prove it. The Brain's
> own requirements are the [Brain's](../brain/requirements.md).

## Unified search

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#unified-search]
```

> The requirements of one search across the Brain and every configured
> repository's devbook.

### Requirement: Results are grouped by source, Brain first

```meta
type: requirement
status: draft
```

The system SHALL group search results by the devbook they came from and put the Brain's group first.

#### Scenario: Everywhere

- **Given** a word that occurs in a Brain note and in a chapter of `backlog`
- **When** the reader searches for it with Everywhere
- **Then** the Brain group comes first and the `backlog` group after it

### Requirement: A scope chip narrows to one source

```meta
type: requirement
status: draft
```

The system SHALL search only the Brain under the Brain chip and only one repository's devbook under that repository's chip.

#### Scenario: Brain only

- **Given** a word that occurs in a Brain note and in a chapter of `backlog`
- **When** the reader searches for it with the Brain chip
- **Then** only the Brain note is listed

### Requirement: A source with no database is named

```meta
type: requirement
status: draft
```

The system SHALL name a source whose database has not been built as not searchable, beside the groups of the sources that answered, rather than leave it out.

#### Scenario: A repository with no index

- **Given** a configured repository whose devbook database has not been built
- **When** the reader searches with Everywhere
- **Then** that repository is listed as not searchable yet, with the reason, after the groups that answered

### Requirement: A result opens in its own devbook

```meta
type: requirement
status: draft
```

The system SHALL open a result in the devbook it came from, moving the pane to that devbook when it shows another, and SHALL leave the header's scope and the task filter unchanged.

#### Scenario: A repository chapter found on the Brain

- **Given** the pane shows the Brain and a search lists a chapter of `backlog`
- **When** the reader opens that result
- **Then** the pane shows the `backlog` devbook on that chapter, and the header's scope is unchanged

## Rendered devbook documents

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#rendered-devbook-documents]
```

> The requirements of how a chapter's metadata is shown and written: the decision
> state a domain chapter carries, and the state another tool keeps on it.

### Requirement: Decision state is shown beside the status

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainDevbookContractSixteenPanelTests.The_nine_state_fields_are_never_rows_in_the_strip, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainDevbookContractSixteenPanelTests.Approval_and_acceptance_are_drawn_beside_the_status_as_who_and_when, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPanelsViewerSwitchesTests.Status_is_not_shown_while_devbook_status_is_off, unit:dotnet:Backlog.UI.Components.UnitTests.MetadataStatusHiddenTests.Findings_about_the_status_and_its_sign_off_go_with_it]
```

While [Devbook status](context.md#devbook-status) is on, the system SHALL show a domain chapter's decision and review state as one line beside its status, saying who decided and when, and never as fields among the chapter's metadata.

#### Scenario: An accepted chapter

- **Given** Devbook status is on, and a domain chapter approved by `jobsc` on 2026-09-02 and accepted by `ann` on 2026-09-20
- **When** the reader opens it in the domain panel
- **Then** the bar beside its status reads *approved by jobsc · 2026-09-02; accepted by ann · 2026-09-20*, and none of the approval or acceptance fields is listed with the chapter's metadata

#### Scenario: Status switched off

- **Given** Devbook status is off, and the same accepted domain chapter
- **When** the reader opens it in the domain panel
- **Then** neither its status nor the decision line is drawn, and the approval and acceptance fields are still not listed with its metadata

### Requirement: Leaving a decision step takes its record with it

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookStatusWriteRulesTests.Moving_from_accepted_to_approved_deletes_the_acceptance_record_and_keeps_the_approval, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookStatusWriteRulesTests.Leaving_the_rungs_for_an_ordinary_status_deletes_all_six_record_fields, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookStatusWriteRulesTests.Leaving_the_rungs_for_the_resting_value_deletes_the_status_and_all_six_record_fields, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookStatusWriteRulesTests.Removing_the_status_of_an_approved_chapter_deletes_the_approval_record_with_it]
```

The system SHALL delete a domain chapter's approval or acceptance record in the same write that moves the chapter off that step.

#### Scenario: Back from accepted to approved

- **Given** a domain chapter that is accepted, with its approval and acceptance records
- **When** the reader sets its status to approved
- **Then** the chapter keeps its approval record and no longer carries an acceptance record

#### Scenario: Back to draft

- **Given** a domain chapter that is accepted, with its approval and acceptance records
- **When** the reader sets its status to draft
- **Then** the chapter carries neither record

### Requirement: Another tool's state is carried as written

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.MetadataReaderContractTests.An_extension_keeps_its_authors_casing_and_its_value_verbatim, unit:dotnet:Backlog.UI.Components.UnitTests.DevbookMetadataFindingsTests.Extension_keys_are_never_validated, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainDevbookContractSixteenPanelTests.An_extension_key_stays_a_row_and_is_marked_as_one]
```

The system SHALL show every `ext.` key of a chapter's metadata with its key and value exactly as written, and report no finding about it.

#### Scenario: A plugin's own key

- **Given** a chapter whose metadata holds `ext.archify.layer: core`
- **When** the reader opens it
- **Then** the chapter lists `ext.archify.layer` with the value `core`, marked as another tool's, and no finding is reported about it

## Remarks on a chapter

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#remarks-on-a-chapter]
```

> The requirements of where the read view shows a reader's remark once the chapter
> it was left on has changed.

### Requirement: A remark follows its passage

```meta
type: requirement
status: draft
related: [.devbook/domain/devbook/domain.md#remark]
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.MarkdownCommentAnchorTests.A_remark_follows_its_paragraph_when_the_chapter_grows_above_it, unit:dotnet:Backlog.UI.Components.UnitTests.MarkdownCommentAnchorTests.A_remark_follows_its_paragraph_when_the_chapter_shrinks_above_it, unit:dotnet:Backlog.UI.Components.UnitTests.MarkdownCommentAnchorTests.A_remark_survives_its_paragraph_being_rewrapped, unit:dotnet:Backlog.UI.Components.UnitTests.MarkdownCommentAnchorTests.A_repeated_passage_takes_the_copy_nearest_the_remembered_index, unit:dotnet:Backlog.UI.Components.UnitTests.MarkdownCommentAnchorTests.A_remark_without_a_digest_is_anchored_by_index_exactly_as_before]
```

The read view SHALL show a remark beside the passage it was left on after the chapter has changed above that passage.

#### Scenario: Paragraphs are added above the remarked passage

- **Given** a remark on the second paragraph of a chapter
- **When** two paragraphs are added above it and the reader opens the chapter again
- **Then** the remark shows beside the same passage, now the fourth paragraph

### Requirement: A remark whose passage has gone shows at the end

```meta
type: requirement
status: draft
related: [.devbook/domain/devbook/domain.md#remark]
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.MarkdownCommentAnchorTests.A_remark_whose_paragraph_was_deleted_shows_at_the_end, unit:dotnet:Backlog.UI.Components.UnitTests.MarkdownCommentAnchorTests.A_remark_whose_paragraph_changed_wording_shows_at_the_end]
```

The read view SHALL show a remark whose passage has been deleted or reworded at the end of the chapter rather than drop it.

#### Scenario: The remarked paragraph is deleted

- **Given** a remark on a paragraph of a chapter
- **When** that paragraph is deleted and the reader opens the chapter again
- **Then** the remark shows at the end of the chapter

## Review notes in a chapter

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#review-notes-in-a-chapter]
```

> The requirements of the review notes a repository keeps in its chapters as
> `annotation` fences.

### Requirement: A review note never travels as the chapter's content

```meta
type: requirement
status: draft
related: [.devbook/domain/devbook/domain.md#review-note]
tests: [unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.DevbookToolsTests.An_ordinary_read_leaves_out_only_the_meta_and_annotation_fences, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.PrivateFenceTests.An_annotation_fence_quoting_a_code_sample_is_cut_whole]
```

The system SHALL leave a chapter's review notes out of everything it hands on as the chapter's content: an assistant's ordinary read, the text it searches, and an excerpt.

#### Scenario: An assistant reads a chapter with an open question

- **Given** a chapter holding an open review note beside one of its paragraphs
- **When** an assistant reads the chapter without asking for its review state
- **Then** the answer holds the chapter's prose, diagrams and code listings, and no review note

## Sync verdicts beside a chapter

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#sync-verdicts-beside-a-chapter]
```

> The requirements of the [Sync Verdict](domain.md#sync-verdict) a chapter shows.

### Requirement: A sweep replaces only the verdicts of the chapters it checked

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DevbookSyncVerdictStoreTests.A_later_run_replaces_the_chapters_it_names_and_leaves_the_rest]
```

The system SHALL show beside a chapter the verdict of the latest sweep that checked it, and keep every other chapter's verdict when a sweep checks only some.

#### Scenario: A later sweep checks one of two chapters

- **Given** one sweep found `run-start` code-ahead and `run-finish` in conflict
- **When** a later sweep finds `run-start` aligned and says nothing about `run-finish`
- **Then** `run-start` shows aligned and `run-finish` still shows in conflict

### Requirement: A verdict keeps the sweep's word

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSyncVerdictBadgeTests.A_word_outside_the_sweeps_vocabulary_keeps_its_spelling_and_wears_the_unknown_tone]
```

The system SHALL show a verdict in the sweep's own word, and show a word outside the sweep's vocabulary as written, flagged as unknown.

#### Scenario: A word the app does not know

- **Given** a sweep recorded the verdict `drifting` for a chapter
- **When** the reader opens the chapter
- **Then** its badge reads `drifting` in the unknown tone, not the tone of a known verdict

## Choosing where the devbook is read from

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#choosing-where-the-devbook-is-read-from]
```

> The requirements of the Devbook pane's source field: the local clone or a branch,
> read from GitHub (local ADR 0008).

### Requirement: Typing narrows the sources

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.A_query_keeps_the_suggestions_containing_it_whatever_the_case, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.A_blank_query_is_no_filter, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.The_load_entry_survives_any_query_until_the_branches_are_in, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Typing_narrows_the_list_to_the_branches_that_match, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.A_click_in_the_untouched_field_lists_every_source]
```

The system SHALL suggest, in the source field, the sources whose names contain the typed text in any case, and every source while the field is blank or untouched.

#### Scenario: Narrowing to a branch

- **Given** the branches of `backlog` are loaded, among them `claude/devbook-branch-search` and `main`
- **When** the reader types `BRANCH-search` into the source field
- **Then** `claude/devbook-branch-search` is suggested and `main` is not

### Requirement: Branches load once per session, in the background

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchOverviewTests.Opening_the_pane_loads_once_and_the_picker_and_tabs_ask_nothing_more, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchOverviewTests.The_list_says_the_branches_are_loading_while_the_first_load_runs, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchOverviewTests.While_a_refresh_runs_the_field_keeps_naming_the_source_read, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.The_first_load_lists_compares_every_branch_and_checks_the_clone, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_second_load_answers_from_the_first_and_asks_nothing, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.Loads_asked_for_at_once_share_one, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.One_caller_giving_up_does_not_cancel_the_shared_load, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_refresh_loads_again, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_load_that_runs_out_of_time_stops_the_comparisons_it_started, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.The_load_entry_becomes_a_refresh_once_the_overview_is_in, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.No_load_entry_is_offered_while_the_overview_loads, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.Loading_branches_refreshes_the_overview, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.While_the_overview_loads_the_suggestions_say_so_and_the_entry_names_nothing, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.Loading_the_branches_does_not_change_what_is_current, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsDevbookFolderStatusTests.After_a_refresh_the_source_select_is_rebuilt_on_the_source_read, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubBranchCatalogTests.Branches_past_the_first_page_are_listed, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubBranchCatalogTests.Listing_stops_at_the_ceiling, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubBranchCatalogTests.Branch_heads_are_paged_to_the_same_ceiling]
```

The system SHALL load a repository's branches, each compared with the default branch, and the local clone's currency once per app session in the background when the Devbook pane first opens on the repository, page by page up to 1000 branches, reuse that load for every later ask until the reader picks "Refresh branches", show "Loading branches…" in the list while a load runs, and never change the source being read or hold back the pane while it does.

#### Scenario: Reopening the picker asks nothing again

- **Given** the pane opened on `backlog` and its branches have loaded
- **When** the reader switches section tabs and opens the source field again
- **Then** GitHub is not asked for the branches again, and the field suggests what the first load found

#### Scenario: A repository with many branches

- **Given** `backlog` has 1200 branches on GitHub and the pane reads its clone
- **When** the pane opens on `backlog`
- **Then** the first 1000 branches are compared and the pane still reads the clone

#### Scenario: The first load is still running

- **Given** the pane has just opened on `backlog` and its branches have not loaded yet
- **When** the reader opens the source field
- **Then** the list shows "Loading branches…", and picking it changes nothing

### Requirement: Only branches that change the devbook are suggested

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchOverviewTests.Only_branches_changing_the_devbook_are_offered, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.Once_the_overview_is_in_only_branches_changing_the_devbook_are_listed, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.The_configured_branch_is_listed_even_when_it_changes_no_devbook, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.A_listed_branch_left_out_by_the_filter_still_means_itself, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_branch_changes_the_sections_its_files_are_under_in_tab_order, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_legacy_root_folder_counts_as_its_section, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_folder_pointed_elsewhere_counts_as_its_section, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.Code_and_instruction_files_change_no_section, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_file_renamed_out_of_a_section_changed_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_cut_short_file_list_with_no_devbook_file_may_change_it]
```

The system SHALL suggest, among the branches, only those whose diff against the default branch touches a file in a devbook area other than the instructions, on either side of a rename, or whose file list GitHub cut short before any devbook file showed, marked as one that may change the devbook; the configured branch is suggested regardless, and any branch the load listed can still be typed by name.

#### Scenario: A code-only branch

- **Given** `fix/build` changes only files under `src/` and `claude/branch-picker` changes `.devbook/domain/`
- **When** the reader opens the source field
- **Then** `claude/branch-picker` is suggested and `fix/build` is not, and typing `fix/build` and pressing Enter switches to it

### Requirement: Each branch says what it changes and how far it is from the default branch

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchOverviewTests.A_branch_row_shows_a_glyph_for_each_section_it_changes_and_its_counts, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.A_branch_row_carries_its_sections_and_counts, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.Nothing_loaded_means_no_row, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.Ahead_and_behind_come_from_the_comparison]
```

The system SHALL show, on each suggested branch, the glyph of every devbook area it changes in tab order with the area named in words for assistive technology, and its commits ahead of and behind the default branch, and show no counts on a row whose comparison nobody read.

#### Scenario: A branch changing two areas

- **Given** `claude/branch-picker` changes `.devbook/arc42/` and `.devbook/domain/`, is 3 commits ahead of `main` and 1 behind
- **When** the reader opens the source field
- **Then** its row shows the Architecture and Domain glyphs and `↑3 ↓1`, read out as "Changes Architecture", "Changes Domain", "3 ahead, 1 behind"

### Requirement: The clone says how far it is from its upstream

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchOverviewTests.The_clone_row_shows_its_counts_against_its_upstream, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.The_clone_row_carries_its_counts_against_its_upstream, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.A_clone_git_could_not_place_has_no_counts, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.A_clone_with_no_upstream_has_no_counts, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_repository_with_no_clone_is_not_checked_for_one, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.Recording_the_clone_replaces_its_answer_and_keeps_the_branches, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_clone_recorded_during_a_load_is_not_overwritten_by_that_loads_older_look, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchOverviewTests.Pulling_moves_the_clone_row_and_lists_no_branches_again]
```

The system SHALL show, on the local clone's row, its commits ahead of and behind the branch it tracks, follow the newest check or pull of the clone without listing the branches again, and show no counts when the clone tracks no upstream or git could not place it.

#### Scenario: A clone with no upstream

- **Given** the clone of `backlog` is on a branch that tracks nothing
- **When** the reader opens the source field
- **Then** the local clone row shows its name and no counts

### Requirement: A failed branch load leaves the known sources offered

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchOverviewTests.A_refused_load_says_why_and_the_picker_keeps_working, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.A_refused_overview_keeps_the_default_the_clone_and_the_configured_branch, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.A_refused_refresh_keeps_the_list_in, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_listing_that_fails_comes_back_as_a_message, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_comparison_that_fails_leaves_that_branch_out_and_nothing_else, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_failed_load_is_not_retried_by_loading_again, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_clone_check_that_fails_leaves_the_clone_unknown, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_refused_refresh_keeps_the_branches_the_last_load_listed, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.A_configured_branch_is_listed_even_when_nothing_was_loaded]
```

The system SHALL, when the branch listing is refused, say why beside the source field and keep offering the default branch, the local clone and the configured branch; leave out only a branch GitHub would not compare; not ask again until the reader refreshes; and, when a refresh is refused, keep the branches the last good load listed.

#### Scenario: Opening the pane offline

- **Given** the machine is offline and the pane has not loaded the branches of `backlog` this session
- **When** the pane opens on `backlog`
- **Then** the field says why the branches could not be read, still offers `main`, the local clone and the configured branch, and reopening the pane does not ask GitHub again

### Requirement: The version control is offered only for the clone

```meta
type: requirement
status: draft
related: [.devbook/domain/devbook/features.md#the-devbook-on-the-repositorys-latest-version]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchOverviewTests.A_branch_shows_no_version_control, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchOverviewTests.A_clone_found_behind_offers_the_pull_without_a_check_first, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookUpdateServiceTests.Only_a_scope_reading_its_clone_has_a_clone_to_update, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookUpdateServiceTests.The_overviews_look_at_the_clone_is_the_state_a_check_would_give, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookUpdateServiceTests.Nothing_loaded_seeds_nothing, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookUpdateServiceTests.A_pull_leaves_the_overview_holding_the_clone_it_left]
```

The system SHALL show the version control only while the pane reads the local clone, start it from the clone's currency the session's branch load found, offering "Pull latest" at once for a clone found behind and announcing nothing, and pull only when the reader presses it.

#### Scenario: A clone found behind on opening

- **Given** the clone of `backlog` is 2 commits behind its upstream
- **When** the pane opens on `backlog` and the branch load lands
- **Then** the version control offers "Pull latest" without "Check now" pressed first, and nothing is pulled

#### Scenario: Reading a branch

- **Given** the pane reads the branch `main` of `backlog`
- **When** the reader looks under the area strip
- **Then** no version control is shown

### Requirement: A typed branch is checked before it is refused

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Typing_a_branch_before_the_list_is_loaded_fetches_it_and_switches, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Typing_a_branch_the_list_lacks_asks_github_and_switches, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Typing_an_unknown_branch_says_so_and_leaves_the_source_alone, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.A_branch_typed_in_another_case_means_the_branch_as_it_is_spelled, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubBranchCatalogTests.A_branch_that_is_gone_resolves_to_null_rather_than_throwing]
```

The system SHALL ask GitHub for a branch the reader commits by name that the loaded list does not hold, switch to it when GitHub has it, and otherwise say there is no such branch and leave the source as it was.

#### Scenario: A branch newer than the list

- **Given** the branch list of `backlog` was loaded before `feature/x` was pushed
- **When** the reader types `feature/x` and presses Enter
- **Then** the pane reads `feature/x`

#### Scenario: A branch GitHub does not have

- **Given** `backlog` has no branch `nope`
- **When** the reader types `nope` and presses Enter
- **Then** the pane says no branch named "nope" is in the repository and keeps reading the source it read

### Requirement: Leaving the field mid-search switches nothing

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Leaving_the_field_mid_search_fetches_nothing_and_switches_nothing, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Leaving_an_emptied_field_switches_nothing, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Entering_an_emptied_field_switches_nothing_and_fetches_nothing, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Leaving_the_field_on_a_named_source_switches_to_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Committing_the_source_already_read_says_nothing]
```

The system SHALL switch the source when the field is left only if its text names a source, and fetch nothing and switch nothing when it holds partial search text or nothing.

#### Scenario: Clicking away from a search

- **Given** the pane reads the clone of `backlog`
- **When** the reader types `devb` into the source field and clicks elsewhere
- **Then** nothing is fetched and the pane still reads the clone

### Requirement: Switching says what is read

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Nothing_is_said_about_the_source_until_somebody_switches_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Switching_to_a_branch_says_the_branch_is_being_read_as_a_snapshot, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Switching_back_to_the_clone_says_the_clone_is_being_read, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.A_refusal_on_a_branch_puts_the_branch_back_and_says_why_under_the_strip, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.A_repository_reading_a_branch_says_so_and_says_it_is_read_only]
```

The system SHALL say under the area strip, after the reader switches the source, whether the pane now reads the local clone or a read-only branch snapshot.

#### Scenario: Switching to a branch

- **Given** the pane reads the clone of `backlog`
- **When** the reader switches the source to `main`
- **Then** the strip says `main` is read as a snapshot, and the chapters cannot be edited

## Reviewing what a branch changed

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#reviewing-what-a-branch-changed]
```

> The requirements of **Review changes** in the Devbook pane, read from GitHub and
> held in memory for the session.

### Requirement: Only a branch can be reviewed

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchReviewTests.The_toggle_is_offered_for_a_branch_and_not_for_the_local_clone, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_branch_source_can_be_reviewed_and_the_local_clone_cannot, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchReviewTests.Switching_to_the_local_clone_closes_the_review, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchReviewTests.Moving_to_another_repository_closes_the_review, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchReviewTests.Closing_the_review_goes_back_to_the_chapters]
```

The system SHALL offer **Review changes** only while the pane reads a branch, and close the review when the source becomes the local clone or the pane moves to another repository.

#### Scenario: Back to the clone

- **Given** a review of the branch `feature/x` is open
- **When** the reader switches the source to the local clone
- **Then** the review closes, the chapters are shown, and Review changes is not offered

### Requirement: The review lists the branch's own commits

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubBranchComparisonTests.A_comparison_asks_for_the_three_dot_compare_of_the_two_refs, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubBranchComparisonTests.A_comparison_carries_the_merge_base_and_how_far_apart_the_two_are, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.The_branch_is_compared_with_the_default_branch_by_their_heads, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.Commits_are_listed_newest_first_with_short_sha_subject_and_age, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchReviewTests.Opening_the_review_shows_the_branch_its_chapters_and_the_first_comparison]
```

The system SHALL list, in a branch review, the commits the branch holds that the repository's default branch does not, newest first with short SHA, subject, and age, beside an entry for all the branch's changes against the default branch.

#### Scenario: Opening a review

- **Given** the pane reads `feature/x`, two commits ahead of `main`
- **When** the reader presses Review changes
- **Then** the review lists All changes vs main and the two commits, newest first

### Requirement: A commit is compared with its first parent, the branch with its merge base

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_commit_is_compared_with_its_parent, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubBranchComparisonTests.A_commit_is_read_against_its_first_parent, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_root_commit_adds_every_chapter_and_lists_no_parent_tree, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.Each_chapter_counts_what_moved_between_the_merge_base_and_the_branch, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchReviewTests.Choosing_a_commit_lists_the_chapters_that_commit_changed]
```

The system SHALL compare a chosen commit with its first parent, and the whole branch with its merge base on the default branch.

#### Scenario: Choosing a commit

- **Given** a review of `feature/x` is open
- **When** the reader chooses a commit that edited `domain/devbook/features.md`
- **Then** the chapters listed are the ones that commit changed against its first parent

### Requirement: Changed chapters are listed and other files counted

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.The_whole_branch_lists_its_devbook_chapters_and_counts_the_rest, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_folder_pointed_elsewhere_is_where_its_chapters_are_looked_for, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_chapter_renamed_out_of_the_devbook_reads_as_removed]
```

The system SHALL list the devbook chapters a chosen change touched and count every other file it touched as "+N other files not shown".

#### Scenario: Code beside a chapter

- **Given** a commit changed one devbook chapter and three source files
- **When** the reader chooses it
- **Then** one chapter is listed and the review reads +3 other files not shown

### Requirement: A chapter is compared section by section

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_chosen_chapter_is_compared_section_by_section, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchReviewTests.Choosing_a_chapter_compares_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_chapter_the_scope_does_not_hold_is_an_error]
```

The system SHALL show a chosen chapter's before and after compared section by section.

#### Scenario: Choosing a chapter

- **Given** a commit edited one section of `features.md`
- **When** the reader chooses `features.md` in the review
- **Then** the comparison shows that section changed and the others unchanged

### Requirement: A partial or refused answer is said

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_truncated_commit_list_says_how_many_are_shown, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_full_file_list_says_chapters_may_be_missing, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_cut_short_earlier_tree_says_a_chapter_may_read_as_new, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubBranchComparisonTests.A_comparison_with_more_commits_than_listed_says_it_is_truncated, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubBranchComparisonTests.A_full_file_list_says_it_may_be_cut_short, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_branch_that_is_gone_is_said_rather_than_thrown, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_refused_comparison_is_said_rather_than_thrown, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchReviewTests.What_github_refused_is_said_in_place_of_the_review]
```

The system SHALL say in the review when GitHub listed only part of a change's commits or files, and show what GitHub refused in place of the review rather than failing.

#### Scenario: A long branch

- **Given** `feature/x` holds more commits than GitHub lists in one comparison
- **When** the reader opens its review
- **Then** the review says how many of the commits are shown

## Lobes atlas

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#lobes-atlas, .devbook/domain/devbook/context.md#lobes-atlas]
```

> The requirements of the one atlas that draws the Brain and every devbook as
> brains. All of them hold while `atlas-lobes` is on.

### Requirement: One atlas maps the Brain and every registered devbook

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.With_the_lobes_flag_on_the_headers_atlas_takes_the_screen_and_its_close_returns_to_the_view, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_scope_switch_offers_all_the_brain_each_devbook_and_each_ghost_dashed, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.Each_scope_counts_its_regions_its_items_and_the_notes_linking_in, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_foot_shows_the_hint_with_the_zoom_level_and_the_marker_turns_with_the_camera, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_stylesheet_spaces_the_headline_from_the_title_as_the_design_does]
```

The system SHALL open one full-window atlas from the shell header that draws the Brain and the devbook of every registered repository as brains the reader rotates by dragging and zooms by scrolling.

#### Scenario: Opening from the header

- **Given** a Brain index and two registered repositories with devbooks
- **When** the reader opens the atlas from the shell header
- **Then** the atlas fills the window in the All scope, with the Brain and both devbooks drawn

#### Scenario: Rotating and zooming

- **Given** the atlas is open
- **When** the reader drags across it and then scrolls
- **Then** the drag rotates the brains and the scroll changes the zoom the atlas reports

#### Scenario: The headline

- **Given** the atlas is open in All
- **When** the reader reads its header
- **Then** the dash before the headline stands apart from the title, as in "ATLAS — Brain + 2 devbooks"

### Requirement: The map fills the atlas and its controls float over it

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_stylesheet_floats_every_part_over_a_full_bleed_map, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_parts_are_siblings_of_the_atlas_in_the_order_the_eye_meets_them, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_stylesheet_stacks_a_narrow_window_in_one_column_with_the_panel_in_a_sheet, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_panels_disclosure_names_the_panel_and_the_card_and_opens_the_sheet]
```

The system SHALL draw the map across the whole atlas in a window wider than 60rem, with the title, the scope switch, the left panel, the scope card, the top-right controls, the chips, the list of marks and the foot floating over it, and SHALL stack them in one column in a window 60rem wide or narrower, with the panel and the scope card folded behind one disclosure, the map in view and nothing overlapping.

#### Scenario: A wide window

- **Given** the atlas at 1440 × 900
- **When** the reader opens it
- **Then** the map fills the atlas, and the panel, the card, the controls, the chips, the list and the foot float over it

#### Scenario: Dragging on a control

- **Given** the atlas is open in a wide window
- **When** the reader drags or scrolls on the left panel, a chip or the list
- **Then** the camera does not move

#### Scenario: A phone

- **Given** the atlas at 390 × 844
- **When** the reader opens it
- **Then** the scope switch and the chips each keep one sideways-scrolling row, the panel and the card sit behind one disclosure, the map is in view with its list under it, and nothing overlaps

### Requirement: The atlas offers All, Brain and one scope per devbook

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_scope_switch_offers_all_the_brain_each_devbook_and_each_ghost_dashed, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.Switching_scope_hands_the_renderer_a_new_layout_which_resets_the_camera, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_stylesheet_lets_the_scope_switch_scroll_rather_than_clip_or_squeeze_its_options]
```

The system SHALL offer the scopes All, Brain and one per devbook, drawing the Brain in the middle of All with each devbook as a smaller brain around it.

#### Scenario: All

- **Given** the atlas is open with two registered devbooks
- **When** the reader picks All
- **Then** the Brain is drawn in the middle and each devbook as a smaller brain around it

#### Scenario: One devbook

- **Given** the atlas is open in All
- **When** the reader picks a devbook's scope
- **Then** only that devbook is drawn, by region

#### Scenario: A narrow window

- **Given** the atlas at 390 px wide with the Brain, eleven devbooks and a ghost devbook
- **When** the reader looks for the last scope in the switch
- **Then** the switch scrolls sideways to it, and no scope's name runs into its neighbour's

### Requirement: An unregistered alias is a ghost devbook

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.An_unregistered_alias_is_a_ghost_scope_holding_only_the_named_chapters_in_the_links_words, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_scope_switch_offers_all_the_brain_each_devbook_and_each_ghost_dashed, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesAtlasTests.In_all_each_tag_carries_its_count_and_a_ghosts_says_it_is_not_registered]
```

The system SHALL draw a repository alias the Brain links to that is not registered on this machine as a dashed ghost devbook holding only the chapters the notes name, in the link's own words.

#### Scenario: Another machine's repository

- **Given** Brain notes linking three chapters of an alias not registered here
- **When** the reader opens the atlas
- **Then** the alias has a dashed scope of its own, and its brain is a dashed outline holding those three chapters, each named as its link wrote it

#### Scenario: Its tag in All

- **Given** a ghost devbook and a registered devbook
- **When** the reader opens the atlas in All
- **Then** the registered devbook's tag gives its name and its count, and the ghost's reads its alias and "· not registered"

### Requirement: A chapter of a devbook not indexed yet says the index is missing

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.A_chapter_of_a_devbook_not_indexed_yet_says_so_rather_than_that_it_is_no_chapter, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.A_chapter_an_indexed_devbook_does_not_hold_is_still_no_chapter_in_its_devbook, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.A_chapter_of_a_devbook_not_indexed_yet_says_so_on_its_chip_as_the_card_does]
```

The system SHALL give a chapter the notes name in a registered devbook whose index is not built yet a chip that reads *its devbook is not indexed yet — see its scope card*, and keep *not a chapter in its devbook* for a chapter an indexed devbook does not hold.

#### Scenario: A devbook not indexed yet

- **Given** a Brain note linking a chapter of a registered repository whose devbook index has not been built
- **When** the reader selects that chapter in the atlas
- **Then** it is drawn dashed, its chip reads *its devbook is not indexed yet — see its scope card*, and the scope's card says "Not indexed yet"

#### Scenario: A chapter an indexed devbook does not hold

- **Given** a Brain note linking a path that a registered, indexed devbook has no chapter for
- **When** the reader selects that chapter in the atlas
- **Then** it is drawn dashed and its chip reads *not a chapter in its devbook*

### Requirement: A Brain note's region comes from its PARA area

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.A_brain_note_is_placed_by_its_para_area, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.The_para_field_wins_over_the_folder, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.An_area_declared_beyond_para_is_parietal, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.Archive_is_the_dust_and_no_region, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.The_brain_stem_holds_conventions_and_the_area_roots_and_occipital_the_repository_hubs, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.The_para_attribute_wins_over_the_folder_for_a_note_and_its_chapters, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.A_para_area_in_a_folder_of_another_name_keeps_its_region]
```

The system SHALL place each Brain note in a region by its PARA area: Projects in Frontal; Areas, Resources and any area `CONVENTIONS.md` declares beyond PARA in Parietal; People in Temporal; the linked repository chapters in Occipital; Journal and the inbox in Cerebellum; area roots and `CONVENTIONS.md` in the Brain stem; and Archive as the faint outer dust.

#### Scenario: A project note

- **Given** a note in the Projects area
- **When** the reader opens the Brain scope
- **Then** the note is drawn in Frontal

#### Scenario: The para field wins

- **Given** a note in the Resources folder whose `para` field names Projects
- **When** the reader opens the Brain scope
- **Then** the note is drawn in Frontal, not Parietal

#### Scenario: A declared area beyond PARA

- **Given** a `CONVENTIONS.md` that declares an area `recipes`
- **When** the reader opens the Brain scope
- **Then** the notes in `recipes` are drawn in Parietal

#### Scenario: Archive

- **Given** a note in the Archive area
- **When** the reader opens the Brain scope
- **Then** the note is drawn in the outer dust and in no region

### Requirement: A devbook chapter's region comes from its folder

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.LobesAnatomyScriptTests]
```

The system SHALL place each devbook chapter in a region by its folder: `arc42` in Frontal, `tech` in Parietal, `domain` in Temporal, `design` in Occipital, `ai` in Cerebellum, and each folder's convention root and the reading-order roots in the Brain stem.

#### Scenario: A decision record

- **Given** a devbook with a decision record under `arc42/adr/`
- **When** the reader opens that devbook's scope
- **Then** the record is drawn in Frontal

#### Scenario: A convention root

- **Given** a devbook whose `domain/` folder has a convention root
- **When** the reader opens that devbook's scope
- **Then** the root is drawn in the Brain stem, not Temporal

#### Scenario: Many roots

- **Given** a devbook with twenty-two convention and reading-order roots
- **When** the reader opens that devbook's scope at the default view
- **Then** its Brain stem draws the twenty-two as one cluster under the brain, inside the map

### Requirement: Brain and devbook marks keep their colours and bundles show the links

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesAtlasTests.A_brain_mark_wears_the_brains_tone_in_the_index_and_a_devbook_mark_stays_gold, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_component_stylesheet_paints_a_brain_mark_in_the_brains_accent]
```

The system SHALL draw Brain marks in `color-band-2` and devbook marks in gold, with a bundle from each Brain repository hub to the chapters the notes link, solid when the link resolves and dashed when it does not.

#### Scenario: Bundles in All

- **Given** Brain notes linking chapters of a registered devbook
- **When** the reader opens the All scope
- **Then** solid bundles run from the Brain's hub for that repository to those chapters

#### Scenario: The Brain hub in a devbook's scope

- **Given** Brain notes linking chapters of a registered devbook
- **When** the reader opens that devbook's scope
- **Then** a Brain hub floats beside the devbook with bundles to those same chapters

#### Scenario: An unresolved link

- **Given** a Brain note linking an alias not registered here
- **When** the reader opens the All scope
- **Then** the bundle from the Brain's hub for that alias is dashed

#### Scenario: The list beside the map

- **Given** the atlas in All
- **When** the reader reads the list beside the map
- **Then** each Brain note's mark is in `color-band-2` and each devbook chapter's is gold, as on the map

### Requirement: The panel and its controls act on the current scope

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.In_all_the_panel_lists_each_scope_with_its_count_and_a_row_opens_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.In_a_scope_the_panel_lists_its_six_regions_with_what_each_holds_and_its_count, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.In_a_scope_the_top_right_has_labels_all_none_and_one_chip_per_region, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.In_all_the_chips_are_one_per_scope_and_hide_that_scope, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.All_and_none_act_on_the_current_scope_only, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.Labels_is_a_toggle_that_reports_its_state, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.None_in_a_scope_hides_its_archive_dust_and_its_rows_and_all_brings_them_back, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_show_all_tool_is_named_apart_from_the_scope_switchs_all_and_starts_with_its_word]
```

The system SHALL list the scopes with their counts in the left panel in All and the six regions with what each holds and its count in a single scope, offer Labels, All, None and one chip per region (per scope in All) at the top right, apply the chips, Labels, All and None to the current scope only, hide a scope's Archive dust with its regions on None and show it again on All, and name the top right's All apart from the scope switch's All with a name that starts with "All".

#### Scenario: None in one scope

- **Given** the atlas in the Brain scope with every region shown, and a devbook's regions all shown
- **When** the reader presses None and then picks that devbook's scope
- **Then** the Brain's regions are hidden and the devbook's regions are all still shown

#### Scenario: The panel follows the scope

- **Given** the atlas in All
- **When** the reader picks the Brain scope
- **Then** the left panel, headed Anatomical regions, lists the six regions in place of the scopes, and the chips at the top right are the six regions'

#### Scenario: A row opens a scope

- **Given** the atlas in All
- **When** the reader presses a devbook's row in the left panel
- **Then** the atlas shows that devbook's scope

#### Scenario: None hides the Archive

- **Given** the atlas in the Brain scope with a note in the Archive
- **When** the reader presses None, then All
- **Then** None leaves no mark on the map and no row in the list, the archived note among them, and All brings the note back

#### Scenario: Two buttons read All

- **Given** the atlas in the Brain scope
- **When** a screen reader reads the header
- **Then** the scope switch's option is named "All" and the picture's tool "All regions"

### Requirement: The scope card offers one action for its scope

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_scope_card_says_when_the_scope_was_last_indexed_and_offers_its_action, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_close_button_and_the_card_raise_the_hosts_callbacks, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_stylesheet_scrolls_only_the_panels_rows_so_the_scope_card_stays_in_view]
```

The system SHALL show a scope card naming the scope with its counts, when its index was last built, and one action: Open in the pane for the Brain, Open devbook for a devbook, Register repository for a ghost devbook.

#### Scenario: A devbook

- **Given** the atlas in a registered devbook's scope
- **When** the reader presses Open devbook on its card
- **Then** the pane shows that repository's devbook and the atlas closes

#### Scenario: A ghost devbook

- **Given** the atlas in a ghost devbook's scope
- **When** the reader reads its card
- **Then** the card's one action is Register repository

#### Scenario: When it was indexed

- **Given** the Brain's index was built four minutes ago
- **When** the reader opens the Brain scope
- **Then** its card says "Indexed 4 minutes ago"

#### Scenario: A short window

- **Given** the atlas at 1440 by 900 in the scope of a devbook not indexed yet, whose card explains why
- **When** the reader reads its card
- **Then** the card and its action are in view without scrolling the panel; the rows above it scroll instead

### Requirement: Without a Brain index the atlas offers to build one

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.With_no_brain_index_the_empty_state_offers_to_build_it_and_a_way_back_instead_of_a_map, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.Build_the_index_now_builds_it_and_the_map_takes_the_empty_states_place, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.A_build_that_fails_or_finds_nothing_says_why_and_keeps_the_offer, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.A_host_that_cannot_build_the_index_offers_only_the_way_back, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.While_the_index_builds_the_button_is_busy_says_so_and_a_second_press_starts_no_second_build, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.An_atlas_opened_on_a_scope_over_the_empty_state_opens_there_once_the_index_is_built, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.The_lobes_source_builds_the_brains_index_through_the_refresher, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_empty_states_header_names_the_brain_beside_the_title]
```

The system SHALL, with no Brain index built yet, name the Brain beside the atlas's title, say the Brain is not indexed yet and offer Build the index now and Back to notes instead of drawing an empty map.

#### Scenario: A Brain never indexed

- **Given** a Brain folder whose index has not been built
- **When** the reader opens the atlas
- **Then** its header reads "Atlas" with the Brain's badge beside it, it says the Brain is not indexed yet and offers Build the index now and Back to notes, and draws no map

#### Scenario: Building it

- **Given** the atlas shows that the Brain is not indexed yet
- **When** the reader presses Build the index now
- **Then** the index is built and the map takes the empty state's place

### Requirement: The foot shows the zoom and the orientation

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_foot_shows_the_hint_with_the_zoom_level_and_the_marker_turns_with_the_camera]
```

The system SHALL show under the map the drag and scroll hint with the zoom level, and an orientation marker that turns with the camera.

#### Scenario: Zooming in

- **Given** the atlas at its default view
- **When** the reader scrolls to zoom in to 150%
- **Then** the hint ends with 150% and the orientation marker has turned with the camera

### Requirement: The keyboard browses the list without opening details

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_keyboard_arrows_through_the_list_without_opening_the_sheet_and_enter_opens_it, unit:dotnet:Backlog.UI.Components.UnitTests.GraphAtlasIndexTests.The_arrows_and_the_ends_move_the_active_row_and_leave_the_selection, unit:dotnet:Backlog.UI.Components.UnitTests.GraphAtlasIndexTests.Enter_or_space_selects_the_row_the_keyboard_is_on, unit:dotnet:Backlog.UI.Components.UnitTests.GraphAtlasIndexTests.Components_js_refuses_the_index_keys_default_at_the_keydown_and_lets_them_bubble, unit:dotnet:Backlog.UI.Components.UnitTests.GraphAtlasTests.Arrowing_the_index_spotlights_the_row_on_the_picture_and_enter_selects_it, unit:dotnet:Backlog.UI.Components.UnitTests.GraphAtlasTests.Clearing_the_selection_leaves_the_tab_stop_on_the_row_and_clears_the_spotlight]
```

The system SHALL keep the list beside the map one tab stop whose arrow keys, Home and End move the focus and the map's spotlight from row to row without opening a mark's details, open them on Enter or Space, and on Escape close them with the focus back on the mark's row.

#### Scenario: Arrowing down the list

- **Given** the atlas in the Brain scope with no mark selected
- **When** the reader tabs into the list beside the map and presses the down arrow
- **Then** the focus and the map's spotlight move from the first mark to the second, and no details are open

#### Scenario: Opening and closing a mark's details

- **Given** the reader has arrowed to a mark in the list
- **When** the reader presses Enter, then Escape
- **Then** Enter opens that mark's details, and Escape closes them with the focus back on its row

### Requirement: Every atlas control is a full-size button

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.Every_control_of_the_atlas_is_a_real_button, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.Every_control_of_the_empty_state_is_a_real_button, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_stylesheet_gives_every_atlas_control_a_44px_target_and_a_focus_ring]
```

The system SHALL make every control of the atlas a real button at least 44 px square with a visible focus ring, and every toggle report `aria-pressed`.

#### Scenario: A region chip

- **Given** the atlas in the Brain scope
- **When** the reader tabs to a region chip
- **Then** the chip shows a focus ring, is at least 44 px square, and reports whether it is pressed

### Requirement: Selecting a mark opens its details and follows it into the Devbook

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.A_brain_note_offers_to_open_in_the_pane, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.A_resolved_chapter_opens_its_devbook_and_a_dashed_one_says_why_it_cannot, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.A_devbook_chapter_no_note_links_to_still_opens_in_its_devbook, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.A_chapter_followed_out_of_the_atlas_opens_the_devbook_in_its_place, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.A_heading_chapter_followed_out_of_the_atlas_opens_the_devbook_at_that_heading, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneChapterNavigationTests.A_request_with_an_anchor_into_an_arc42_chapter_brings_the_heading_into_view, unit:dotnet:Backlog.UI.Components.UnitTests.BacklogRevealHeadingScriptTests]
```

The system SHALL open a mark's details when the reader selects it on the map or in the list beside it, and following a Brain note, a gold chip or a devbook's own chapter out of them SHALL give the Devbook the screen in the atlas's place, at the heading the chapter names when it is a heading inside a file.

#### Scenario: Selecting a mark

- **Given** the atlas is open in the All scope
- **When** the reader selects a devbook chapter's mark in the list beside the map
- **Then** its details open, offering to open the chapter in its devbook

#### Scenario: A dashed chip

- **Given** a chapter a note links to in a repository not registered here
- **When** the reader selects its mark
- **Then** its details show the dashed chip with the reason it cannot be followed, and nothing to press

#### Scenario: A chapter that is a heading inside a file

- **Given** the atlas shows *Quality Goals*, a heading in `.devbook/arc42/01-introduction-and-goals.md`
- **When** the reader selects its mark and opens it
- **Then** the atlas closes, the Devbook opens that file, and the reading is scrolled to *Quality Goals*, whether the chapter was the devbook's own or a note's gold chip

### Requirement: The pane's Atlas button follows the flag

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneAtlasTests.With_the_lobes_flag_on_the_atlas_button_opens_the_lobes_atlas_at_the_repository, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneAtlasTests.With_the_lobes_flag_off_the_atlas_button_opens_todays_atlas, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneAtlasTests.On_the_brain_the_atlas_button_opens_the_lobes_atlas_at_the_brain, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.The_devbooks_atlas_button_opens_the_atlas_at_its_scope_and_the_way_back_returns_to_the_devbook]
```

The system SHALL open the Lobes atlas at the pane's scope from the Devbook pane's Atlas button while `atlas-lobes` is on, and today's atlas while it is off, leaving the Technology section to its own views.

#### Scenario: On a repository

- **Given** `atlas-lobes` is on and the pane shows `backlog`
- **When** the reader presses Atlas
- **Then** the Lobes atlas opens in the `backlog` scope

#### Scenario: Flag off

- **Given** `atlas-lobes` is off and the pane shows the Brain
- **When** the reader presses Atlas
- **Then** today's atlas of the Brain opens

#### Scenario: The Technology section

- **Given** `atlas-lobes` is on
- **When** the reader opens the Technology section
- **Then** it shows its own views, and no atlas draws the stack

### Requirement: A drag turns the brains like a globe in the hand

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.LobesCameraScriptTests.A_fling_slows_and_comes_to_rest_within_a_second_and_a_half, unit:dotnet:Backlog.UI.Components.UnitTests.LobesCameraScriptTests.One_long_frame_moves_the_camera_as_far_as_two_short_ones]
```

The system SHALL turn the near face of a brain with the pointer on both axes while the reader drags, and carry a brisk drag on after release until it comes to rest.

#### Scenario: Dragging across

- **Given** the brains are at rest
- **When** the reader drags to the right across the map
- **Then** the near face of the brains moves to the right with the pointer

#### Scenario: A fling

- **Given** the atlas is open
- **When** the reader drags quickly across it and lets go while moving
- **Then** the brains keep turning the same way and come to rest within about a second and a half

#### Scenario: A click is no fling

- **Given** the brains are at rest
- **When** the reader presses and releases on a mark without moving the pointer
- **Then** the camera stays where it was and the mark's details open

#### Scenario: Catching a spin

- **Given** the brains are still turning after a fling
- **When** the reader presses on a mark
- **Then** the brains stop where they are and no details open

### Requirement: A scroll zooms toward the pointer

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.LobesCameraScriptTests.A_zoom_keeps_the_point_under_the_cursor_where_it_is, unit:dotnet:Backlog.UI.Components.UnitTests.LobesCameraScriptTests.The_pan_is_held_at_the_centre_until_zoomed_in_and_never_past_the_edge, unit:dotnet:Backlog.UI.Components.UnitTests.LobesCameraScriptTests.An_easing_closes_on_its_target_and_then_stops_there]
```

The system SHALL zoom toward the point under the pointer when the reader scrolls, easing to each step and staying between half and 2.4 times.

#### Scenario: Zooming at the pointer

- **Given** the atlas is open
- **When** the reader scrolls in with the pointer on a mark away from the centre
- **Then** the zoom eases to its new level and the mark stays under the pointer

#### Scenario: At the limit

- **Given** the atlas is zoomed in to 2.4 times
- **When** the reader scrolls in again
- **Then** the zoom stays at 2.4 times and the foot still reads 240%

### Requirement: A scope switch eases the camera home

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.LobesCameraScriptTests.The_scope_curve_starts_at_rest_ends_at_rest_and_never_turns_back, unit:dotnet:Backlog.UI.Components.UnitTests.LobesCameraScriptTests.Easing_home_takes_the_short_way_round_however_far_the_idle_turn_wound]
```

The system SHALL ease the camera from the last view to the default view, the short way round, when the reader switches scope.

#### Scenario: Switching scope

- **Given** the reader has turned the brains in All
- **When** the reader picks a devbook's scope
- **Then** the camera turns through the views in between and settles on the default view within a second

#### Scenario: After a long idle turn

- **Given** the brains have turned on their own for several minutes
- **When** the reader switches scope
- **Then** the camera turns less than half a turn on its way to the default view

### Requirement: A region or Labels toggle leaves the camera as it was

```meta
type: requirement
status: draft
```

The system SHALL keep the camera's view and its motion when the reader shows or hides a region or turns Labels on or off.

#### Scenario: A toggle mid-turn

- **Given** the brains are turning
- **When** the reader hides a region or turns Labels off
- **Then** the brains keep turning from where they were, with no jump

### Requirement: The brains turn on their own only while nobody uses them

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.LobesCameraScriptTests.The_idle_turn_waits_its_delay_then_ramps_in_over_transition_slow, unit:dotnet:Backlog.UI.Components.UnitTests.LobesCameraScriptTests.The_idle_turn_is_slow_enough_to_take_about_a_hundred_seconds_a_turn]
```

The system SHALL turn the brains slowly on their own only after four seconds without input, and stop the turn at once when the reader presses, scrolls or rests the pointer on a mark.

#### Scenario: Left alone

- **Given** the atlas is open with the brains at rest
- **When** the reader leaves the map untouched for four seconds
- **Then** the brains start turning slowly, about one turn in a hundred seconds

#### Scenario: Coming back

- **Given** the brains are turning on their own
- **When** the reader presses or scrolls on the map, or rests the pointer on a mark
- **Then** the turn stops at once, and starts again only after four more seconds without input

### Requirement: Reduced motion holds the atlas still

```meta
type: requirement
status: draft
```

The system SHALL, under reduced motion, turn the brains only while a drag lasts, land a scroll and a scope switch at once, and never turn the brains on their own.

#### Scenario: Reduced motion

- **Given** reduced motion is on
- **When** the reader drags quickly and lets go, then scrolls, then switches scope
- **Then** the drag turns the brains only until the release, and the zoom and the default view arrive at once

#### Scenario: Waiting under reduced motion

- **Given** reduced motion is on and the atlas is open
- **When** the reader leaves the map untouched for longer than four seconds
- **Then** the brains do not move

## Technology view

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#technology-view]
```

> The requirements of the Technology area of the Devbook pane: its five views,
> the measures they compute from `depends-on`, and how they are operated.

### Requirement: Five views replace the Atlas and Layers tabs

```meta
type: requirement
status: draft
```

The system SHALL show the Technology area as five views, Overview, Dependencies, Complexity, Clusters and Type lenses, switched from one tab bar, with no Atlas or Layers tab.

#### Scenario: Opening the Technology area

- **Given** a repository whose devbook has a technology folder
- **When** the reader opens the Technology area
- **Then** one tab bar offers Overview, Dependencies, Complexity, Clusters and Type lenses
- **And** neither an Atlas nor a Layers tab is offered

### Requirement: The selection is shared across the views

```meta
type: requirement
status: draft
```

The system SHALL keep the technology selected in one Technology view selected when the reader switches to another.

#### Scenario: From Overview to Dependencies

- **Given** ASP.NET Core is selected in Overview
- **When** the reader switches to Dependencies
- **Then** Dependencies shows ASP.NET Core in focus

#### Scenario: From Clusters back to Overview

- **Given** the reader picked Playwright in Clusters
- **When** the reader switches to Overview
- **Then** the inspector shows Playwright

### Requirement: Used by counts the direct users

```meta
type: requirement
status: draft
```

The system SHALL show as a technology's Used by the number of technologies whose `depends-on` names it.

#### Scenario: A technology three others sit on

- **Given** Razor Components, Blazor Server and ASP.NET Core are the only technologies whose `depends-on` names C# Language
- **When** the reader selects C# Language
- **Then** Used by reads 3

#### Scenario: A leaf

- **Given** no technology's `depends-on` names Playwright
- **When** the reader selects Playwright
- **Then** Used by reads 0 and the inspector says nothing depends on it

### Requirement: Breaks if it changes counts the users transitively

```meta
type: requirement
status: draft
```

The system SHALL show as a technology's Breaks if it changes the number of technologies that reach it through `depends-on` at any distance, each counted once.

#### Scenario: A user reached two ways

- **Given** Blazor Server depends on Razor Components and on ASP.NET Core, and Razor Components depends on ASP.NET Core
- **And** nothing else depends on any of them
- **When** the reader selects ASP.NET Core
- **Then** Breaks if it changes reads 2

### Requirement: Rests on counts the depends-on transitively

```meta
type: requirement
status: draft
```

The system SHALL show as a technology's Rests on the number of technologies it reaches through `depends-on` at any distance, each counted once.

#### Scenario: A framework three deep

- **Given** Razor Components depends on ASP.NET Core and C# Language, ASP.NET Core on .NET Runtime and C# Language, and C# Language on .NET Runtime
- **When** the reader selects Razor Components
- **Then** Rests on reads 3

#### Scenario: A foundation

- **Given** .NET Runtime declares no `depends-on`
- **When** the reader selects .NET Runtime
- **Then** Rests on reads 0 and the inspector says it sits on nothing

### Requirement: Depth is the longest chain to a foundation

```meta
type: requirement
status: draft
```

The system SHALL show as a technology's Depth the number of hops in the longest `depends-on` chain from it to a technology with no `depends-on`.

#### Scenario: The longest of two paths

- **Given** ASP.NET Core depends on .NET Runtime directly and through C# Language
- **When** the reader selects ASP.NET Core
- **Then** Depth reads 2

### Requirement: Complexity is rests on plus depth, banded

```meta
type: requirement
status: draft
```

The system SHALL show as a technology's Complexity its Rests on plus its Depth, banded Low below 6, Medium from 6 to 9 and High from 10.

#### Scenario: Low

- **Given** ASP.NET Core rests on 2 technologies at a depth of 2
- **When** the reader selects ASP.NET Core
- **Then** Complexity reads 4, Low

#### Scenario: Medium at the lower edge

- **Given** Razor Components rests on 3 technologies at a depth of 3
- **When** the reader selects Razor Components
- **Then** Complexity reads 6, Medium

#### Scenario: High at the lower edge

- **Given** a technology rests on 6 technologies at a depth of 4
- **When** the reader selects it
- **Then** Complexity reads 10, High

### Requirement: Colour follows the type family

```meta
type: requirement
status: draft
```

The system SHALL colour each technology in the Technology views by its type's family: framework; platform; runtime; language and package; library, tool and plugin; skill; agent, service and format; protocol.

#### Scenario: Two types of one family

- **Given** a technology of type `tool` and one of type `plugin`
- **When** the reader opens Clusters
- **Then** both are drawn in the colour of the library, tool and plugin family

#### Scenario: Two families

- **Given** a technology of type `skill` and one of type `agent`
- **When** the reader opens Clusters
- **Then** they are drawn in two different colours

### Requirement: A technology not yet adopted is drawn hollow

```meta
type: requirement
status: draft
```

The system SHALL draw a technology whose status is not `adopted` hollow in every Technology view that draws it, and an `adopted` one filled.

#### Scenario: A candidate beside an adopted choice

- **Given** Background Workers is `candidate` and .NET Runtime is `adopted`
- **When** the reader opens Clusters
- **Then** Background Workers is drawn hollow and .NET Runtime filled

### Requirement: The inspector opens on status, sits on and used by

```meta
type: requirement
status: draft
```

The system SHALL open the inspector of every selected technology, whatever its type, on its status, what it sits on and what uses it, with the section its type chooses below them.

#### Scenario: A package

- **Given** the reader selects a technology of type `package`
- **When** the inspector opens
- **Then** it shows status, sits on and used by first, and the package section below them

#### Scenario: A type with no section of its own

- **Given** the reader selects a technology of type `protocol`
- **When** the inspector opens
- **Then** it shows status, sits on and used by

### Requirement: A lens shows only what the chapter and the graph record

```meta
type: requirement
status: draft
```

The system SHALL fill a type lens only from the technology chapter's metadata and the graph, and leave out each fact the chapter does not record.

#### Scenario: A package with a version

- **Given** a package chapter declares `version: "2.2.0"`
- **When** the reader opens its lens
- **Then** the lens shows version 2.2.0

#### Scenario: A package with no version

- **Given** a package chapter declares no `version`
- **When** the reader opens its lens
- **Then** the lens shows no version line, neither empty nor a placeholder

#### Scenario: Alternatives

- **Given** the GitHub CLI chapter declares `alternatives: ["Octokit.NET", "raw REST calls"]`
- **When** the reader opens its lens
- **Then** the lens names Octokit.NET and raw REST calls as the alternatives

### Requirement: Status is changed from the inspector

```meta
type: requirement
status: draft
```

The system SHALL let the reader change a technology's status from the inspector and write the new status into that technology's chapter in its layer file.

#### Scenario: Adopting a trial

- **Given** Azure Cosmos DB is `trial` in `.devbook/tech/cloud.md`
- **When** the reader sets its status to `adopted` in the inspector
- **Then** the Azure Cosmos DB chapter in `cloud.md` reads `status: adopted`
- **And** the views draw it filled

#### Scenario: A source that cannot be written

- **Given** the pane reads a branch snapshot
- **When** the reader opens the inspector
- **Then** the status is shown and cannot be changed

### Requirement: Every drawn technology is a button

```meta
type: requirement
status: draft
```

The system SHALL draw each technology on the Clusters map and the Complexity scatter as a button that selects it.

#### Scenario: Pressing a point

- **Given** the reader opens Complexity
- **When** the reader presses the point for .NET Runtime
- **Then** .NET Runtime is selected

#### Scenario: Tabbing to a technology

- **Given** the reader opens a layer in Clusters
- **When** the reader tabs to one of its technologies and presses Enter
- **Then** that technology is selected

### Requirement: Every pointer view has a list companion

```meta
type: requirement
status: draft
```

The system SHALL pair each Technology view a pointer operates with a list or table on screen that selects the same technologies and shares its selection.

#### Scenario: Selecting from the companion

- **Given** the reader opens Complexity
- **When** the reader picks .NET Runtime from its list with the keyboard
- **Then** the point for .NET Runtime is shown selected

#### Scenario: Selecting from the picture

- **Given** the reader opens Clusters
- **When** the reader presses SQLite on the map
- **Then** SQLite is the selected row of the companion list

### Requirement: A technology naming itself is not its own user

```meta
type: requirement
status: draft
```

The system SHALL leave a technology out of its own Used by, Breaks if it changes and Rests on when its `depends-on` names itself.

#### Scenario: A self-reference

- **Given** Playwright's `depends-on` names Playwright and .NET Runtime, .NET Runtime declares no `depends-on`, and no other technology names Playwright
- **When** the reader selects Playwright
- **Then** Used by reads 0 and Rests on reads 1

### Requirement: The members of a cycle share one depth

```meta
type: requirement
status: draft
```

The system SHALL give every member of a `depends-on` cycle the depth of the longest chain that leaves the cycle, count no hop between two members, and end every walk at a technology it has already visited.

#### Scenario: Two technologies naming each other

- **Given** Razor Components depends on Blazor Server, Blazor Server on Razor Components and on .NET Runtime, and .NET Runtime on nothing
- **When** the reader selects Razor Components and then Blazor Server
- **Then** each reads Depth 1 and Rests on 2
- **And** the Technology area stays responsive

### Requirement: A technology from another folder is measured, not counted

```meta
type: requirement
status: draft
```

The system SHALL count a chapter of another devbook folder that a technology's `depends-on` names in the measures, and leave it out of the folder's per-type, per-family and per-status counts, the Complexity scatter and the Clusters map.

#### Scenario: A depends-on into another folder

- **Given** Blazor Server's `depends-on` names .NET Runtime and a chapter of `.devbook/arc42/`, and .NET Runtime declares no `depends-on`
- **When** the reader selects Blazor Server
- **Then** Rests on reads 2
- **And** the Overview's type and status counts leave the `arc42/` chapter out
- **And** neither the Complexity scatter nor the Clusters map draws it

### Requirement: The Complexity quadrants split at 3.5 and 4.5

```meta
type: requirement
status: draft
```

The system SHALL place a technology on the Complexity scatter in Hubs at used by 4 or more and rests on 5 or more, in Foundations at used by 4 or more and rests on 4 or fewer, in Deep leaves at used by 3 or fewer and rests on 5 or more, and in Leaves otherwise, and show each quadrant's name with its one line.

#### Scenario: Each side of each split

- **Given** .NET Runtime is used by 4 and rests on 0, Razor Components is used by 3 and rests on 5, Blazor Server is used by 4 and rests on 5, and Playwright is used by 3 and rests on 4
- **When** the reader opens Complexity
- **Then** .NET Runtime is in Foundations, Razor Components in Deep leaves, Blazor Server in Hubs and Playwright in Leaves

#### Scenario: The quadrant lines

- **Given** every quadrant holds a technology
- **When** the reader opens Complexity
- **Then** Foundations reads "Load-bearing. Upgrade on purpose, never in passing." and Leaves reads "Cheap to change or replace."

#### Scenario: An empty quadrant

- **Given** no technology is used by 4 or more and rests on 5 or more
- **When** the reader opens Complexity
- **Then** the Hubs line ends "None today."

### Requirement: The used-by axis is square-root scaled

```meta
type: requirement
status: draft
```

The system SHALL place a technology across the Complexity scatter at the square root of its used by over the square root of the axis's end, the end being the largest used by and never less than 4.

#### Scenario: Half way across

- **Given** the most-used technology is used by 4
- **When** the reader opens Complexity
- **Then** a technology used by 1 is drawn half way across the plot

### Requirement: Technologies at one place are one point

```meta
type: requirement
status: draft
```

The system SHALL draw the technologies with the same used by and rests on as one point labelled with every name, and select the next of them each time the point is pressed again.

#### Scenario: Pressing a merged point twice

- **Given** Playwright and SQLite, in that reading order, are each used by 0 and rest on 1
- **When** the reader opens Complexity
- **Then** one point reads "Playwright · SQLite"
- **When** the reader presses it
- **Then** Playwright is selected
- **When** the reader presses it again
- **Then** SQLite is selected

### Requirement: Dependencies shows a technology at its nearest hop

```meta
type: requirement
status: draft
```

The system SHALL show each technology the Dependencies view reaches once, at its nearest hop, marked with every further hop it is also reached at, and from 2 hops with the technology one hop nearer that it is reached through.

#### Scenario: Reached at one and two hops

- **Given** ASP.NET Core depends on .NET Runtime directly and through C# Language
- **When** the reader selects ASP.NET Core in Dependencies
- **Then** .NET Runtime shows once, in the 1 hop column, marked "also 2 hops"

#### Scenario: Via

- **Given** Razor Components depends only on ASP.NET Core, and ASP.NET Core on .NET Runtime
- **When** the reader selects Razor Components in Dependencies
- **Then** .NET Runtime shows at 2 hops marked "via ASP.NET Core"

### Requirement: Dependencies groups by hop or by layer

```meta
type: requirement
status: draft
```

The system SHALL let the reader group the Dependencies cards by hop or by layer.

#### Scenario: Grouping by layer

- **Given** the reader selects ASP.NET Core in Dependencies
- **When** the reader sets Group by to Layer
- **Then** the cards stand in one column per layer, and each card names its hop

### Requirement: At most ten paths to a foundation

```meta
type: requirement
status: draft
```

The system SHALL list at most 10 paths from the selected technology down to a foundation, say when more exist, and show the longest path running through it.

#### Scenario: More paths than the list holds

- **Given** more than 10 paths lead down from Blazor Server to a foundation
- **When** the reader selects Blazor Server in Dependencies
- **Then** 10 paths are listed with a note that more lead down
- **And** the longest path through Blazor Server is shown below them

### Requirement: Clusters opens on the selection's layer

```meta
type: requirement
status: draft
```

The system SHALL open Clusters on the selected technology's layer, with that technology in focus, and on All layers when nothing is selected.

#### Scenario: Arriving with a selection

- **Given** SQLite is selected in Overview
- **When** the reader switches to Clusters
- **Then** SQLite's layer is open with SQLite in focus
- **And** the breadcrumb reads All layers, the layer and SQLite

### Requirement: The Clusters trail keeps the last six

```meta
type: requirement
status: draft
```

The system SHALL keep the last 6 technologies the reader followed in Clusters as a trail, and step back along it with Back.

#### Scenario: A seventh step

- **Given** the reader has followed six technologies in Clusters
- **When** the reader follows a seventh
- **Then** the trail shows the last six, without the first

#### Scenario: Back

- **Given** the trail ends Razor Components, then ASP.NET Core
- **When** the reader presses Back
- **Then** Razor Components is in focus and ends the trail

### Requirement: Type lenses shows one card per type in use

```meta
type: requirement
status: draft
```

The system SHALL show on the Type lenses board one card per type in use, each with its type's lens for the selected technology when that technology is of its type and otherwise for the type's most-used technology, and a Lens per type table below the cards.

#### Scenario: The most-used technology of a type

- **Given** no framework is selected, and ASP.NET Core is the framework the most others use
- **When** the reader opens Type lenses
- **Then** the framework card shows ASP.NET Core's lens

#### Scenario: The selected technology's own card

- **Given** Razor Components, a framework, is selected
- **When** the reader opens Type lenses
- **Then** the framework card shows Razor Components' lens

#### Scenario: The lens table

- **Given** the reader opens Type lenses
- **When** the board is drawn
- **Then** a Lens per type table names, for each lens, the types it serves, what it shows and what it reads

## Domain view by unit

```meta
type: requirements
status: draft
roadmap: [devbook-domain-view]
related: [.devbook/domain/devbook/features.md#domain-view-by-unit, .devbook/arc42/adr/0044-the-domain-view-reads-by-unit.md]
```

> The requirements of the Domain area read by unit, behind the
> [New domain view](context.md#new-domain-view) switch (local ADR 0044). The plan
> `+devbook-domain-view` builds them page by page; the Context map, a context's
> page with its neighbours and its Overview, Domain, Features, Requirements, Model, Flow, the
> Glossary and the Brain and task links are built. The Demo button is not.

### Requirement: The flag off keeps today's Domain view

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneDomainViewFlagTests.With_the_switch_off_the_Domain_area_is_todays_panel, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.With_the_switch_off_a_split_context_is_todays_flat_list, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.With_the_switch_off_an_unsplit_context_is_todays_list, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneDomainViewFlagTests.Flipping_the_switch_relists_the_open_Domain_menu]
```

The system SHALL show the Domain area exactly as it reads today while the `devbook-domain-view` switch is off.

#### Scenario: A fresh install

- **Given** the `devbook-domain-view` switch has never been changed
- **When** the reader opens the Domain area of a repository's Devbook
- **Then** the Domain area reads as it did before the switch existed

### Requirement: The menu lists the files in reading order

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.Lists_an_unsplit_context_as_its_kinds, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.Lists_a_split_context_by_kind_in_reading_order]
```

The system SHALL list a bounded context's files in the devbook reading order, one row per file, each row titled by its kind.

#### Scenario: An unsplit context

- **Given** a context holding `context.md`, `domain.md`, `features.md`, `requirements.md`, `model.md` and `flow.md`
- **When** the reader opens the context in the Domain view
- **Then** the menu lists the context, Domain, Features, Requirements, Model and Flow rows in that order, and the Glossary is in the Domain page's chapter bar

### Requirement: A split file is a child of its kind's row

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.Nests_each_split_file_under_its_kind_labelled_by_its_name_without_a_database, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.Labels_a_split_row_by_its_heading_from_the_database, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.A_further_dot_nests_one_level_deeper, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.A_kind_with_no_base_file_is_a_row_that_only_opens_its_children]
```

The system SHALL list a file named `<kind>.<name>.md` under its kind's row, titled by the file's own heading, and nest each further dot one level deeper.

#### Scenario: A split domain

- **Given** a context holding `domain.md` and `domain.order.md`, whose heading is *Order*
- **When** the reader opens the context in the Domain view
- **Then** the Domain row holds a child row titled *Order*

#### Scenario: Two dots

- **Given** a context holding `domain.order.md` and `domain.order.line.md`, whose heading is *Order line*
- **When** the reader opens the context in the Domain view
- **Then** *Order line* is a child of the *Order* row

### Requirement: A kind's row reads its heading and counts its pages

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.A_kind_row_counts_the_rows_under_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.A_kind_row_is_labelled_by_its_base_file_s_heading_from_the_database, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.The_assets_folder_draws_no_row_but_another_folder_does, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuTreeViewTests.A_counted_row_shows_its_count_after_its_label]
```

The system SHALL title a kind's row by its base file's heading unless that heading only repeats the context's name, show beside it how many rows it holds, and draw no row for the context's `assets` folder.

#### Scenario: A split context in its own words

- **Given** a context whose `flow.md` is headed *Flows* and which holds ten `flow.<name>.md` files
- **When** the reader opens the context in the Domain view
- **Then** the menu has a *Flows* row showing 10, and no *Assets* row

### Requirement: The rules are the Always true tab

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainUnitPageTests.An_aggregate_opens_on_about_with_always_true_beside_it, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesTests.A_domain_file_s_rules_are_its_invariants_file_beside_it, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesTests.A_split_page_reads_every_rule_of_its_own_invariants_file, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.Draws_no_row_for_invariants_dependencies_demo_or_underscore_files]
```

The system SHALL show `domain.<name>.invariants.md` as the Always true tab of the `domain.<name>.md` page and give it no row of its own.

#### Scenario: An aggregate with its rules

- **Given** a context holding `domain.order.md` and `domain.order.invariants.md`
- **When** the reader opens *Order*
- **Then** the page offers the About and Always true tabs, Always true shows the rules, and the menu has no row for the invariants file

### Requirement: A rule is its claim and its proof

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesTests.Each_rule_is_its_claim_marked_by_its_proof, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesTests.Rules_group_by_the_concept_their_enforced_at_names_falling_back_to_the_aggregate, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesTests.Rules_under_a_group_heading_read_as_that_group_and_link_what_it_is_about, unit:dotnet:Backlog.UI.Components.UnitTests.DomainAlwaysTrueTests.The_aggregate_s_always_true_tab_lists_its_rules_by_concept_with_their_marks, unit:dotnet:Backlog.UI.Components.UnitTests.DomainAlwaysTrueTests.A_written_group_is_a_card_headed_by_its_words_with_what_it_is_about_beside_it, unit:dotnet:Backlog.UI.Components.UnitTests.DomainAlwaysTrueTests.The_tab_counts_the_rules_and_each_mark, unit:dotnet:Backlog.UI.Components.UnitTests.DomainAlwaysTrueTests.An_open_rule_says_nobody_has_settled_it, unit:dotnet:Backlog.UI.Components.UnitTests.DomainAlwaysTrueTests.Where_each_rule_is_enforced_shows_only_when_asked, unit:dotnet:Backlog.UI.Components.UnitTests.DomainAlwaysTrueTests.A_shared_grouping_s_rules_show_on_the_base_page]
```

The system SHALL show each rule on Always true as its heading, marked ✓ when its chapter names a test, ○ when it names none and ? when its `Enforced at:` line is `open`, with the rules and each mark counted, in a card per group the invariants chapter writes — headed by its words, with what its `related` names beside them — and, for a rule outside every such group, under the domain concept its `Enforced at:` line names or else under the aggregate, with its first sentence and where it is enforced hidden until the reader asks for them.

#### Scenario: Rules in the groups the chapter writes

- **Given** an *Order* invariants chapter holding a `### How an order moves` heading of `type: invariants`, whose `related` names *Order* and the order lifecycle, with two rules beneath it, one of them `open`
- **When** the reader opens Always true
- **Then** the tab reads *Always true · 2*, the bar counts one rule proved and one open, and both rules sit in one card headed *How an order moves* with *Order lifecycle* linked beside it and *Order* not linked
- **And** the open rule says it is not built until someone decides

#### Scenario: Rules by concept

- **Given** an *Order* aggregate owning *Order Line*, and an invariants chapter holding a rule enforced at `Place()` and one enforced at `OrderLine.SetQuantity()`
- **When** the reader opens Always true
- **Then** the first rule is under *Order* and the second under *Order Line*, and neither shows where it is enforced

#### Scenario: Where each is enforced

- **Given** Always true open on *Order*
- **When** the reader presses *Show where each is enforced*
- **Then** each rule shows its first sentence and its `Enforced at:` line, and pressing it again hides them

#### Scenario: Shared rules on the base page

- **Given** a `domain.md` with a `## Shared Value Objects` grouping holding *Money*, and an invariants chapter about *Money*
- **When** the reader opens the Domain row
- **Then** the chapter bar offers *Shared Value Objects*, and it shows *Money*'s rules

### Requirement: A demo is a button on its page

```meta
type: requirement
status: draft
```

The system SHALL offer a `*.demo.html` file as a Demo button on the page it belongs to and give it no row of its own.

#### Scenario: A page with a demo

- **Given** a context holding `features.md` and `features.demo.html`
- **When** the reader opens Features
- **Then** the page offers a Demo button, and the menu has no row for the demo

### Requirement: Dependencies are the neighbours atop the context's page

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewContextPageTests.Neighbours_reads_dependencies_md_into_listens_to_listen_to_and_hand_in_hand, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewContextPageTests.A_context_without_dependencies_md_reads_its_own_Dependencies_chapter, unit:dotnet:Backlog.UI.Components.UnitTests.DomainNeighboursTests, unit:dotnet:Backlog.UI.Components.UnitTests.DomainContextViewTests.The_board_groups_the_neighbours_and_dashes_an_external_system, unit:dotnet:Backlog.UI.Components.UnitTests.DomainContextViewTests.The_page_opens_with_its_title_and_tags_each_card]
```

The system SHALL show a context's `dependencies.md` as the neighbours at the top of the context's page, each relation worded by the first sentence of its table's "Why" or "What it relies on" column cut to a short phrase, the whole sentence on hover, and give the file no row of its own. A neighbour stands by the way information goes: the ones that supply the context on the left as what it listens to, the ones it supplies on the right as who listens to it, read from the pattern's role mark — *(Inbox = supplier)*, *(Tasks = customer)* — and, where a row has none, from its table, outbound on the left and inbound on the right. Its Partnership neighbours stand below as hand in hand, and on neither side. A neighbour that is not one of the domain's contexts is a dashed card, and outside systems on one side that share a contract are one card. No card names the relationship's pattern. A context that keeps its tables in a `## Dependencies` chapter of `context.md` reads the same.

#### Scenario: Each neighbour stands where its information goes

- **Given** a context whose `dependencies.md` lists Inbox as an inbound dependent with *(Inbox = supplier)*, Productivity as an outbound dependency with *(Tasks = supplier)*, and Roadmap Planning both as an outbound Customer/Supplier and as an inbound Partnership
- **When** the reader opens the context
- **Then** Inbox stands on the left, Productivity on the right, and Roadmap Planning only under hand in hand

#### Scenario: A context with neighbours

- **Given** a context whose `dependencies.md` lists Tasks with the Why *Notes link to and embed tasks*
- **When** the reader opens the context
- **Then** Tasks is listed with *Notes link to and embed tasks*, and the menu has no row for `dependencies.md`

#### Scenario: A partner and an outside system

- **Given** a context whose `dependencies.md` lists Devbook as a Partnership and *GitHub (external)* as an outbound ACL
- **When** the reader opens the context
- **Then** Devbook stands under hand in hand, GitHub is a dashed card on the left, and neither card says *Partnership* or *ACL*

### Requirement: A context is one page however it is reached

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainContextViewTests.The_page_opens_on_the_neighbours_with_the_Overview_below_and_no_tabs, unit:dotnet:Backlog.UI.Components.UnitTests.DomainContextViewTests.The_middle_card_draws_no_Overview_link_on_the_context_page, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewContextPageTests.A_dependencies_path_opens_the_same_one_page, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewContextMapPageTests.Picking_a_card_opens_that_contexts_one_page_as_the_menu_does]
```

The system SHALL show a bounded context as one page with no tabs, whichever way the reader reached it — a Context map card, a related link to its `context.md`, a link to its `dependencies.md`, or its menu row. The page opens on the context's neighbours and shows its Overview below them.

#### Scenario: From the map and from the menu

- **Given** a domain folder whose context map names Tasks
- **When** the reader picks the Tasks card on the Context map, and later picks the Tasks row in the menu
- **Then** both open the same Tasks page, its neighbours first and its Overview below, with no tab to choose

#### Scenario: From a link to the dependencies

- **Given** a chapter that links to `tasks/dependencies.md`
- **When** the reader follows the link
- **Then** the Tasks page opens as its menu row opens it, and the menu selects the Tasks row

### Requirement: Overview is the context's boundary

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewContextPageTests.A_context_row_opens_on_its_neighbours_with_the_boundary_counts_proof_and_flags_below, unit:dotnet:Backlog.UI.Components.UnitTests.DomainContextOverviewTests, unit:dotnet:Backlog.UI.Components.UnitTests.DomainContextViewTests.An_outside_answer_a_tile_a_gate_and_a_lens_each_follow_their_reference, unit:dotnet:Backlog.UI.Components.UnitTests.DomainContextViewTests.The_invariants_card_opens_its_units_Always_true_tab]
```

The system SHALL show, below a context's neighbours, an Overview of the context's subdomain, deployment and status, its name, and the prose above the first chapter of its `context.md` as a lead, an Inside card and an Outside card. Each question the Outside card asks links to the contexts its sentence says answer it. Below them the Overview shows its domain and feature chapters counted per kind, each count opening its first unit, and its requirements and invariants as two cards that say how many name a test. It also shows its chapters per status as one bar, its feature flags and settings with the features each gates, and the context's other pages with the question each answers.

#### Scenario: A context's Overview

- **Given** a context whose `context.md` holds the feature flag `github-integration` and whose `requirements.md` holds two requirements, one with `tests`
- **When** the reader opens the context
- **Then** the Overview stands below the neighbours, its Requirements card counts two requirements and says one of two name a test, Requirements is among its pages, and `github-integration` is among its switches

### Requirement: Underscore files stay hidden

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.Draws_no_row_for_invariants_dependencies_demo_or_underscore_files]
```

The system SHALL list no file or folder whose name starts with `_`.

#### Scenario: Generated renders

- **Given** a context holding an `_archify` folder
- **When** the reader opens the context in the Domain view
- **Then** no row names `_archify` or a file inside it

### Requirement: An unsplit domain picks its units in the page

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainUnitPageTests.An_unsplit_file_offers_its_units_in_a_chapter_bar_led_by_their_kind, unit:dotnet:Backlog.UI.Components.UnitTests.DomainUnitPageTests.A_split_file_opens_on_its_one_unit_without_a_bar, unit:dotnet:Backlog.UI.Components.UnitTests.DomainUnitsTests.The_chapter_bar_offers_aggregates_services_and_the_glossary_in_file_order]
```

The system SHALL offer an unsplit `domain.md`'s aggregates, domain services and Glossary as a chapter bar inside the Domain page.

#### Scenario: One domain file

- **Given** a context whose only domain file is `domain.md`, holding the aggregates *Knowledge Note* and *Remark* and the domain service *Cross-Linking*
- **When** the reader opens Domain
- **Then** the page's chapter bar offers *Knowledge Note*, *Remark*, *Cross-Linking* and Glossary, and picking *Remark* shows that aggregate

### Requirement: Wording is the chapters' own

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeaturesTests.A_summary_is_the_features_first_sentence, unit:dotnet:Backlog.UI.Components.UnitTests.DomainUnitPageTests.About_is_a_summary_of_the_chapter_not_its_prose, unit:dotnet:Backlog.UI.Components.UnitTests.DomainUnitSummaryTests.A_summary_leaves_a_closing_list_of_fields_to_model]
```

The system SHALL title each row and page with the heading as written, and summarize a unit with its chapter's first sentence, read as far as the colon when it closes on a list of fields.

#### Scenario: A summary

- **Given** a feature titled *Knowledge capture* whose first sentence is *Store notes as markdown.*
- **When** the reader opens Features
- **Then** the feature is listed as *Knowledge capture* with the summary *Store notes as markdown.*

### Requirement: Technical detail is on Model only

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainUnitPageTests.About_reads_cards_values_and_events_in_words_and_no_field, unit:dotnet:Backlog.UI.Components.UnitTests.DomainUnitPageTests.A_service_page_is_its_summary_and_never_how_it_is_invoked, unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelViewTests.Fields_groups_each_class_and_carries_the_notes_and_how_the_services_run]
```

The system SHALL show field types, invocation semantics and event payloads on the Model page only.

#### Scenario: An aggregate's About tab

- **Given** an aggregate whose `model.md` lists its fields with their types
- **When** the reader opens the aggregate's About tab
- **Then** the tab names no field type, and the Model page's Fields tab does

#### Scenario: A domain service's page

- **Given** a domain service whose chapter says it is called synchronously by the use case that completes the task
- **When** the reader opens the service in the chapter bar
- **Then** the page shows its first sentence and what it is held to, and says nothing of how it is invoked

### Requirement: Model draws the aggregate's boundary

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelViewTests.The_page_opens_on_Diagram_with_Fields_and_Events_beside_it, unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelViewTests.An_arranged_fence_draws_the_band_its_parts_references_and_services, unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelViewTests.A_fence_that_does_not_parse_is_drawn_with_mermaid, unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelArrangementTests.The_root_sits_in_its_band_with_its_composed_parts_and_their_multiplicity, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewModelPageTests.The_Model_row_opens_on_Diagram_with_the_aggregate_in_its_band, unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelViewTests.Diagram_is_headed_by_the_root_and_draws_its_parts_as_a_tree_around_its_card, unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelViewTests.All_fields_on_the_root_card_opens_Fields, unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelArrangementTests.Each_type_the_root_names_by_id_sits_outside_the_band_once, unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelArrangementTests.A_part_s_own_ids_are_the_part_s_detail_not_the_aggregate_s_references, unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelArrangementTests.An_id_points_at_the_chapter_whose_alias_or_heading_names_it, unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelArrangementTests.A_class_no_band_holds_is_drawn_outside_every_aggregate, unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelViewTests.A_class_no_aggregate_holds_is_a_card_outside_every_band, unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelArrangementTests.Every_model_in_this_repository_arranges]
```

The system SHALL open a `model.md` or `model.<name>.md` row on a Diagram tab, beside Fields and Events, that draws each aggregate root inside its consistency boundary with its key fields and its composed parts and their multiplicity, places outside the boundary one entry per type the root names by id, places the context's domain services below it, draws a class no aggregate holds as a card outside every boundary, and draws the diagram with mermaid only when it does not parse or names no aggregate root.

#### Scenario: An aggregate with parts and references

- **Given** a `model.md` whose `classDiagram` composes *SubItem* into the aggregate root *Task* as `"0..*"` and draws a dashed line from *Task* to *Task*
- **When** the reader opens Model
- **Then** Diagram is open, *SubItem* sits inside *Task*'s boundary marked `0..*`, and the dashed reference sits outside it

#### Scenario: Each type named by id once

- **Given** a root *Task* with a `List~Id~ depends_on` attribute and a dashed `depends on` line to *Task*, a `repo_ids` attribute, a dashed line to the enumeration *Readiness*, and a part *ProjectionRef* with its own `repo_id`
- **And** a *Repository* entity chapter whose `aliases` hold `repo_ids`
- **When** the reader opens Model
- **Then** outside the boundary sit *Task* by `depends_on` and *Repository* by `repo_ids`, once each, and neither *Readiness* nor *ProjectionRef*'s `repo_id`

#### Scenario: A class no aggregate holds

- **Given** a `model.md` whose `classDiagram` holds the aggregate root *Device*, the value object *OwnerScope* that no root composes, and *TaskChange* with no stereotype
- **When** the reader opens Model
- **Then** Diagram draws *Device* in its boundary and *OwnerScope* and *TaskChange* as cards outside every boundary, *TaskChange* marked as another context's type

#### Scenario: A diagram the view cannot read

- **Given** a `model.md` whose `classDiagram` names no aggregate root
- **When** the reader opens Model
- **Then** Diagram draws the fence with mermaid

### Requirement: Fields groups each class's attributes by kind

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelFieldsTests.Attributes_are_grouped_by_kind_in_the_tab_order, unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelViewTests.Fields_groups_each_class_and_carries_the_notes_and_how_the_services_run, unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelViewTests.Fields_reads_the_model_files_purpose_groups_with_meanings_and_rule_counts, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFieldBoardTests.A_fields_chapter_reads_into_purpose_groups_with_their_question_meanings_and_rule_links, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFieldBoardTests.The_rule_index_counts_the_invariants_under_each_heading, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewModelPageTests.Fields_and_Events_read_the_model_notes_and_the_contexts_domain_chapters]
```

The system SHALL head Model's Fields tab with the aggregate root's kind, its chapter's status and aliases, its name and its chapter's first sentence, and list the root's fields in the purpose groups the model file's `## Fields` chapter writes — each group with the question its first paragraph asks, each field with its type, its meaning and how many invariants its `Rules:` links name, fields no group names under *Other fields* — or, when the model file writes no `## Fields` chapter, grouped by what their types say: dates and times, ids pointing elsewhere, vocabularies and other values, in that order. Beside the groups it SHALL list what the root owns — its entities, value objects, enums with their values and the context's domain services — and beneath them the model file's relationship notes and each domain-service chapter's invocation semantics under *Domain services · how they run*. A fence with no aggregate root SHALL list each class that holds state with its attributes grouped by kind.

#### Scenario: A task's purpose groups

- **Given** a `model.md` whose `## Fields` chapter has a `### Work state` group asking *Where is the work?* with the bullet `` `status` — The recorded lifecycle state. Rules: `` linking two invariants of `domain.invariants.md`, and whose aggregate root *Task* also has `+Date due_on`
- **When** the reader opens Model and picks Fields
- **Then** *Work state* asks *Where is the work?*, *status* reads *Task Status*, *The recorded lifecycle state.* and *2 rules*, and *due_on* stands under *Other fields* as a *date*

#### Scenario: A task's fields with no Fields chapter

- **Given** a `model.md` with no `## Fields` chapter whose aggregate root *Task* has `+Date due_on`, `+List~Id~ depends_on`, `+TaskStatus status` for an enumeration *TaskStatus*, and `+String title`, and composes *SubItem* as `"0..*"`
- **When** the reader opens Model and picks Fields
- **Then** *due_on* is under Dates and times, *depends_on* under Ids pointing elsewhere, *status* under Vocabularies, *title* under Other values, and *SubItem* is under *Owned by the task* marked `0..*`

#### Scenario: How a service runs

- **Given** a `domain-service` chapter *Occurrence Spawning* whose prose says *Invocation semantics: called synchronously by the use case that completes the task.*
- **When** the reader opens Model and picks Fields
- **Then** *Domain services · how they run* names *Occurrence Spawning* with *Called synchronously by the use case that completes the task.*

### Requirement: Events reads each event as a contract

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelViewTests.Events_reads_each_event_as_a_contract, unit:dotnet:Backlog.UI.Components.UnitTests.DomainChapterContractsTests.A_domain_event_reads_as_a_contract, unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelViewTests.Events_says_what_the_context_listens_to, unit:dotnet:Backlog.UI.Components.UnitTests.DomainEventIndexTests.A_context_listens_to_the_published_languages_another_context_supplies, unit:dotnet:Backlog.UI.Components.UnitTests.DomainEventIndexTests.An_event_reads_when_it_fires_who_consumes_it_and_its_payload_as_rows, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewModelPageTests.Fields_and_Events_read_the_model_notes_and_the_contexts_domain_chapters]
```

The system SHALL head Model's Events tab *What ‹context› tells other contexts*, list each `domain-event` chapter of the context in an *All events* index with what raises it, when, its consumers and its state, and show the picked event's contract beneath — what raises it, its consumers, its payload and its published-language rules as the chapter writes them — the first event picked until the reader picks another. It SHALL mark an event *Emitted* only when the chapter that raises it says it emits or publishes it and no chapter says it is not emitted yet, and *Documented* otherwise. Under *What ‹context› listens to* it SHALL list each row of the context's `dependencies.md` in which another context supplies a published language.

#### Scenario: A documented event

- **Given** a `domain-event` chapter *StatusChanged* whose `related` names the aggregate *Task*, with *Payload* and *Consumers* sub-chapters, in a context whose chapters say it publishes no domain events yet
- **When** the reader opens Model and picks Events
- **Then** *StatusChanged* is raised by *Task*, shows its payload and consumers, and is marked *Documented*

### Requirement: The Features page opens on its title, lead and legend

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.The_page_title_names_the_context_and_the_lead_counts_its_features, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.Without_a_context_name_the_page_title_is_Features, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewFeaturesPageTests.The_Features_row_opens_the_board_in_columns_with_each_cards_proof]
```

The system SHALL open the Features page of a `features.md` or `features.<name>.md` row on a title naming its context, *What Tasks lets a person do* for Tasks (*Features* when the context has no name), a lead counting its features, and a legend for the flag or setting chip, the requirement tally and the *Proposed* mark.

#### Scenario: Opening the Features of Tasks

- **Given** a context *Tasks* whose `features.md` holds two features
- **When** the reader opens Features
- **Then** the page is titled *What Tasks lets a person do*, and its lead begins *2 features and their sub-features*

### Requirement: Features stand in columns by what they build on

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeaturesTests.Features_stand_in_columns_by_what_they_build_on_in_the_context, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeaturesTests.A_deprecated_feature_goes_to_the_end_of_its_column_and_a_proposed_one_says_so, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeaturesTests.A_card_counts_its_sub_features_and_names_its_flag_or_setting, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeaturesTests.A_card_counts_its_requirements_and_how_many_are_proved_from_their_tests, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeaturesTests.A_split_file_reads_its_one_feature_with_the_depth_the_base_file_gives_it, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.Cards_stand_in_columns_with_their_counts_flag_and_proof, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.A_proposed_card_is_dashed_and_deprecated_features_gather_at_the_end_of_the_last_column, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.A_column_left_with_only_deprecated_features_is_not_drawn, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.A_setting_chip_is_outlined_like_a_flag_and_names_its_kind_only_to_a_listener, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.A_card_shows_no_summary_and_no_tally_without_requirements, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.A_picked_proposed_card_carries_the_selected_class, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewFeaturesPageTests.The_Features_row_opens_the_board_in_columns_with_each_cards_proof, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewFeaturesPageTests.A_split_features_file_reads_its_one_feature_in_the_column_the_base_file_gives_it]
```

The system SHALL draw the features of a `features.md` or `features.<name>.md` row in columns by how many of the context's features each builds on through `depends-on`, each card showing its title, its sub-feature count, its flag or setting, *n req · m tested* when it has requirements and, past the first column, the features it comes after; a proposed feature dashed, a picked card edged in the brand colour even when proposed or hovered, and the deprecated features gathered, struck through, in one box at the end of the last column, a column they leave empty not drawn.

#### Scenario: Three levels

- **Given** a `features.md` whose *Checkout* depends on *Ordering*, whose *Quotes* depends on *Checkout*, and whose deprecated *Fax orders* depends on nothing
- **When** the reader opens Features
- **Then** *Ordering* stands in the first column, *Checkout* in the second and *Quotes* in the third, and *Fax orders* is struck through in the *Deprecated · 1* box at the end of the third

#### Scenario: A card's proof

- **Given** a feature *Checkout* behind the flag *New checkout*, with two sub-features, whose requirements group holds two requirements of which one names a test
- **When** the reader opens Features
- **Then** *Checkout*'s card says 2 sub-features, the flag *New checkout*, 2 req · 1 tested and after Ordering, and a feature with no requirements shows no tally

### Requirement: Picking a feature shows its detail

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeaturesTests.The_detail_names_what_it_builds_on_and_the_model_chapters_its_related_names, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.Picking_a_card_opens_its_detail_and_reports_the_pick, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.Following_a_chapter_in_the_detail_hands_its_reference_to_the_host, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.The_gate_reads_its_link_then_its_kind_as_separate_words, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.The_promises_pane_counts_the_requirements_and_bars_how_many_a_test_proves, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewFeaturesPageTests.A_features_requirements_link_opens_Requirements_on_that_features_group]
```

The system SHALL show, for the feature the reader picks on Features, what it builds on, the flag it is gated by or the setting it is configured by (*Always on* when neither), the model chapters its `related` names and its sub-features, each a link to the chapter it names, and beside them what it promises: its requirement count, a bar of how many a linked test proves, and a link that opens its group on Requirements.

#### Scenario: Picking Checkout

- **Given** a feature *Checkout* behind the flag *New checkout* that depends on *Ordering*, whose `related` names the aggregate *Order*, and whose requirements group holds two requirements of which one names a test
- **When** the reader picks *Checkout* on Features
- **Then** the detail names *Ordering* under Builds on, *New checkout · flag* under Gated by and *Order* under Model it uses, and promises 2 requirements, 1 of 2 linked to a test

### Requirement: Requirements read per feature with their proof

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_requirements_file_reads_one_group_per_feature_with_its_tally, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_requirement_reads_its_shall_sentence_and_whether_a_test_proves_it, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_scenario_reads_as_given_when_then_rows_with_an_and_clause_split_into_bullets, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.An_and_row_joins_the_row_above_it, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_clause_is_not_split_inside_a_code_span_or_emphasis, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_split_requirements_file_reads_its_one_feature, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.Requirements_written_straight_under_the_title_are_one_group, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.Requirements_read_per_feature_with_the_shall_sentence_and_proof, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.A_scenario_reads_as_rows_with_an_and_clause_as_bullets, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.Picking_another_feature_shows_its_requirements]
```

The system SHALL show on a `requirements.md` or `requirements.<name>.md` row, per feature, each requirement with its SHALL sentence set apart and whether a test its `tests` field names proves it, and each scenario as Given, When and Then rows, a row joining clauses with ", and" read as one bullet per clause.

#### Scenario: A proved requirement

- **Given** a requirement whose `tests` names one test and whose scenario reads *Then the payment is refused, and the order stays paid*
- **When** the reader opens Requirements
- **Then** the requirement is marked Test linked, and its Then row holds the bullets *the payment is refused* and *the order stays paid*

### Requirement: Requirements read as the design lays them out

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.The_page_opens_on_its_summary_and_sets_the_shall_word_apart, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.A_long_feature_opens_two_requirements_and_lists_the_rest_as_rows, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_shall_sentence_splits_around_its_first_shall_outside_code]
```

The system SHALL open the Requirements page on a card saying what a requirement is with how many the file holds and how many a test proves; list the features By feature · tested, each with its proved and total count and a bar; and draw each requirement as a card holding its title, a *Test linked* or dashed *No test linked* mark, its SHALL sentence with the word SHALL set apart, the tasks referencing it, and each scenario's GIVEN, WHEN and THEN rows on the card's foot. A feature with more than three requirements opens its first two and lists the rest as rows that each open in place, four of them before an *and n more* row.

#### Scenario: A feature with seven requirements

- **Given** a feature whose requirements file holds seven requirements
- **When** the reader opens it on Requirements
- **Then** the first two read as cards, four more as rows reading Show, and a last row reads *and 1 more in* the feature
- **And** pressing a row opens that requirement in place

### Requirement: A feature's rules show under it

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_sentence_explaining_invariants_gives_way_to_the_units_always_true_link, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_group_with_no_invariants_explained_or_aggregate_named_links_no_unit, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_sentence_naming_rules_no_unit_here_holds_stays, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.The_rules_in_place_replace_the_invariants_sentence_and_link_each_to_its_row, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.An_aggregate_can_be_opened_on_always_true, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewFeaturesPageTests.Requirements_read_as_rows_and_a_rule_shown_in_place_opens_its_row_on_Always_true, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_term_an_aggregates_rules_are_about_links_that_aggregate_with_its_rule_count, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_chapter_inside_an_aggregate_links_that_aggregate, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_chapter_inside_an_aggregate_with_no_rules_links_nothing, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.What_is_always_true_lists_the_rules_in_place_and_counts_them, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_rule_shown_in_place_opens_on_where_it_is_enforced_and_links_to_its_row_on_always_true, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_file_outside_the_domain_its_rules_are_about_links_the_aggregate]
```

The system SHALL show under a requirements group's heading, in place of its sentence explaining invariants, What is always true: the rules about what the group, its requirements, its feature or that sentence names — every rule of an aggregate it names, and the rules of a concept, term or file one of an aggregate's invariants groups is about — and, for a chapter inside an aggregate that no group is about, every rule of that aggregate. It counts the rules it shows, keeps the sentence where no rule of the context matches, and shows each rule as Always true marks it, opening on where it is enforced with a link to its row on Always true.

#### Scenario: The rules a task keeps

- **Given** a requirements group whose text says *The rules a task keeps are its invariants*, linking `domain.invariants.md#task`
- **When** the reader opens Requirements, opens a rule under What is always true and follows it to Always true
- **Then** the sentence is not shown, and the Domain page opens on *Task* with Always true picked and that rule's row marked

#### Scenario: A feature naming a term the rules are about

- **Given** a feature whose `related` names the term *My Day*, and a group of *Task*'s invariants whose `related` names it too
- **When** the reader opens the feature on Requirements
- **Then** What is always true reads the number of that group's rules *· on Domain* and lists them, each with its ✓, ○ or ? mark

### Requirement: A unit's rules show wherever they apply

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesInPlaceTests.The_rules_about_a_concept_are_those_its_enforced_at_line_or_its_group_names, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesInPlaceTests.The_rules_about_a_unit_are_every_rule_on_its_always_true, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesInPlaceTests.Each_rule_knows_its_row_on_always_true, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesInPlaceTests.A_list_draws_each_rule_as_always_true_does_and_shows_three_before_the_rest, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesInPlaceTests.A_list_one_past_the_limit_shows_every_rule, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesInPlaceTests.A_row_opens_on_where_the_rule_is_enforced_and_links_to_its_row, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesInPlaceTests.Always_true_marks_the_rule_a_link_opened_it_on, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesInPlaceTests.A_member_cards_rule_count_opens_its_rules_in_place_and_each_lands_on_always_true, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesInPlaceTests.The_host_can_open_a_unit_on_a_rule, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesInPlaceTests.A_domain_service_shows_what_is_always_true_of_the_units_it_touches, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesInPlaceTests.A_glossary_rows_rule_count_opens_its_rules_in_place, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesInPlaceTests.A_lifecycle_the_invariants_name_shows_its_rules_under_it_and_a_card_behind_its_count, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesInPlaceTests.A_fields_rule_count_opens_the_fields_rules_in_place_marked, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesInPlaceTests.A_fields_rule_no_invariants_file_holds_reads_as_its_heading_unmarked, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesInPlaceTests.A_requirements_link_to_one_rule_shows_that_rule_alone, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesInPlaceTests.A_parts_rule_count_on_the_diagram_opens_its_rules_in_place, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesInPlaceTests.A_features_detail_shows_what_is_always_true_of_what_it_uses]
```

The system SHALL show the rules about a thing on every page that draws it, in one list: on Features under a feature's promises, on Model behind a field's rule count on Fields and a part's on Diagram, on About behind a member card's rule count, on a domain service's page for the units its `related` names, on the Glossary behind a row's rule count, and on Flow under a lifecycle and behind a card's rule count — wherever a group's `related` or a rule's `Enforced at:` type names it. Each rule reads as its claim with its ✓, ○ or ? mark, opens in place on its first sentence and where it is enforced, and links to its row on its unit's Always true tab, which opens with that row marked and focused. A list shows three rules and the rest behind *n more*, or all four when there are four; Always true stays the full list.

#### Scenario: A field's rules on Model

- **Given** a model file whose `status` field links two of *Order*'s invariants, one named by a test and one `open`
- **When** the reader opens Fields and presses *2 rules* on `status`
- **Then** the two rules show under the row, marked ✓ and ?, and pressing one shows where it is enforced and *On Order's Always true →*

#### Scenario: A member's rules on About

- **Given** an *Order* aggregate owning *Order Line*, and two rules enforced at `OrderLine`
- **When** the reader presses *2 rules* on the *Order Line* card and follows the first rule
- **Then** the rules show on the card, and Always true opens with that rule's row marked

#### Scenario: A long list

- **Given** a thing five rules are about
- **When** the reader opens its rules
- **Then** three show, and *2 more rules* shows the other two

### Requirement: The context map reads as a map and as a list

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.ContextMapTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewContextMapPageTests]
```

The system SHALL offer the context map, under its file's heading and a sentence counting its contexts, as a Map tab that draws the contexts and their relations and a List tab that names the contexts in reading order. Map draws every bounded context of the map's Mermaid fence as a card on an ellipse, ordered to cross as few lines as it can, and one line per pair of contexts the fence joins, with a head at both ends when the pair points both ways. A line between two core contexts is amber and any other grey, and a line the fence draws `-.->` is dashed. Each card says whether its context is core or supporting and how many lines touch it, and no line runs beneath a card it does not join. Pointing at or focusing a card fades every line that does not touch it, and the card says it zooms in. A line's tooltip and accessible name are the "Why" of the matching row in the source context's dependency tables, or the other context's when the source keeps none. Picking a card opens that context's page. List names the core contexts and then the supporting ones, each group headed by its kind, its count and what the kind is for and each context with its "Why" from the Subdomain landscape table, then the published-languages table under the chapter's own headings with each contract named short — an event or type as the map spells it, a term in words, never its devbook path — and then each strategic rule as a card titled by its first sentence. No pattern name shows on either tab.

#### Scenario: Contracts by their short names

- **Given** a published-languages row whose contract cell is `.devbook/domain/capture/domain.md#itemcaptured`, and a fence edge labelled *ItemCaptured*
- **When** the reader opens Context map and picks List
- **Then** the row's contract reads `ItemCaptured`, opens that chapter when picked, and shows no path

#### Scenario: A rule as a card

- **Given** a strategic rule *Only the owning context defines a published language. Consumers conform to that contract.*
- **When** the reader opens List
- **Then** a card is titled *Only the owning context defines a published language* and reads *Consumers conform to that contract.* beneath it

#### Scenario: Switching to the list

- **Given** a domain folder with three bounded contexts
- **When** the reader opens Context map and picks List
- **Then** the three contexts are listed in reading order

#### Scenario: Reading a line and zooming to a context

- **Given** a context map whose fence draws `Inbox -->|OHS + Published Language| Tasks`, and an Inbox `dependencies.md` whose row for Tasks says why *A triaged item becomes a draft task*
- **When** the reader points at the Inbox card, then picks the Tasks card
- **Then** every line that does not touch Inbox fades, the Inbox–Tasks line reads *A triaged item becomes a draft task* and never *OHS*, and the Tasks page opens on its neighbours

### Requirement: Flow draws a lifecycle as one row of states

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainFlowsTests.Moves_between_neighbours_run_straight_forward_on_top_and_back_underneath_and_the_restore_loop_runs_outermost_above, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFlowsTests.A_way_back_over_a_state_runs_in_a_lane_below_the_row, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFlowsTests.A_state_box_reads_its_alias_its_description_and_its_class, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFlowsTests.An_event_in_a_label_is_a_chip_under_its_transition_dashed_until_it_is_emitted, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFlowsTests.A_way_in_from_several_states_under_one_label_is_one_note_not_an_arc_each, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFlowsTests.A_one_line_note_is_a_caption_under_its_state_and_a_start_into_the_first_state_is_not_drawn, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFlowsTests.The_row_scales_to_its_column_rather_than_scrolling, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFlowsTests.Marks_that_would_meet_under_the_row_take_separate_lines, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFlowsTests.The_first_chapter_titles_the_page_with_its_lead_and_a_legend_and_untyped_sub_chapters_read_as_cards, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFlowsTests.The_flow_page_draws_the_row_with_the_prose_beneath_and_mermaid_for_the_rest, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFlowsTests.A_diagram_the_row_cannot_hold_is_left_to_mermaid, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewFlowPageTests.The_Flow_row_draws_the_lifecycle_as_one_row_and_keeps_every_other_diagram]
```

The system SHALL draw each `stateDiagram-v2` of a `flow.md` or `flow.<name>.md` row as one row of states that fits the column without scrolling sideways: each state a box with its label and its description, a move to the next state on top and a move back to the one before underneath, every other forward move above the row and every other way back below it, the way from the last state back to the first as a loop round the outside above it, the event a transition raises as a chip under it that is dashed while the event is documented but not yet emitted and that opens Model, where the events are read, a way in from several states under one label as one note under the row, and a one-line note as a caption under its state. The first chapter SHALL title the page, a chapter's markdown before its first diagram SHALL be its lead, and a state row SHALL carry a legend. A chapter that draws no diagram and holds only untyped sub-chapters SHALL show them as cards. The system SHALL render every other Mermaid diagram of the file as it renders elsewhere.

#### Scenario: A lifecycle with a restore

- **Given** a `flow.md` chapter whose `stateDiagram-v2` runs *Draft* to *Ready* to *Done* and back from *Done* to *Draft* as *Restored*
- **When** the reader opens Flow
- **Then** *Draft*, *Ready* and *Done* stand in one row, *Restored* loops round the outside above it, and the chapter's prose follows the row

#### Scenario: An event and a drop

- **Given** a lifecycle whose *Ready* to *In progress* label reads *started (TaskProjected, not yet emitted)*, and whose *Draft*, *Ready* and *In progress* each go to *Archived* under one label
- **When** the reader opens Flow
- **Then** the move reads *started* with a dashed *TaskProjected* chip under it, and one note says *Also into Archived: Draft, Ready and In progress* in place of three arcs

#### Scenario: What sits beside the status

- **Given** a chapter *Beside the status, not states* holding `### The tick`, `### Readiness` and `### Time`
- **When** the reader opens Flow
- **Then** each of the three is a card with its title, its tag and its link

#### Scenario: A class diagram beside it

- **Given** the same `flow.md` holds a chapter with a `classDiagram`
- **When** the reader opens Flow
- **Then** that chapter's diagram renders as Mermaid, as it does on every other page

### Requirement: The Glossary lists terms and model chapters A to Z

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainGlossaryTests.Lists_every_term_and_model_chapter_of_the_context_a_to_z, unit:dotnet:Backlog.UI.Components.UnitTests.DomainGlossaryTests.Each_row_shows_its_kind_its_aliases_as_code_chips_and_its_first_sentence, unit:dotnet:Backlog.UI.Components.UnitTests.DomainGlossaryTests.A_deprecated_term_is_struck_through_and_says_so_in_words, unit:dotnet:Backlog.UI.Components.UnitTests.DomainGlossaryTests.A_proposed_term_carries_a_proposed_badge, unit:dotnet:Backlog.UI.Components.UnitTests.DomainGlossaryTests.The_filter_matches_a_heading_or_an_alias, unit:dotnet:Backlog.UI.Components.UnitTests.DomainGlossaryTests.The_glossary_opens_on_its_lead_and_a_labelled_find_box_hinting_at_aliases, unit:dotnet:Backlog.UI.Components.UnitTests.DomainGlossaryTests.The_show_pills_count_and_narrow_to_model_terms_or_borrowed, unit:dotnet:Backlog.UI.Components.UnitTests.DomainGlossaryTests.A_term_naming_its_own_heading_in_another_context_is_borrowed, unit:dotnet:Backlog.UI.Components.UnitTests.DomainGlossaryTests.A_long_list_shows_fifteen_and_the_rest_behind_more, unit:dotnet:Backlog.UI.Components.UnitTests.DomainGlossaryTests.Terms_that_name_each_other_are_easily_confused_as_one_card_and_a_one_way_pointer_is_not, unit:dotnet:Backlog.UI.Components.UnitTests.DomainGlossaryTests.Reached_from_the_domain_chapter_bar_it_lists_the_split_files_too]
```

The system SHALL offer, from the Domain page's chapter bar, a Glossary that lists every term chapter under `## Ubiquitous Language` and every model chapter of the context's domain files A to Z, each with its kind, a proposed or deprecated badge, its aliases as code chips and its first sentence; that filters by heading or alias and by one choice of All, Model, Terms or Borrowed, each with its count; that strikes a deprecated chapter through; that marks as borrowed, linking there, a term whose `related` names its own heading in another context; and that sets beside the list, as one Easily confused card per group, the term chapters that name each other in `related`.

#### Scenario: Filtering by an alias

- **Given** a term *Customer* with the alias `Client`
- **When** the reader types `client` into the Glossary's filter
- **Then** *Customer* is listed

#### Scenario: Two terms that name each other

- **Given** a term *Basket* whose `related` names *Draft Order*, and *Draft Order*'s names *Basket*
- **When** the reader opens the Glossary
- **Then** Easily confused sets *Basket* beside *Draft Order* in one card, each with its first sentence

#### Scenario: A borrowed word

- **Given** a term *Roadmap Tag* whose `related` names `.devbook/domain/roadmap/domain.md#roadmap-tag`
- **When** the reader presses Borrowed
- **Then** *Roadmap Tag* is listed, reading *borrowed from roadmap*, and following that opens the roadmap context's chapter

### Requirement: A unit, feature or term shows the Brain notes linking to it

```meta
type: requirement
status: draft
related: [.devbook/domain/brain/requirements.md#requirement-backlinks-reach-only-the-owner-and-write-nothing]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewBrainTaskLinksTests.A_unit_lists_the_notes_linking_to_it_or_to_a_chapter_inside_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewBrainTaskLinksTests.A_glossary_term_carries_its_notes_badge_and_box_and_a_term_with_none_carries_neither, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewBrainTaskLinksTests.A_model_part_counts_the_notes_linking_to_its_chapter, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewBrainTaskLinksTests.Links_to_a_whole_file_name_no_chapter_and_a_note_is_listed_once_per_chapter, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainBacklinkTests.A_repository_s_links_read_per_heading_once_per_note_and_heading]
```

The system SHALL show under each unit, feature and glossary term of the Domain view the From your Brain box for the owner's Brain notes linking to that chapter or to a chapter inside it, and on each model-diagram part and glossary term a teal **◆ n note** badge counting them, and draw neither where no note links.

#### Scenario: Notes on an aggregate and its entity

- **Given** a Brain note *Plan* linking to the aggregate *Task* and a note *Steps idea* linking to its entity *Sub Item*
- **When** the reader opens the Domain page on *Task*
- **Then** the box under *Task* lists *Plan* and *Steps idea*, and on Model *SubItem*'s part reads ◆ 1 note

#### Scenario: A term no note links to

- **Given** a Brain note linking to the term *Inbox* and none to the term *Today*
- **When** the reader opens the Glossary
- **Then** *Inbox* carries ◆ 1 note and its box, and *Today* carries neither

### Requirement: A feature or requirement shows the tasks naming it

```meta
type: requirement
status: draft
related: [.devbook/domain/brain/features.md#bi-directional-linking]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewBrainTaskLinksTests.A_feature_shows_the_tasks_naming_it_gold_or_dashed_and_the_notes_linking_to_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewBrainTaskLinksTests.A_requirement_shows_the_tasks_naming_it_and_nothing_on_the_view_is_teal_but_the_Brains, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewBrainTaskLinksTests.Links_to_a_whole_file_name_no_chapter_and_a_note_is_listed_once_per_chapter]
```

The system SHALL show on a feature's detail and on each requirement the tasks whose Devbook references name that chapter as task chips, gold when this backlog holds the task and dashed, reading *not in this backlog*, when it does not, and pressing a gold chip SHALL open the task.

#### Scenario: Two tasks on Capture

- **Given** a task this backlog holds and a task id it does not hold, both naming the feature *Capture* in their Devbook references
- **When** the reader picks *Capture* on Features
- **Then** the detail shows the held task as a gold chip reading its title and the other as a dashed chip reading *not in this backlog*, and pressing the gold chip opens the task
