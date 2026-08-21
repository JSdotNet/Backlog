# Development and Governance Tooling

```meta
status: adopted
related: [".tech/technology-graph.md"]
```

> Technologies used to build, govern, and automate this repository rather than
> to run the product. This layer is the most `adopted` one, because it is
> already in daily use during the bootstrap phase.

## Git

```meta
status: adopted
type: tool
```

Source control for the repository and its worktree-based session model.

- **Used for** — versioning code, the knowledge folders, and all governance
  assets.
- **Why** — non-negotiable baseline; worktrees are how parallel agent sessions
  stay isolated.

## .NET SDK

```meta
status: candidate
type: tool
depends-on: [".tech/shared.md#net-runtime"]
```

The build and test toolchain for every C# component.

- **Used for** — `dotnet build`, `dotnet test`, and packaging across desktop,
  mobile, IDE, and cloud projects.
- **Why** — the single toolchain for the .NET side of the stack.

## NuGet

```meta
status: candidate
type: tool
depends-on: [".tech/tooling.md#net-sdk"]
```

The package manager for .NET dependencies.

- **Used for** — resolving framework and library packages, with central version
  management planned once code lands.
- **Why** — the standard .NET package ecosystem.

## npm

```meta
status: candidate
type: tool
depends-on: [".tech/shared.md#nodejs"]
```

The package manager for JavaScript/TypeScript dependencies.

- **Used for** — VS Code extension dependencies and repository-local Copilot
  extensions.
- **Why** — the default package manager of the Node.js ecosystem.

## GitHub Actions

```meta
status: adopted
type: service
depends-on: [".tech/shared.md#github-platform"]
```

The CI/CD automation platform.

- **Used for** — running CodeQL today; build, test, and release pipelines once
  code exists.
- **Why** — native to the repository host, with least-privilege and OIDC support.

## CodeQL

```meta
status: adopted
type: tool
depends-on: [".tech/tooling.md#github-actions"]
```

Static application security testing.

- **Used for** — scanning the repository for security issues on push and
  schedule (`.github/workflows/codeql.yml`).
- **Why** — first-party scanning with no extra infrastructure.

## Dependabot

```meta
status: adopted
type: service
depends-on: [".tech/shared.md#github-platform"]
```

Automated dependency and security updates.

- **Used for** — keeping NuGet, npm, and GitHub Actions dependencies current
  (`.github/dependabot.yml`).
- **Why** — low-effort supply-chain hygiene for a single-maintainer project.

## GitHub Copilot CLI

```meta
status: adopted
type: tool
depends-on: [".tech/shared.md#github-platform"]
```

The AI development environment this project is built with.

- **Used for** — agent sessions, orchestration skills, custom agents, and the
  canvas extensions delivered by installed plugins.
- **Why** — the project is explicitly AI-first; its own tooling is part of the
  stack, not incidental.

## Model Context Protocol Servers

```meta
status: adopted
type: protocol
depends-on: [".tech/tooling.md#github-copilot-cli"]
related: [".tech/tooling.md#knowledge-canvas-extension"]
```

The authoritative guidance channel for agents working in this repository.

- **Used for** — `jsdotnet-project-guidelines` (repository conventions) and
  `jsdotnet-project-design` (design and UX guidance).
- **Why** — keeps governance out of prompt memory and in a queryable source.

## Knowledge Base Plugin

```meta
status: adopted
type: tool
depends-on: [".tech/tooling.md#github-copilot-cli", ".tech/shared.md#nodejs"]
related: [".tech/tooling.md#knowledge-canvas-extension"]
```

The Copilot plugin that owns the knowledge-folder convention this repository
follows.

- **Used for** — the authoring instructions for `.arc42`, `.domain`, `.backlog`,
  `.tech`, and `.design`, the per-folder orchestration skills, the
  `knowledge-canvas` extension, and the `knowledge-meta` generator installed
  into `.github/tools/`.
- **Why** — the convention is reusable across repositories, so it lives in one
  versioned plugin instead of being duplicated per repository.
- **Sourced from** — `copilot plugin install JSdotNet/Copilot:plugins/knowledge-base`.

## Knowledge Canvas Extension

```meta
status: adopted
type: tool
depends-on: [".tech/tooling.md#github-copilot-cli", ".tech/shared.md#nodejs", ".tech/shared.md#mermaid"]
related: [".tech/tooling.md#knowledge-base-plugin"]
```

The `knowledge-base` plugin's Copilot canvas for viewing knowledge folders.

- **Used for** — rendering `.arc42`, `.domain`, `.backlog`, `.tech`, and `.design`
  Markdown with live Mermaid diagrams and a metadata/lint side panel.
- **Why** — the metadata convention is designed for machine reading, so a viewer
  is what makes the graph usable rather than just stored.
- **Sourced from** — `JSdotNet/Copilot:plugins/knowledge-base`; it is installed
  as a plugin rather than checked into this repository.

## Aspire CLI

```meta
status: candidate
type: tool
depends-on: [".tech/cloud.md#net-aspire"]
```

The local orchestration and diagnostics entry point.

- **Used for** — running the app model locally, plus logs, traces, and deploy
  artifact generation during QA.
- **Why** — the paired CLI for the .NET Aspire app model already assumed by this
  repository's QA workflow.
