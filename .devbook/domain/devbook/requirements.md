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

## C4 zoom navigation

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#c4-zoom-navigation]
```

> The requirements of the **C4 zoom** tab of the architecture panel, behind the
> `c4-zoom` switch (local ADR 0042). Each has its cases written inline until a
> journey proves it.

### Requirement: The zoom tab sits beside the C4 tab

```meta
type: requirement
status: draft
```

The system SHALL offer a **C4 zoom** tab beside an unchanged **C4** tab in the architecture panel only while both `c4-zoom` and `c4-diagrams` are on, open it on the System context view, and show a dismissable hint naming the ways to move the first time it opens.

#### Scenario: Both switches on

- **Given** `c4-diagrams` and `c4-zoom` are on and the repository has a C4 model
- **When** the reader opens the architecture panel
- **Then** the tabs read Chapters, C4 and C4 zoom, and the C4 tab shows what it showed with `c4-zoom` off

#### Scenario: Only the zoom switch on

- **Given** `c4-zoom` is on and `c4-diagrams` is off
- **When** the reader opens the architecture panel
- **Then** neither the C4 tab nor the C4 zoom tab is offered

#### Scenario: First opening

- **Given** the reader has never opened the C4 zoom tab
- **When** they open it
- **Then** it shows the System context view and a hint naming zoom in, zoom out, jump and sideways

#### Scenario: The hint once dismissed

- **Given** the reader dismissed the hint
- **When** they open the C4 zoom tab again
- **Then** the System context view shows with no hint

### Requirement: The zoom bar names each level's element

```meta
type: requirement
status: draft
```

The system SHALL head the zoom tab with a zoom bar of the steps 1 Context, 2 Containers, 3 Components and 4 Code, each showing the element its level is about, a deeper step greyed with "pick a container" or "pick a component" until an element filling it is selected, and Landscape, Dynamic and Deployment beside it under "Also".

#### Scenario: On the context view

- **Given** the zoom tab shows the System context view of Prompt Backlog
- **When** nothing is selected
- **Then** the bar reads 1 Context Prompt Backlog, 2 Containers Prompt Backlog, 3 Components greyed "pick a container" and 4 Code greyed "pick a component"

#### Scenario: A container selected

- **Given** the zoom tab shows the Containers view
- **When** the reader selects Desktop App
- **Then** step 3 reads Components Desktop App and can be picked, and step 4 still reads "pick a component"

#### Scenario: The supplementary views

- **Given** the zoom tab is open on any level
- **When** the reader looks beside the zoom bar
- **Then** Landscape, Dynamic and Deployment are offered under "Also" and none of them is a numbered step

### Requirement: Zooming moves one level and keeps the selection

```meta
type: requirement
status: draft
```

The system SHALL zoom into a drillable element on a double-click, on its "Zoom in" chip, or on Enter while it has focus, and zoom out on an earlier zoom bar step or on Backspace while the diagram has focus, keeping the element zoomed out of selected and taking neither key outside the focused diagram.

#### Scenario: Zoom in by double-click

- **Given** the System context view shows Prompt Backlog with a Zoom in chip
- **When** the reader double-clicks Prompt Backlog
- **Then** the Containers view of Prompt Backlog opens

#### Scenario: Zoom in by keyboard

- **Given** Desktop App has focus on the Containers view
- **When** the reader presses Enter
- **Then** the Components view of Desktop App opens

#### Scenario: Zoom out keeps the selection

- **Given** the Components view of Desktop App is open
- **When** the reader presses Backspace with the diagram focused
- **Then** the Containers view opens with Desktop App selected

#### Scenario: Zoom out from the bar

- **Given** the Code view of the Devbook component is open
- **When** the reader picks step 2 Containers in the zoom bar
- **Then** the Containers view opens with Desktop App selected

#### Scenario: Keys outside the diagram

- **Given** the zoom tab is open and the find field has focus
- **When** the reader presses Backspace or Enter
- **Then** the field takes the key and the view does not change

#### Scenario: An element with no deeper view

- **Given** the Containers view shows Cloud Database, which has no components view
- **When** the reader double-clicks it
- **Then** it is selected, no view opens, and it shows no Zoom in chip

### Requirement: A selected element opens its inspector

```meta
type: requirement
status: draft
```

The system SHALL show, for the selected element, its kind, description and technology, the arc42 chapters that document a view showing it, the ADRs and the TDRs that name it — a proposed ADR marked as proposed, a TDR with its severity and lifecycle, each opening in the Chapters tab — and a zoom-in button only when a deeper view of it exists.

#### Scenario: A container with records

- **Given** the Containers view is open
- **When** the reader selects Desktop App
- **Then** the inspector reads Container, its description and .NET MAUI Blazor Hybrid, lists the arc42 chapters, ADRs and TDRs that name it, and offers to open its components

#### Scenario: An element with nothing deeper

- **Given** the Containers view is open
- **When** the reader selects Cloud Database
- **Then** the inspector shows its kind, description and technology and offers no zoom-in button

#### Scenario: An element no record names

- **Given** the Containers view is open and no ADR or TDR names Cloud Database
- **When** the reader selects Cloud Database
- **Then** the Decisions and Technical debt sections each say that nothing names it

#### Scenario: Opening a record

- **Given** Desktop App is selected and its inspector lists a proposed ADR
- **When** the reader picks that ADR
- **Then** the Chapters tab opens on the ADR

### Requirement: An overlay counts the records on each card

```meta
type: requirement
status: draft
```

The system SHALL offer an overlay switch of None, Decisions and Debt that puts the count of ADRs, or of open TDRs, naming an element on its card, draws a card that open TDRs name on the error surface, counts under the diagram the records that name only elements below the view, and shows the open TDRs that name no element in a cross-cutting tray while Debt is chosen.

#### Scenario: Decisions

- **Given** the Containers view is open with the overlay at None
- **When** the reader chooses Decisions
- **Then** each card named by an ADR shows its count of ADRs, and no card shows a TDR count

#### Scenario: Debt with a cross-cutting record

- **Given** one TDR names the Devbook component and two TDRs name no element
- **When** the reader chooses Debt on the Components view of Desktop App
- **Then** the Devbook card reads 1 TDR on the error surface and the cross-cutting tray lists the other two

#### Scenario: Records below the view

- **Given** one ADR names only the Shell component of Desktop App
- **When** the reader chooses Decisions on the Containers view
- **Then** no card counts that ADR, and the line under the diagram says 1 ADR links only to elements below it

### Requirement: Code is drawn from the source on demand

```meta
type: requirement
status: draft
```

The system SHALL draw the Code level of a component from the source its code link names when it is opened, showing the types and their members, and never from an authored diagram.

#### Scenario: A component with a code link

- **Given** the Devbook component's code link names `Backlog.Infrastructure.Devbook`
- **When** the reader zooms into Devbook
- **Then** the Code view lists that project's types with their members, read from the source as it is now

#### Scenario: The source changed

- **Given** a type was added to `Backlog.Infrastructure.Devbook` since the Code view was last opened
- **When** the reader opens the Code view of Devbook again
- **Then** the new type is shown

#### Scenario: A component with no code link

- **Given** the Inbox component has no code link
- **When** the reader selects it
- **Then** step 4 stays greyed and Inbox shows no Zoom in chip

### Requirement: The supplementary views move in their own way

```meta
type: requirement
status: draft
```

The system SHALL play a dynamic view one numbered step at a time, switch a deployment view between its environments behind one tab list with another environment drawn as a dashed door into its own view, show a selected deployment node's description, technology and the container instances it runs, each opening the container view, and draw the landscape's DSL groups as bands.

#### Scenario: Stepping a dynamic view

- **Given** the dynamic view "Capture → issue" shows step 3 of 5
- **When** the reader steps forward
- **Then** step 4 is drawn as the current step and the other steps are drawn as not current

#### Scenario: Another environment

- **Given** the Local machine deployment view is open
- **When** the reader opens the dashed Azure door
- **Then** the deployment view switches to the Azure environment

#### Scenario: A deployment node

- **Given** the Local machine deployment view is open
- **When** the reader selects the MSIX package node
- **Then** the inspector shows its description and MSIX, lists Desktop App as the container instance it runs, and Show in containers opens the Containers view with Desktop App selected

#### Scenario: Landscape groups

- **Given** the DSL groups its external systems as Capture sources, Work tracking and Operations
- **When** the reader opens Landscape
- **Then** each group is drawn as a band holding its systems

### Requirement: Records and chapters show where they land in the model

```meta
type: requirement
status: draft
```

The system SHALL, while `c4-zoom` is on, show for an ADR or a TDR opened in the Chapters tab where it lands on the container view and its decision chain, and for an arc42 chapter the views it documents with the elements present in the model but not in its mermaid fence, and those in the fence but not in the model, flagged.

#### Scenario: A decision record

- **Given** `c4-zoom` is on and ADR 0005 names Cloud Service and Cloud Database
- **When** the reader opens ADR 0005 in the Chapters tab
- **Then** a container view marks the elements it decides and touches, and its chain shows what it builds on and what builds on it

#### Scenario: A chapter and the model disagree

- **Given** `c4-zoom` is on and the fence in 05 Building block view draws Attachments, which the model lacks
- **When** the reader opens chapter 05 in the Chapters tab
- **Then** the chapter embeds the Containers view it documents and flags Attachments as in the fence but not in the model

#### Scenario: The switch off

- **Given** `c4-zoom` is off and `c4-diagrams` is on
- **When** the reader opens ADR 0005 in the Chapters tab
- **Then** the record shows no container view and no decision chain

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
