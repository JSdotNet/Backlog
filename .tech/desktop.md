# Desktop Stack

```meta
status: candidate
related: [".tech/technology-graph.md", ".arc42/07-deployment-view.md#local-deployment-desktop"]
```

> The canonical, local-first Windows client. It owns the data and runs all
> capture workers, so this is the richest layer of the graph.

## Windows

```meta
status: candidate
kind: platform
related: [".arc42/07-deployment-view.md#local-deployment-desktop"]
```

The target operating system for the desktop client.

- **Used for** — hosting the desktop app, its background workers, and the local
  Markdown/JSON store.
- **Why** — the personal-use scope is Windows-only, which allows a native
  Windows UI stack instead of a cross-platform compromise.

## Windows App SDK

```meta
status: candidate
kind: framework
depends-on: [".tech/desktop.md#windows", ".tech/shared.md#net-runtime"]
```

The modern Windows application platform underneath the UI framework.

- **Used for** — windowing, app lifecycle, notifications, and OS integration.
- **Why** — the supported foundation for WinUI 3 desktop apps.

## WinUI 3

```meta
status: candidate
kind: framework
depends-on: [".tech/desktop.md#windows-app-sdk", ".tech/shared.md#c-language"]
related: [".arc42/04-solution-strategy.md#technology-choices"]
alternatives: ["WPF", "Avalonia", "Electron"]
```

The preferred UI framework for the desktop client.

- **Used for** — the full desktop experience: inbox triage, backlog, prompt
  library, second brain, and dashboards.
- **Why** — named as the preferred desktop stack in
  `.arc42/04-solution-strategy.md#technology-choices`; native Windows fidelity
  with a modern XAML/C# model.

## Local Markdown and JSON Store

```meta
status: candidate
kind: library
depends-on: [".tech/shared.md#markdown", ".tech/shared.md#json"]
related: [".arc42/08-crosscutting-concepts.md#storage-and-sync"]
alternatives: ["SQLite", "LiteDB"]
```

The file-backed persistence layer, implemented in-app rather than bought in.

- **Used for** — reading and writing the canonical Markdown tree and maintaining
  the derived JSON indexes that power local full-text search.
- **Why** — the "Markdown is canonical" constraint rules out a database as the
  source of truth; indexes stay a rebuildable derived artifact.

## Background Workers

```meta
status: candidate
kind: library
depends-on: [".tech/shared.md#net-runtime"]
related: [".arc42/04-solution-strategy.md#thin-cloud-rich-desktop"]
```

In-process hosted services that poll external sources on the local machine.

- **Used for** — YouTube, website, and email capture, GitHub issue sync, and
  stale-item detection.
- **Why** — keeping fetching local is what keeps external credentials off the
  cloud (quality goal 2).

## GitHub CLI

```meta
status: adopted
kind: tool
depends-on: [".tech/shared.md#github-platform"]
related: [".arc42/04-solution-strategy.md#thin-cloud-rich-desktop"]
alternatives: ["Octokit.NET", "raw REST calls"]
```

The local integration path to GitHub from the desktop (`gh`).

- **Used for** — creating and updating issues from backlog items without routing
  credentials through the cloud service.
- **Why** — reuses the developer's existing authenticated GitHub session; also
  already the mandated tool for GitHub operations in this repository.

## MSIX Packaging

```meta
status: candidate
kind: tool
depends-on: [".tech/desktop.md#windows-app-sdk"]
```

The packaging and update format for the Windows client.

- **Used for** — installing and updating the desktop app on the user's machines.
- **Why** — the standard distribution model for Windows App SDK applications.
