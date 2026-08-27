# Desktop Stack

```meta
status: adopted
related: [".tech/technology-graph.md", ".arc42/07-deployment-view.md#local-deployment-desktop"]
```

> The canonical, local-first Windows client. It owns the data and runs all
> capture workers, so this is the richest layer of the graph.

## Windows

```meta
status: adopted
type: platform
related: [".arc42/07-deployment-view.md#local-deployment-desktop"]
```

The target operating system for the desktop client.

- **Used for** — hosting the desktop app, its background workers, and the local
  task store.
- **Why** — the personal-use scope is Windows-only, which allows a native
  Windows UI stack instead of a cross-platform compromise.

## Windows App SDK

```meta
status: adopted
type: framework
depends-on: [".tech/desktop.md#windows", ".tech/shared.md#net-runtime"]
```

The modern Windows application platform underneath the UI framework.

- **Used for** — windowing, app lifecycle, notifications, and OS integration.
- **Why** — the supported foundation for WinUI 3 desktop apps.

## WinUI 3

```meta
status: adopted
type: framework
depends-on: [".tech/desktop.md#windows-app-sdk", ".tech/shared.md#c-language"]
related: [".arc42/04-solution-strategy.md#technology-choices", ".arc42/adr/0001-desktop-stack-maui-blazor-hybrid.md"]
```

The native Windows head that `.tech/shared.md#net-maui` uses on this platform.

- **Used for** — hosting the desktop client's native shell, windowing, and OS
  integration; also hosts the WebView2 surface that renders the app's actual UI
  (`.tech/shared.md#blazor-hybrid`).
- **Why** — `.arc42/adr/0001-desktop-stack-maui-blazor-hybrid.md` supersedes the
  original plain-WinUI-3 choice: the app is no longer authored directly as WinUI
  XAML, but MAUI's Windows head still is WinUI 3, so the native filesystem-access
  and background-worker guarantees are unchanged.

## WebView2

```meta
status: adopted
type: framework
depends-on: [".tech/desktop.md#windows-app-sdk"]
related: [".tech/shared.md#blazor-hybrid", ".tech/testing.md#playwright", ".arc42/adr/0001-desktop-stack-maui-blazor-hybrid.md"]
```

The Chromium-based embedded browser the desktop UI actually renders in.

- **Used for** — hosting the `BlazorWebView` that draws every screen, and
  exposing a CDP debugging port so Playwright can attach to the running desktop
  app rather than only to the web harness.
- **Why** — that debugging port is one of the two decisive reasons ADR 0001 chose
  Blazor Hybrid over plain MAUI XAML. It is why Debug builds stay unpackaged:
  MSIX would break both the Aspire desktop resource and the CDP attach.

## Local Task Store

```meta
status: adopted
type: library
depends-on: [".tech/shared.md#sqlite", ".tech/shared.md#microsoftdatasqlite", ".tech/shared.md#json"]
related: [".arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md", ".arc42/08-crosscutting-concepts.md#storage-and-sync"]
alternatives: ["Markdown files with YAML frontmatter", "LiteDB"]
```

The file-backed persistence layer, implemented in-app rather than bought in.

- **Used for** — `Backlog.Infrastructure.Sqlite` holds the canonical `tasks`
  table; `RootedSqliteTaskRepository` binds it to the workspace root the user
  chose, so the desktop head and the web harness open the same database the same
  way. Settings stay JSON files beside it.
- **Why** — `.arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md`
  replaced the earlier markdown-document store and its two derived indexes with
  one database that cannot disagree with itself. Markdown remains the *content*
  of an entry, as a text column — not its storage format.

## Background Workers

```meta
status: candidate
type: library
depends-on: [".tech/shared.md#net-runtime"]
related: [".arc42/04-solution-strategy.md#thin-cloud-rich-desktop"]
```

In-process hosted services that poll external sources on the local machine.

- **Used for** — YouTube, website, and email capture, GitHub issue sync, and
  stale-item detection. Still `candidate`: no hosted service is implemented yet,
  and the capture paths currently run on demand.
- **Why** — keeping fetching local is what keeps external credentials off the
  cloud (quality goal 2).

## GitHub CLI

```meta
status: adopted
type: tool
depends-on: [".tech/shared.md#github-platform"]
related: [".arc42/04-solution-strategy.md#thin-cloud-rich-desktop"]
alternatives: ["Octokit.NET", "raw REST calls"]
```

The local integration path to GitHub from the desktop (`gh`).

- **Used for** — creating and updating issues from backlog items without routing
  credentials through the cloud service. `GhCliTransport` in
  `Backlog.Infrastructure.GitHub` is the seam; the clients above it (issues,
  activity, billing, identity) never see a token.
- **Why** — reuses the developer's existing authenticated GitHub session; also
  already the mandated tool for GitHub operations in this repository.

## MSIX Packaging

```meta
status: adopted
type: tool
depends-on: [".tech/desktop.md#windows-app-sdk"]
related: [".arc42/07-deployment-view.md#installation-and-updates", ".tech/tooling.md#powershell"]
```

The packaging and update format for the Windows client.

- **Used for** — building the desktop app as a signed, sideloadable MSIX and
  installing/updating it on the user's machines.
- **Why** — the standard distribution model for Windows App SDK applications.
- **How** — Release + Windows builds switch to `WindowsPackageType=MSIX` and
  emit a single signed MSIX (`AppxBundle=Never`, `SideloadOnly`). Debug stays
  unpackaged (`WindowsPackageType=None`) so the Aspire desktop resource and the
  WebView2 CDP attach used by Playwright keep working. The MSIX is published as a
  GitHub Release asset and installed by sideloading rather than through the
  Microsoft Store; its `Identity Name` (`JSdotNet.Backlog.Desktop`) and
  `Publisher` (`CN=JSdotNet`) must match the App Installer's `MainPackage`
  exactly or updates will not apply. Because it is self-signed, the signing
  certificate has to be trusted on the target machine before the first install.

## App Installer (`.appinstaller`)

```meta
status: adopted
type: format
depends-on: [".tech/desktop.md#msix-packaging"]
related: [".arc42/07-deployment-view.md#installation-and-updates"]
```

The XML manifest that turns a bare MSIX into an updatable install.

- **Used for** — declaring the stable update source and the current package, so
  the app can check for and pull newer versions from GitHub Releases.
- **Why** — App Installer's `UpdateSettings` (`OnLaunch` +
  `AutomaticBackgroundTask`) gives launch-time and background update checks
  without any custom update server; clicking the version in the app header
  drives the same mechanism via `PackageManager`.
- **How** — generated from `build/Backlog.Desktop.appinstaller.template` by
  `build/New-AppInstaller.ps1` during the release workflow. Its own `Uri` points
  at the `latest` release download (stable), while `MainPackage/@Uri` points at
  the tagged release asset; `Name`, `Publisher`, and `ProcessorArchitecture` are
  kept identical to the signed MSIX.
