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

## New domain view

```meta
status: draft
type: feature-flag
key: devbook-domain-view
default: off
roadmap: [devbook-domain-view]
related: [".devbook/domain/devbook/features.md#domain-view-by-unit", ".devbook/arc42/adr/0036-the-domain-view-reads-by-unit.md", ".devbook/domain/devbook/requirements.md#requirement-the-flag-off-keeps-todays-domain-view"]
```

Turning it on replaces the Domain area of the Devbook with the
[Domain view by unit](features.md#domain-view-by-unit). The menu then lists a
bounded context's files as units, and each row opens a page drawn for the kind of
file it is (local ADR 0036). Turned off, the Domain area is today's view,
unchanged.

It ships off and marked `DEV` while the plan `+devbook-domain-view` builds the
new view. Only the Domain area reads it. The other areas of the Devbook, and the
[Devbook pane](#devbook-pane) switch that offers the pane at all, do not. The
settings screen lists it from the feature catalog in `AppFeatures.cs`, and a
person's choice is kept in `features.json` beside the app's other per-device
choices.

While the plan builds it, the view is a frame: each menu row opens a placeholder
that names the page its kind of file will get, and the plan's later steps
replace the placeholders one page at a time.

Model is the first page built. It opens on **Diagram**, which draws each
`classDiagram` of the model file as the aggregate's consistency boundary: the
root and its composed parts inside the band, what it holds by identity outside
it, and the domain services below. A diagram that cannot be read that way, has
no aggregate root, or holds structure no boundary shows is drawn with mermaid as
before. **Fields** lists each class's attributes grouped by kind, with the
model's relationship notes beneath and how each domain service runs, read from
its chapter's invocation semantics. **Events** reads each `domain-event` chapter
as a contract: what raises it, who consumes it, its payload and its
published-language rules, and whether the chapters say it is emitted or only
documented.

Domain is the second. An unsplit `domain.md` offers its units in a chapter bar,
a split file opens on its one aggregate, and an aggregate reads as **About**
beside **Always true**, which is still a placeholder; see
[Domain view by unit](features.md#domain-view-by-unit).

The flag is retired when the old Domain view is removed, which a later plan does.
The new view then always shows.
