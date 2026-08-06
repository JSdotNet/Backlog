# Shared Technologies

```meta
status: candidate
related: [".tech/technology-graph.md", ".arc42/02-constraints.md#technical-constraints"]
```

> Technologies used by more than one channel. Every layer file points at these
> chapters with `depends-on` instead of redefining them locally.

## Markdown

```meta
status: adopted
kind: format
related: [".arc42/02-constraints.md#technical-constraints", ".arc42/08-crosscutting-concepts.md#storage-and-sync"]
```

The canonical storage format for all user content: inbox items, backlog items,
knowledge notes, and prompts.

- **Used for** — the single source of truth on disk; also the format of this
  repository's own `.arc42`/`.domain`/`.backlog`/`.tech` knowledge folders.
- **Why** — plain text is durable, diffable, greppable, and editable without the
  app; it is a hard constraint of the architecture.

## JSON

```meta
status: adopted
kind: format
related: [".arc42/08-crosscutting-concepts.md#storage-and-sync"]
```

The derived-data format sitting beside the canonical Markdown.

- **Used for** — indexes (`.index/*.json`), metadata, relationships, the repo
  registry (`config/repos.json`), and the tag graph (`.tags/`).
- **Why** — cheap to write from every channel's stack and fast to load into
  memory for local search without a database engine.

## YAML

```meta
status: adopted
kind: format
```

The structured-metadata format embedded in Markdown documents.

- **Used for** — the fenced `meta` blocks that make the knowledge folders
  machine-readable, plus GitHub workflow and issue-template configuration.
- **Why** — human-writable inside Markdown and trivially parseable by tooling.

## Mermaid

```meta
status: adopted
kind: format
depends-on: [".tech/shared.md#markdown"]
```

Diagram-as-text notation embedded directly in Markdown.

- **Used for** — the technology graph, C4/deployment/sequence diagrams in
  `.arc42`, and domain model/flow diagrams in `.domain`.
- **Why** — diagrams stay version-controlled and reviewable in the same diff as
  the prose they belong to; rendered natively by GitHub and the knowledge canvas.

## .NET Runtime

```meta
status: candidate
kind: runtime
version: "10.0"
related: [".arc42/04-solution-strategy.md#technology-choices", ".arc42/09-architecture-decisions.md"]
alternatives: ["Node.js only", "Rust + Tauri"]
```

The primary managed runtime for the desktop, mobile, IDE (Visual Studio), and
cloud channels.

- **Used for** — hosting every C#-based component of the system.
- **Why** — the organization's governed .NET guidance applies to the cloud
  service, and reusing one runtime across channels maximizes shared code.

## C# Language

```meta
status: candidate
kind: language
depends-on: [".tech/shared.md#net-runtime"]
related: [".arc42/04-solution-strategy.md#technology-choices"]
```

The main implementation language of the system.

- **Used for** — desktop, mobile, Visual Studio extension, and cloud service.
- **Why** — one language across four of the five channels keeps domain logic and
  contracts shareable.

## Node.js

```meta
status: candidate
kind: runtime
related: [".tech/ide.md#vs-code-extension-api"]
```

The JavaScript runtime hosting the VS Code extension and repository-local
tooling.

- **Used for** — running the VS Code extension host and this repository's
  Copilot canvas extension.
- **Why** — mandated by the VS Code extension model; already present on
  developer machines.

## TypeScript

```meta
status: candidate
kind: language
depends-on: [".tech/shared.md#nodejs"]
related: [".tech/ide.md#vs-code-extension-api"]
```

The implementation language for the VS Code channel and repository tooling.

- **Used for** — the VS Code extension and its webview UI, plus the local
  knowledge-canvas Copilot extension in `.github/extensions/`.
- **Why** — the only first-class option for VS Code extensibility.

## GitHub Platform

```meta
status: adopted
kind: service
related: [".arc42/02-constraints.md#organizational--process-constraints"]
```

The external system of record for issues, repositories, and automation.

- **Used for** — backlog issue sync, webhooks into the cloud service, repository
  health signals, CI/CD, and this repository itself.
- **Why** — a hard organizational constraint: GitHub is the external issue
  system, so the product integrates with it rather than replacing it.
