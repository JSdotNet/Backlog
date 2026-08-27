# IDE Stack

```meta
status: candidate
related: [".tech/technology-graph.md", ".arc42/04-solution-strategy.md#technology-choices"]
```

> Two separate extension stacks that bring backlog items, prompts, and project
> knowledge into the editor where the work actually happens. The VS Code side
> exists; the Visual Studio side is still a named intention.

## VS Code Extension API

```meta
status: adopted
type: framework
version: "^1.90.0"
depends-on: [".tech/shared.md#typescript", ".tech/shared.md#nodejs"]
related: [".arc42/04-solution-strategy.md#technology-choices", ".tech/shared.md#net-aspire"]
```

The extensibility model for the VS Code channel.

- **Used for** — `Backlog.Ide.VsCode`: the `backlog.capture` and
  `backlog.refreshInbox` commands and the `backlogInbox` explorer tree view,
  configured with the sync service's base URL.
- **Why** — the only supported way to extend VS Code.
- **How** — the AppHost registers two explicit-start resources for it:
  `ide-vscode-build` runs `npm run watch` to keep `out/extension.js` current, and
  `ide-vscode-host` launches an Extension Development Host with the extension
  side-loaded. Because Aspire assigns a dynamic port per run, `backlog.cloudUrl`
  has no default — it is copied from the dashboard.

## VS Code Webview UI

```meta
status: candidate
type: framework
depends-on: [".tech/ide.md#vs-code-extension-api"]
alternatives: ["Native tree/quick-pick UI only"]
```

The HTML-based panel UI hosted inside the extension.

- **Used for** — richer views (prompt browsing, item detail) that native VS Code
  UI primitives cannot express. Not yet built: the extension currently
  contributes only commands and a tree view.
- **Why** — keeps complex views feasible without leaving the editor.

## Visual Studio Extensibility

```meta
status: candidate
type: framework
depends-on: [".tech/shared.md#c-language"]
related: [".arc42/04-solution-strategy.md#technology-choices"]
```

The VSSDK-based extension model for the Visual Studio channel.

- **Used for** — tool windows and commands mirroring the VS Code extension's
  functionality. No project exists for it yet.
- **Why** — required for Visual Studio integration; stays in C#, so contracts can
  be shared with the desktop client.

## WPF

```meta
status: candidate
type: framework
depends-on: [".tech/ide.md#visual-studio-extensibility", ".tech/shared.md#net-runtime"]
```

The UI framework for Visual Studio tool windows.

- **Used for** — rendering the extension's tool-window content.
- **Why** — Visual Studio's own UI stack; not a free choice.
