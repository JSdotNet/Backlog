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
repository's devbook folders in place, the reader's remarks on chapters,
instruction review, and retrieval across all of them. Outside it: a repository's
folders themselves, which the repository owns and edits elsewhere; the Brain
folder's sync between machines, which the folder's own sync product does; the
header scope that picks between the Brain and the repositories, which the
desktop shell owns and this context only follows; the work items a chapter links
to, which [Tasks](../tasks/domain.md#task) answers; and gathering chapters under
planned work, which [Roadmap Planning](../roadmap/domain.md#roadmap-item)
answers.

## Devbook pane

```meta
status: draft
type: feature-flag
key: repository-devbook
default: on
related: [".devbook/domain/devbook/features.md#repository-devbook-areas", ".devbook/domain/devbook/features.md#area-selection-and-scope", ".devbook/domain/devbook/features.md#brain-mode-of-the-devbook-pane"]
```

Turning it on shows the Devbook side pane: for the repository the scope's anchor names, with its design, architecture, domain, technology, AI adoption, and instruction sections, and for the Brain when the anchor is Brain or nothing is pressed. The Brain mode has no switch of its own: it ships with the pane (local ADR 0027). The Shell reads it to decide whether to offer the pane at all, and this context's own scope service reads it to decide which sections to show. Released and on by default. It absorbed `devbook-sections`, which switched the sections separately and left an empty pane when turned off alone, and it replaced `repository-knowledge`. The catalog still reads all three former keys, `knowledge-sections` among them, so a `features.json` that turned either switch off keeps Devbook off. The settings screen lists it from the feature catalog in `AppFeatures.cs`, and a person's choice is kept in `features.json` beside the app's other per-device choices.

## Brain folder

```meta
status: draft
type: setting
key: brain.settings.json
scope: user
default: none
related: [".devbook/domain/devbook/features.md#brain-folder-setup", ".devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md"]
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainSettingsStoreTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsBrainFolderTests]
```

The folder that holds the person's Brain, chosen on the Brain card in Settings › Storage. It ships with no value, because no path suits every person: until one is chosen the Brain has no notes, the Brain chip still scopes tasks, and the Devbook pane on the Brain asks for a folder. Any folder outside a repository works, and a OneDrive folder is the expected one; a folder inside a repository's working tree, a relative path or a file is refused in words, and a folder that does not exist yet is created. The path is kept in `brain.settings.json`, a file of its own beside the workspace's `settings.json` in the app's per-user folder, and is never written into the Brain folder, because the app has to know the path before it can read the folder. It is not a key in `settings.json` because that file is one decision, where the backlog lives and where it is backed up, and moving the backlog never moves the Brain. Blanking the field clears the setting. `IBrainSettings` in `Backlog.Modules.Devbook.Abstractions` is the port every reader takes, with `BrainSetup` holding the chip's two names and the link to the card; `BrainSettingsStore` is the file. Choosing another folder builds another database: the database is keyed by the folder's absolute path, and the old one is left behind.

## Instruction optimization

```meta
status: draft
type: feature-flag
key: instruction-optimization
related: [".devbook/domain/devbook/features.md#instruction-optimization"]
```

Reserved for the instruction review the feature it gates describes, and not in the feature catalog yet: no code checks the key, so there is no default to state and nothing to switch. The chapter exists so the feature has its gate before the first line of it is built; when the review ships, the key lands in `DevbookFeatures` and in the catalog, and this chapter gains its `default`.
