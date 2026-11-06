# Brain

```meta
index: root
type: context
deployment: module
related: [.devbook/arc42/adr/0040-the-brain-is-its-own-bounded-context.md, .devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md, .devbook/arc42/adr/0027-the-brain-is-a-scope-beside-the-repositories.md, .devbook/domain/context-map.md]
```

The Brain is one person's own notes: one devbook-shaped folder per user, outside every repository (local ADR 0026). It is also the scope of everything that person files against no repository (local ADR 0027). Its notes are [Knowledge Notes](domain.md#knowledge-note), organized under [areas](domain.md#para-category), linked to [Tasks](../tasks/domain.md#task) by id and, one way, into a repository's devbook. It is its own bounded context (local ADR 0040): the person and their agent own its structure, as a repository owns its devbook.

Inside the boundary:

- the Knowledge Note and the areas it is organized under;
- the Brain folder setting and the folder's `CONVENTIONS.md`;
- capturing into the Brain, the Brain chip's meaning, and the Brain mode of the Devbook pane;
- repo links and the backlinks they draw, and task links in both directions;
- the Brain's scope on the MCP tools;
- the [Lobes atlas](features.md#lobes-atlas), which maps the Brain beside the devbook of every repository registered on this machine.

Outside it:

- reading Markdown and its `meta` blocks, building the database, rendering a chapter, search, and a reader's [remarks](../devbook/domain.md#remark), which [Devbook](../devbook/context.md) supplies to the Brain as to a repository;
- the [planning fields](../devbook/domain.md#shared-value-objects) a Brain note carries as any devbook chapter does, which Devbook registers;
- the folder's sync between machines, which the folder's own sync product does;
- the header scope's storage and the chip strip, which the desktop shell owns;
- the work items a note links to, which [Tasks](../tasks/domain.md#task) answers;
- triage, which the [Inbox](../inbox/domain.md#inbox-item) does before it routes an item **To Brain**.

## Brain folder

```meta
type: setting
key: brain.settings.json
scope: user
default: none
related: [".devbook/domain/brain/features.md#brain-folder-setup", ".devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md", ".devbook/domain/brain/requirements.md#requirement-a-folder-that-cannot-hold-the-brain-is-refused"]
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainSettingsStoreTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsBrainFolderTests, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainFolderWatchTests, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainFolderSourceTests, unit:dotnet:Backlog.HostComposition.UnitTests.MauiDesktopCompositionTests.The_maui_head_composes_the_brain_beside_the_repository_devbook]
```

The folder that holds the person's Brain, chosen on the Brain card in Settings › Storage. It ships with no value, because no path suits every person. Until one is chosen, the Brain has no notes, the Brain chip still scopes tasks, and the Devbook pane on the Brain asks for a folder.

| What the person enters | What happens |
| --- | --- |
| A folder outside every repository (a OneDrive folder is the expected one) | Kept as the Brain folder |
| A folder that does not exist yet | Created, then kept |
| A folder inside a repository's working tree, a relative path, or a file | Refused ([A folder that cannot hold the Brain is refused](requirements.md#requirement-a-folder-that-cannot-hold-the-brain-is-refused)) |
| Nothing (the field blanked) | The setting is cleared |

**Storage.** `BrainSettingsStore` keeps the path in `brain.settings.json`, a file of its own beside the workspace's `settings.json` in the app's per-user folder. The app must know the path before it can read the folder, so [the path stays out of the folder](requirements.md#requirement-the-path-stays-out-of-the-folder). It is not a key in `settings.json`, which decides where the backlog lives and is backed up; moving the backlog never moves the Brain.

**Readers.** `IBrainSettings` in `Backlog.Modules.Brain.Abstractions` is the port every reader takes. `BrainSetup` holds the chip's two names and the link to the card.

**Live reload.** The Brain's folder source reads the folder from the port on every question, so a change shows without a restart. The folder watch follows it: it moves the database watch to the new folder, stops it when the setting is cleared, and reloads an open Devbook pane.

**Database keying.** The Brain's database is keyed by the folder's absolute path, so choosing another folder builds another database and leaves the old one behind.

## Lobes atlas

```meta
type: feature-flag
key: atlas-lobes
default: off
related: [".devbook/domain/brain/features.md#lobes-atlas", ".devbook/domain/brain/requirements.md#lobes-atlas"]
```

With the `atlas-lobes` flag on, the Devbook pane's **Atlas** button opens the [Lobes atlas](features.md#lobes-atlas) on the header's repository scope, whose anchor the pane reads. The shell header offers the atlas. Off, the button opens today's atlas and the header offers none. The Technology section keeps its own views either way.

It ships off, marked in development, while the Lobes atlas is finished. The key is in `DevbookFeatures` and the feature catalog in `AppFeatures.cs`, so the settings screen lists it with the other switches. A person's choice is kept in `features.json` like the pane's. It means nothing with Devbook off: Devbook's scope service answers the header and the pane from the two switches together.

## Dependencies

The Brain is downstream of Devbook, whose reading machinery it uses, and of the Inbox, which routes items **To Brain**. It is a partner of Tasks.

### Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Devbook](../devbook/context.md) | Customer/Supplier (Devbook = supplier), conforming to the devbook convention as Published Language | In-process use of Devbook's Markdown and `meta` reader, builder, viewer, search and remarks | The devbook convention (local ADR 0016); `DevbookCorpusProfile`, the corpus hook; local ADR 0040 | A Brain note is a devbook chapter with three more fields. |
| [Devbook](../devbook/context.md) | Customer/Supplier (Devbook = supplier), read-only | In-process: Devbook's reader opens the devbook database of every registered repository for the [Lobes atlas](features.md#lobes-atlas) | The devbook database and its reader (local ADR 0015); local ADR 0040 §2 | The Lobes atlas draws every registered repository's devbook beside the Brain. |
| [Devbook](../devbook/context.md) | One-way reference | A [repo link](domain.md#repo-link), `repo:<alias>/<devbook path>[#slug]`, resolved against the repositories configured on this machine | `.devbook/domain/brain/domain.md#repo-link`; local ADR 0027 §6 | A note points at the repository chapter it is about. A repository devbook never links back. |
| [Tasks](../tasks/domain.md#task) | Partnership | Id-based cross-link, read both ways from the note's `tasks` field | `.devbook/domain/brain/domain.md#cross-linking` | Notes link to tasks, and a task row counts the owner's notes that name it. |
| [Repository Management](../repository-management/context.md) | Customer/Supplier (Repository Management = supplier) | Repository registry lookup by alias | The configured repository aliases | A repo link's alias resolves only on a machine where that repository is configured. |

- **Devbook, the chapter machinery.** The Brain reads, indexes and renders a note with Devbook's machinery, applied to the Brain folder. `DevbookCorpusProfile` in `Backlog.Infrastructure.Devbook` is the hook through which Devbook's builder reads that folder.
- **Devbook, the database reader.** The read runs one way, Brain to Devbook. The Brain writes nothing into a repository's devbook, and none links into the Brain.
- **Repository Management.** The registry also lists the repositories whose devbooks the Lobes atlas draws. An alias the notes name that the registry does not hold is a ghost devbook there.

### Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Inbox](../inbox/domain.md#inbox-item) | OHS + Published Language (Inbox = supplier) | Publishes `ItemTriaged` (knowledge route), answered by `IBrainNoteWriter` | `.devbook/domain/inbox/domain.md#itemtriaged` | The Brain writes a Knowledge Note into its inbox area for each item routed **To Brain**. |
| [Tasks](../tasks/domain.md#task) | Partnership | Reference read model plus bi-directional link | `.devbook/domain/brain/domain.md#cross-linking` | The notes that name a task stay countable without sharing aggregates. |
| [Monitoring](../monitoring/domain.md#progress-signal) | Customer/Supplier (Monitoring = customer) | Read-side feed of knowledge activity | `.devbook/domain/brain/domain.md#knowledge-note` | Knowledge activity feeds the project health view, and insights can be captured back as notes. |

An item routed **To Brain** from the Inbox may be a capture or an Inbox note; the Brain treats them alike.
