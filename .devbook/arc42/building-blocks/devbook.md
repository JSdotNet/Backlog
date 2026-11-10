# Devbook

```meta
c4: [backlog.desktop.devbook]
code: [src/Infrastructure/Backlog.Infrastructure.Devbook]
related: [".devbook/arc42/05-building-block-view.md#desktop-app", ".devbook/arc42/adr/0015-devbook-database-lives-in-app-storage-and-the-app-builds-it.md", ".devbook/arc42/adr/0044-devbook-subject-state-lives-in-backlogs-own-store.md", ".devbook/arc42/adr/0046-c4-zoom-navigation-is-a-second-tab-read-from-devbook-meta.md", ".devbook/domain/devbook/features.md#c4-zoom-navigation"]
```

The Devbook component of the desktop app reads a repository's devbook folders,
from a clone or a branch snapshot, and draws their chapters in the Devbook pane. It
owns the devbook database the app builds in its own storage (local ADR 0015) and
the C4 zoom surface over `workspace.dsl` (local ADR 0046). Where the folders are on
disk is answered outside it, by `Infrastructure.FileSystem`. A record's workflow
state lives in Backlog's subject store (local ADR 0044). Chapter 05's
[Desktop App](../05-building-block-view.md#desktop-app) section describes the
database tables and the Brain's use of the same builder.

## Interfaces

```meta
```

- **The Devbook pane:** `DevbookPane`, which the shell offers. While `c4-zoom` and
  `c4-diagrams` are on, its architecture section is the C4 zoom surface.
- **The ports:** `IDevbookFolderSource`, `IDevbookSubjectStateStore` and the other
  interfaces in `Backlog.Modules.Devbook.Abstractions`, with the switch keys in
  `DevbookFeatures`.
- **The database:** `DevbookDatabaseBuilder`, `DevbookDatabaseRefresher` and the
  read-only `DevbookDatabase` in `Backlog.Infrastructure.Devbook`.
- **The Code level:** `CSharpCodeReader` in the same project, which reads the C#
  types under a building block's `code` folders.

## Structure

```meta
```

| Project | Holds |
| --- | --- |
| `Backlog.Modules.Devbook.Abstractions` | The ports, and the reading rules the builder and the pane share |
| `Backlog.Modules.Devbook.UI` | The pane and its panels, the C4 surface among them |
| `Backlog.Infrastructure.Devbook` | The database builder and reader, search, and the C# code reader |

`code` names `Backlog.Infrastructure.Devbook` alone, so the Code level of this
component lists that project's types.

## Dependencies

```meta
```

What the block depends on:

| Dependency | Mechanism |
| --- | --- |
| UI Components | Project reference; the C4 diagrams, zoom explorer and Code view are drawn there |
| `Infrastructure.Copilot`, `Infrastructure.GitHub` | Project references from the module UI |
| Tasks and Notifications abstractions | Project references from `Backlog.Infrastructure.Devbook` |

What depends on the block:

| Dependent | Mechanism |
| --- | --- |
| Shell, `Backlog.Desktop.UI` | Offers the pane |
| `Backlog.Desktop.Composition` | Registers the adapters in `AddDevbookAdapters` |
| `Infrastructure.FileSystem` | Implements `IDevbookFolderSource` over clones and branch snapshots |
| Brain, `Infrastructure.Brain` and `Modules.Brain.UI` | Reuse the builder and the panels for the Brain |
| `Infrastructure.Mcp` | Reads through the Abstractions ports |
