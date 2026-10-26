# Devbook

```meta
status: draft
index: root
type: context
deployment: module
related: [.devbook/arc42/05-building-block-view.md#desktop-app]
```

Devbook is a personal project knowledge base, not a task queue. It reads two
kinds of devbook: the folders a repository carries beside its code, and the
person's own **Brain**, one devbook-shaped folder per user outside every
repository (local ADR 0026). Brain notes are Knowledge Notes, organized under the
areas the Brain's `CONVENTIONS.md` declares, with the PARA set (Projects, Areas,
Resources, Archive and the rest) as the default. Both kinds link with
[Tasks](../tasks/domain.md#task), so related context is easy to find later, and
a Brain note may link one way into a repository devbook.

Inside the boundary: Knowledge Notes and the Brain that holds them, reading a
repository's devbook folders in place, the reader's
[remarks](domain.md#remark) on chapters, instruction review, and retrieval across
all of them.

Outside it:

- a repository's folders themselves, which the repository owns and edits elsewhere;
- the Brain folder's sync between machines, which the folder's own sync product does;
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
related: [".devbook/domain/devbook/features.md#repository-devbook-areas", ".devbook/domain/devbook/features.md#area-selection-and-scope", ".devbook/domain/devbook/features.md#brain-mode-of-the-devbook-pane"]
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
related: [".devbook/domain/devbook/features.md#rendered-devbook-documents", ".devbook/domain/devbook/requirements.md#requirement-a-brain-note-is-always-editable"]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookScopeViewerSwitchesTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPanelsViewerSwitchesTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.AppFeatureSettingsStoreTests.The_devbook_viewer_switches_are_released_product_areas_that_start_off]
```

Turning it on offers Edit in the header of a repository devbook file, in the
architecture, domain, design, AI adoption and instruction areas. Edit swaps the
rendered chapter for its editor. Off, a chapter reads only and its header shows
no Edit control. Turning it off while an editor is open saves what was typed,
then closes the editor.

A Brain note is outside the switch: it is
[always editable](requirements.md#requirement-a-brain-note-is-always-editable),
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
today's atlas, and the header offers none. The technology atlas on the Tech tab
is the same either way.

It ships off, marked in development, while the Lobes atlas is finished. The key
is in `DevbookFeatures` and the feature catalog in `AppFeatures.cs`, so the
settings screen lists it with the other switches, and a person's choice is kept
in `features.json` like the pane's. It means nothing with Devbook off: the
context's scope service answers both the header and the pane from the two
switches together.

## Brain folder

```meta
status: draft
type: setting
key: brain.settings.json
scope: user
default: none
related: [".devbook/domain/devbook/features.md#brain-folder-setup", ".devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md", ".devbook/domain/devbook/requirements.md#requirement-a-folder-that-cannot-hold-the-brain-is-refused"]
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainSettingsStoreTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsBrainFolderTests, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainFolderWatchTests, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainFolderSourceTests, unit:dotnet:Backlog.HostComposition.UnitTests.MauiDesktopCompositionTests.The_maui_head_composes_the_brain_beside_the_repository_devbook]
```

The folder that holds the person's Brain, chosen on the Brain card in Settings ›
Storage. It ships with no value, because no path suits every person. Until one is
chosen, the Brain has no notes, the Brain chip still scopes tasks, and the Devbook
pane on the Brain asks for a folder.

| What the person enters | What happens |
| --- | --- |
| A folder outside every repository (a OneDrive folder is the expected one) | Kept as the Brain folder |
| A folder that does not exist yet | Created, then kept |
| A folder inside a repository's working tree, a relative path, or a file | Refused ([A folder that cannot hold the Brain is refused](requirements.md#requirement-a-folder-that-cannot-hold-the-brain-is-refused)) |
| Nothing (the field blanked) | The setting is cleared |

**Storage.** The path is kept in `brain.settings.json`, a file of its own beside
the workspace's `settings.json` in the app's per-user folder. The app has to know
the path before it can read the folder, which is why
[the path stays out of the folder](requirements.md#requirement-the-path-stays-out-of-the-folder).
It is not a key in `settings.json` either. That file is one decision, where
the backlog lives and where it is backed up, and moving the backlog never moves
the Brain. `BrainSettingsStore` is the file.

**Readers.** `IBrainSettings` in `Backlog.Modules.Devbook.Abstractions` is the port
every reader takes. `BrainSetup` holds the chip's two names and the link to the
card.

**Live reload.** The Brain's folder source reads the folder from the port on every
question, so a change shows without a restart. The Brain's folder watch follows it
too. It moves the database watch to the new folder, stops it when the setting is
cleared, and reloads an open Devbook pane.

**Database keying.** The Brain's database is keyed by the folder's absolute path.
Choosing another folder therefore builds another database, and the old one is left
behind.

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
