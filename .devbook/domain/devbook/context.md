# Devbook

```meta
status: draft
index: root
type: context
deployment: module
related: [.devbook/arc42/05-building-block-view.md#desktop-app, .devbook/arc42/adr/0040-the-brain-is-its-own-bounded-context.md]
```

Devbook reads the devbook folders a repository carries beside its code: it
reads their Markdown and `meta` blocks in place, builds their database, renders
their chapters, and searches them. It supplies the same reading machinery to the
[Brain](../brain/context.md), the person's own devbook-shaped folder, which is a
bounded context of its own (local ADR 0040).

Inside the boundary: reading a repository's devbook folders in place, the
devbook database and its builder, the chapter viewer and the Devbook pane, the
reader's [remarks](domain.md#remark) on chapters, the planning fields every
devbook chapter may carry, instruction review, and retrieval across every
devbook the app reads.

Outside it:

- a repository's folders themselves, which the repository owns and edits elsewhere;
- the Brain's notes, areas, conventions, capture and links, which the
  [Brain](../brain/context.md) owns and reads through this context's machinery;
- the header scope that picks between the Brain and the repositories, which the
  desktop shell owns and this context only follows;
- the work items a chapter links to, which [Tasks](../tasks/domain.md#task) answers;
- gathering chapters under planned work, which
  [Roadmap Planning](../roadmap/domain.md#roadmap-item) answers.

## Devbook pane

```meta
status: draft
type: feature-flag
key: repository-devbook
default: on
related: [".devbook/domain/devbook/features.md#repository-devbook-areas", ".devbook/domain/devbook/features.md#area-selection-and-scope", ".devbook/domain/brain/features.md#brain-mode-of-the-devbook-pane"]
```

Turning it on offers the Devbook, which takes the whole screen in place of the
main view. The pane shows the repository the
scope's anchor names, with its design, architecture, domain, technology, AI
adoption, and instruction sections. It shows the Brain when the anchor is Brain or
nothing is pressed. The Brain mode has no switch of its own: it ships with the pane
(local ADR 0027). Which of these the pane shows, by scope, is in
[Area selection and scope](features.md#area-selection-and-scope).

The Shell reads the switch to decide whether to offer the pane at all. This
context's own scope service reads it to decide which sections to show. It is
released and on by default.

The catalog also reads three former keys: `repository-knowledge`,
`devbook-sections` and `knowledge-sections`. Turning any of them off in
`features.json` keeps Devbook off. The settings screen lists the switch from the
feature catalog in `AppFeatures.cs`, and a person's choice is kept in
`features.json` beside the app's other per-device choices.

## Devbook editing

```meta
status: draft
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

It is released and off by default while the owner weighs removing repository
editing altogether. It is retired either way: the key goes when Edit is removed,
or when Edit stays and is simply there. It has its own key rather than sharing
one with [Devbook status](#devbook-status), because the two may be kept or
removed apart. This context's scope service answers whether Edit is offered, and
each panel follows a change live, with no restart.

## Devbook status

```meta
status: draft
type: feature-flag
key: devbook-status
default: off
related: [".devbook/domain/devbook/features.md#rendered-devbook-documents", ".devbook/domain/devbook/requirements.md#requirement-decision-state-is-shown-beside-the-status", ".devbook/design/content-editing.md#knowledge-metadata-blocks"]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookScopeViewerSwitchesTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPanelsViewerSwitchesTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainStatusSwitchTests, unit:dotnet:Backlog.UI.Components.UnitTests.MetadataStatusHiddenTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.AppFeatureSettingsStoreTests.The_devbook_viewer_switches_are_released_product_areas_that_start_off]
```

Turning it on shows the status every devbook file and each of its chapters
state: the selector or badge beside the file's name and beside each heading, and
the decision state that qualifies it. It covers every area, the technology
ratings and Brain notes included. Off, no status or decision state is drawn
there, and neither are the findings about them. A chapter's other fields, such
as `related` and `sync`, are still drawn, and a heading over an otherwise empty
`meta` fence still renders.

It switches the file and chapter surfaces only. The AI loop's usage pills and the
atlas badges read a chapter's status as a summary shown somewhere else, and are
not behind it.

It is released and off by default while the owner weighs removing the status
display, and it is retired when that is decided. It has its own key rather than
sharing one with [Devbook editing](#devbook-editing): a status is read, not
written, wherever the reader cannot change it, so it can stay or go apart from
Edit. Each panel follows a change live, with no restart.

## Lobes atlas

```meta
status: draft
type: feature-flag
key: atlas-lobes
default: off
related: [".devbook/domain/devbook/features.md#lobes-atlas", ".devbook/domain/devbook/requirements.md#lobes-atlas"]
```

Turning it on makes the Devbook pane's **Atlas** button open the
[Lobes atlas](features.md#lobes-atlas) at the pane's scope, the Brain or that
repository, and offers the atlas from the shell header. Off, the button opens
today's atlas, and the header offers none. The Technology section keeps its own
views either way.

It ships off, marked in development, while the Lobes atlas is finished. The key
is in `DevbookFeatures` and the feature catalog in `AppFeatures.cs`, so the
settings screen lists it with the other switches, and a person's choice is kept
in `features.json` like the pane's. It means nothing with Devbook off: the
context's scope service answers both the header and the pane from the two
switches together.

## Instruction optimization

```meta
status: draft
type: feature-flag
key: instruction-optimization
related: [".devbook/domain/devbook/features.md#instruction-optimization"]
```

Reserved for the instruction review the feature it gates describes. It is not in
the feature catalog yet: no code checks the key, so there is no default to state
and nothing to switch. The chapter exists so the feature has its gate before the
first line of it is built. When the review ships, the key lands in
`DevbookFeatures` and in the catalog, and this chapter gains its `default`.

## New domain view

```meta
status: draft
type: feature-flag
key: devbook-domain-view
default: off
roadmap: [devbook-domain-view]
related: [".devbook/domain/devbook/features.md#domain-view-by-unit", ".devbook/arc42/adr/0044-the-domain-view-reads-by-unit.md", ".devbook/domain/devbook/requirements.md#requirement-the-flag-off-keeps-todays-domain-view"]
```

Turning it on replaces the Domain area of the Devbook with the
[Domain view by unit](features.md#domain-view-by-unit). The menu then lists a
bounded context's files as units, and each row opens a page drawn for the kind of
file it is (local ADR 0044). Turned off, the Domain area is today's view,
unchanged.

It ships off and marked `DEV` until the new view is accepted. Only the Domain area reads it. The other areas of the Devbook, and the
[Devbook pane](#devbook-pane) switch that offers the pane at all, do not. The
settings screen lists it from the feature catalog in `AppFeatures.cs`, and a
person's choice is kept in `features.json` beside the app's other per-device
choices.

The plan has built every page: the Context map, a context's page with its
neighbours and its **Overview**, Domain, Features, Requirements, Model and Flow, with the Brain
notes and tasks that link to each. Only the **Demo** button for a click demo is
still to come, and a file of no known kind opens a placeholder.

Model opens on **Diagram**, headed by the root's name, which draws each
`classDiagram` of the model file as the aggregate's consistency boundary: the
root card with its key fields and a link to Fields in the middle of the band,
its composed parts as a tree joined to it above and below, and outside the band
one card per type the root names by id — the class a dashed line reaches, or
the aggregate or entity chapter whose alias or heading the id attribute spells.
The context's domain-service chapters sit below it, and a class no
aggregate holds is a card outside every boundary. A diagram that does not parse
or has no aggregate root is drawn with mermaid as before. **Fields** lists each
class's attributes grouped by kind, with the model's relationship notes beneath and how each domain service runs, read from
its chapter's invocation semantics. **Events** reads each `domain-event` chapter
as a contract: what raises it, who consumes it, its payload and its
published-language rules, and whether the chapters say it is emitted or only
documented.

On Domain, an unsplit `domain.md` offers its units in a chapter bar, a split
file opens on its one aggregate, and an aggregate reads as **About** beside
**Always true**, its rules read from the invariants file beside the page; see
[Domain view by unit](features.md#domain-view-by-unit).

Flow draws each lifecycle as one row of states, and the Domain page's Glossary
lists the context's terms and model chapters A to Z.

The flag is retired when the old Domain view is removed, which a later plan does.
The new view then always shows.
