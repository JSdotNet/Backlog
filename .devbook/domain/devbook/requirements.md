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

> The requirements of how a chapter's metadata and workflow state are shown: the
> decision state a domain chapter carries, the state the app keeps for a decision
> or debt record and a proposal, and the state another tool keeps on a chapter.

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

### Requirement: A record's state is shown from the app's store

```meta
type: requirement
status: draft
related: [.devbook/domain/devbook/domain.md#subject-state, .devbook/arc42/adr/0044-devbook-subject-state-lives-in-backlogs-own-store.md]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.Arc42DevbookPanelTests.A_decision_records_state_is_drawn_read_only_from_the_store, unit:dotnet:Backlog.Desktop.UI.UnitTests.Arc42DevbookPanelTests.A_state_that_arrives_while_the_record_is_open_is_drawn, unit:dotnet:Backlog.Desktop.UI.UnitTests.Arc42DevbookPanelTests.With_the_status_display_off_no_record_state_is_drawn, unit:dotnet:Backlog.Desktop.UI.UnitTests.Arc42DevbookPanelTests.The_authored_status_reads_as_a_badge_and_nothing_writes_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPanelsReadOnlyBranchTests.The_arc42_panel_sets_a_debt_records_state_on_a_branch_and_writes_no_file, unit:dotnet:Backlog.UI.Components.UnitTests.MetadataSubjectStateTests.The_line_states_the_kind_who_and_when, unit:dotnet:Backlog.UI.Components.UnitTests.MetadataSubjectStateTests.A_subject_with_options_is_offered_where_the_status_cannot_be_edited, unit:dotnet:Backlog.UI.Components.UnitTests.MetadataSubjectStateTests.A_subject_with_no_options_is_shown_and_not_offered, unit:dotnet:Backlog.UI.Components.UnitTests.MetadataSubjectStateTests.With_the_status_hidden_the_subject_state_is_not_drawn]
```

While [Devbook status](context.md#devbook-status) is on, the system SHALL show a decision or debt record's state from the app's own store beside the record's name, saying who set it and when.

#### Scenario: A debt record on a branch

- **Given** Devbook status is on, and a debt record under `arc42/tdr/` read from a branch snapshot
- **When** the reader picks `planned` as its state
- **Then** the record shows `planned` and *debt state set by You* on that day, and no file of the snapshot changes

#### Scenario: A decision record

- **Given** Devbook status is on, and an assistant set a decision record to `proposed` in one pull request and to `decided` in a later one
- **When** the reader opens the record
- **Then** it shows `decided` with who set it and when, and offers no state to pick

#### Scenario: Status switched off

- **Given** Devbook status is off, and a debt record whose state is `planned`
- **When** the reader opens it
- **Then** no state is drawn beside its name, and the store still keeps `planned`

### Requirement: Leaving a decision step takes its record with it

```meta
type: requirement
status: draft
related: [.devbook/domain/devbook/domain.invariants.md#invariant-leaving-a-decision-step-withdraws-the-decisions-above-it]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSubjectRulesTests.Setting_a_proposal_back_to_draft_withdraws_approval_and_acceptance, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSubjectRulesTests.Approving_again_withdraws_an_acceptance_at_the_old_fingerprint]
```

The system SHALL withdraw a proposal's approval and acceptance when it is set back to draft, and its acceptance when it is approved again.

#### Scenario: Approved again

- **Given** a proposal that is accepted, with its approval and acceptance, in the app's store
- **When** an assistant approves it again at the change's new fingerprint
- **Then** the proposal is approved at that fingerprint and no longer carries an acceptance

#### Scenario: Back to draft

- **Given** a proposal that is accepted, with its approval and acceptance, in the app's store
- **When** an assistant sets its state to `draft`
- **Then** the proposal carries neither its approval nor its acceptance

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
