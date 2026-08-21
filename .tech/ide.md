# IDE Stack

```meta
status: candidate
related: [".tech/technology-graph.md", ".arc42/04-solution-strategy.md#technology-choices"]
```

> Two separate extension stacks that bring backlog items, prompts, and project
> knowledge into the editor where the work actually happens.

## VS Code Extension API

```meta
status: candidate
type: framework
depends-on: [".tech/shared.md#typescript", ".tech/shared.md#nodejs"]
related: [".arc42/04-solution-strategy.md#technology-choices"]
```

The extensibility model for the VS Code channel.

- **Used for** — commands, tree views, and webview panels showing the local
  backlog, prompt library, and knowledge notes.
- **Why** — the only supported way to extend VS Code; reads the desktop's local
  Markdown/local API directly.

## VS Code Webview UI

```meta
status: candidate
type: framework
depends-on: [".tech/ide.md#vs-code-extension-api"]
alternatives: ["Native tree/quick-pick UI only"]
```

The HTML-based panel UI hosted inside the extension.

- **Used for** — richer views (prompt browsing, item detail) that native VS Code
  UI primitives cannot express.
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
  functionality.
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
