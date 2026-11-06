# Devbook

```meta
index: root
type: context
deployment: module
related: [.devbook/arc42/05-building-block-view.md#desktop-app, .devbook/arc42/adr/0040-the-brain-is-its-own-bounded-context.md]
```

Devbook reads the devbook folders a repository carries beside its code: their
Markdown and `meta` blocks in place. It builds their database, renders their
chapters and searches them. It supplies the same machinery to the
[Brain](../brain/context.md), the person's own devbook-shaped folder and a
bounded context of its own (local ADR 0040).

Inside the boundary:

- reading a repository's devbook folders in place;
- the devbook database and its builder;
- the chapter viewer and the Devbook pane;
- the reader's [remarks](domain.md#remark) on chapters;
- the [workflow state](domain.md#subject-state) of a change proposal, a decision
  record and a debt record;
- the planning fields every devbook chapter may carry;
- instruction review;
- retrieval across every devbook the app reads.

Outside it:

- a repository's folders themselves, which the repository owns and edits elsewhere;
- the Brain's notes, areas, conventions, capture and links, which the
  [Brain](../brain/context.md) owns and reads through this context's machinery;
- the [Lobes atlas](../brain/features.md#lobes-atlas), which the Brain owns and
  the pane's **Atlas** button opens while its `atlas-lobes` flag is on: it reads
  every registered repository's devbook database through this context's reader;
- the header scope that picks between the Brain and the repositories, which the
  desktop shell owns and this context only follows;
- the work items a chapter links to, which [Tasks](../tasks/domain.md#task) answers;
- gathering chapters under planned work, which
  [Roadmap Planning](../roadmap/domain.md#roadmap-item) answers.

## Devbook pane

```meta
type: feature-flag
key: repository-devbook
default: on
related: [".devbook/domain/devbook/features.md#repository-devbook-areas", ".devbook/domain/devbook/features.md#area-selection-and-scope", ".devbook/domain/brain/features.md#brain-mode-of-the-devbook-pane"]
```

Turning on `repository-devbook` offers the Devbook, which takes the whole screen in place of the
main view. The pane shows the repository the scope's anchor names, with these
sections:

- design;
- architecture;
- domain;
- technology;
- AI adoption;
- instructions.

The pane shows the Brain when the anchor is Brain or nothing is pressed. The Brain
mode has no switch of its own (local ADR 0027). Which sections show, by scope, is
in [Area selection and scope](features.md#area-selection-and-scope).

The Shell reads the switch to decide whether to offer the pane, and this context's
scope service reads it to decide which sections to show. The flag is released and
on by default.

The catalog also reads three former keys: `repository-knowledge`,
`devbook-sections` and `knowledge-sections`. Turning any of them off in
`features.json` keeps Devbook off. The settings screen lists the switch from the
catalog in `AppFeatures.cs`, and the choice is kept in `features.json` beside the
app's other per-device choices.

## C4 zoom

```meta
type: feature-flag
key: c4-zoom
default: off
related: [".devbook/domain/devbook/features.md#c4-zoom-navigation", ".devbook/arc42/adr/0046-c4-zoom-navigation-is-a-second-tab-read-from-devbook-meta.md"]
```

Turning it on adds a **C4 zoom** tab beside the **C4** tab in the architecture
panel. An ADR, a TDR or an arc42 chapter in the **Chapters** tab then shows where
it lands in the C4 model. It needs `c4-diagrams` on as well, because the model it
walks is the one that switch shows. With `c4-diagrams` off the panel offers no zoom
tab, whatever this switch says. The **C4** tab is the same with this switch on or
off.

The plan `+c4-zoom-navigation` owns the rollout. It ships off by default, and the
switch is retired once the zoom tab has been validated and replaces the **C4** tab
(local ADR 0046).

## Devbook editing

```meta
type: feature-flag
key: devbook-editing
default: off
related: [".devbook/domain/devbook/features.md#rendered-devbook-documents", ".devbook/domain/brain/requirements.md#requirement-a-brain-note-is-always-editable"]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookScopeViewerSwitchesTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPanelsViewerSwitchesTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.AppFeatureSettingsStoreTests.The_devbook_viewer_switches_are_released_product_areas_that_start_off]
```

Turning it on offers Edit in the header of a repository devbook file, in the
architecture, domain, design, AI adoption and instruction areas. Edit swaps the
rendered chapter for its editor. Off, a chapter reads only and its header shows
no Edit control. Turning it off while an editor is open saves what was typed,
then closes the editor.

A Brain note is outside the switch: it is
[always editable](../brain/requirements.md#requirement-a-brain-note-is-always-editable),
whatever the switch says. The switch is therefore the repository viewer's, not
Devbook-wide.

The `devbook-editing` flag is released and off by default while the owner weighs
removing repository editing altogether. It is retired either way: the key goes when Edit is
removed, or when Edit stays and is simply there. It has its own key, apart from
[Devbook status](#devbook-status), because the two may be kept or removed
separately. The scope service answers whether Edit is offered, and each panel
follows a change live, with no restart.

## Devbook status

```meta
type: feature-flag
key: devbook-status
default: off
related: [".devbook/domain/devbook/features.md#rendered-devbook-documents", ".devbook/domain/devbook/requirements.md#requirement-decision-state-is-shown-beside-the-status", ".devbook/domain/devbook/requirements.md#requirement-a-records-state-is-shown-from-the-apps-store", ".devbook/design/content-editing.md#knowledge-metadata-blocks"]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookScopeViewerSwitchesTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPanelsViewerSwitchesTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainStatusSwitchTests, unit:dotnet:Backlog.UI.Components.UnitTests.MetadataStatusHiddenTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.AppFeatureSettingsStoreTests.The_devbook_viewer_switches_are_released_product_areas_that_start_off, unit:dotnet:Backlog.Desktop.UI.UnitTests.Arc42DevbookPanelTests.With_the_status_display_off_no_record_state_is_drawn]
```

Turning it on shows the status every devbook file and chapter
states. That is the selector or badge beside the file's name and each heading, and
the decision state that qualifies it. It covers every area, the technology
ratings and Brain notes included. It also shows the workflow state the app keeps
for a decision or debt record ([Subject State](domain.md#subject-state)), with who
set it and when.

Off, no status, decision state or record state is drawn there,
and neither are the findings about them. A record's state is still kept, and an
assistant can still read and set it over MCP. A chapter's other fields, such
as `related`, are still drawn, and so is a unit's sync direction, which the app
keeps in its direction map. A heading over an otherwise empty `meta` fence still
renders.

The flag switches the file and chapter surfaces only. The AI loop's usage pills
and the atlas badges read a chapter's status as a summary shown elsewhere, and are
not behind it.

The `devbook-status` flag is released and off by default while the owner weighs
removing the status display, and is retired when that is decided. It has its own key, apart from
[Devbook editing](#devbook-editing), because a status is read where the reader
cannot change it, so it can stay or go apart from Edit. Each panel follows a
change live, with no restart.

## Instruction optimization

```meta
type: feature-flag
key: instruction-optimization
related: [".devbook/domain/devbook/features.md#instruction-optimization"]
```

The `instruction-optimization` flag is reserved for the instruction review of the
same name. It is not in the
feature catalog yet: no code checks the key, so there is no default and nothing to
switch. The chapter exists so the feature has its gate before it is built. When the review ships, the key lands in `DevbookFeatures` and in the
catalog, and this chapter gains its `default`.
