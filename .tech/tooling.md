# Build and Governance Tooling

```meta
status: adopted
related: [".tech/technology-graph.md", ".tech/ai-development.md"]
```

> Technologies used to build, package, deploy, and govern this repository rather
> than to run the product. The agent harnesses and their plugins used to live
> here too; they are now their own layer in
> [`ai-development.md`](ai-development.md).

## Git

```meta
status: adopted
type: tool
related: [".tech/ai-development.md#git-worktree-sessions"]
```

Source control for the repository and its worktree-based session model.

- **Used for** — versioning code, the knowledge folders, and all governance
  assets; `GitFileHistoryService` in `Backlog.Infrastructure.GitHub` also reads
  history as a product signal.
- **Why** — non-negotiable baseline; worktrees are how parallel agent sessions
  stay isolated.

## MSBuild

```meta
status: adopted
type: tool
depends-on: [".tech/tooling.md#net-sdk"]
related: [".tech/tooling.md#central-package-management"]
```

The build engine, and the directory-scoped property files that configure it.

- **Used for** — one root `Directory.Build.props` setting `net10.0`,
  `LangVersion`, `Nullable`, `ImplicitUsings`, the product identity, and
  `$(MauiVersion)`; plus two subtree files that each re-import it explicitly
  because MSBuild loads only the nearest one — `tests/` (the shared runner and
  coverage package set, `OutputType=Exe`, the `Xunit` using, and the linked
  `RepositoryRoot.cs`) and `src/Harness/` (the non-publishable flags that make an
  accidental `dotnet publish` fail loudly).
- **Why** — a setting every project needs is declared once, so a new project
  cannot forget it.

## .NET SDK

```meta
status: adopted
type: tool
depends-on: [".tech/shared.md#net-runtime"]
related: [".tech/testing.md#microsofttestingplatform"]
```

The build and test toolchain for every C# component.

- **Used for** — `dotnet build`, `dotnet test`, and `dotnet publish` across
  desktop, mobile, IDE, cloud, and harness projects.
- **Why** — the single toolchain for the .NET side of the stack. `global.json`
  selects Microsoft.Testing.Platform as the test runner, which changes the
  command-line contract of `dotnet test`.

## .NET MAUI Workloads

```meta
status: adopted
type: tool
depends-on: [".tech/tooling.md#net-sdk", ".tech/shared.md#net-maui"]
```

The optional SDK workloads the two app heads need to build.

- **Used for** — `dotnet workload restore` in the pull-request workflow, and the
  targeted `dotnet workload install maui-windows` / `maui-android` in the two
  release workflows.
- **Why** — the MAUI heads do not build without them, and installing only the
  platform workload a release job needs keeps that job short.

## NuGet

```meta
status: adopted
type: tool
depends-on: [".tech/tooling.md#net-sdk"]
```

The package manager for .NET dependencies.

- **Used for** — restoring the twenty-three packages the solution pins, plus the
  Aspire AppHost SDK.
- **Why** — the standard .NET package ecosystem.

## Central Package Management

```meta
status: adopted
type: tool
depends-on: [".tech/tooling.md#nuget"]
related: [".tech/shared.md#yamldotnet"]
```

One file declaring every package version in the solution.

- **Used for** — `Directory.Packages.props`, per org ADR 0002: project files
  reference packages by name only, and a project-local `Version` attribute has to
  be justified in a comment rather than used as a convenience.
- **Why** — one place to see and change what the solution resolves.
- **How** — `CentralPackageTransitivePinningEnabled` is deliberately **off**.
  Turning it on rewrote versions the solution never asked for — the AppHost's
  transitive YamlDotNet from 16.3.0 to 18.1.0, OpenTelemetry 1.15.3 to 1.17.0,
  `Microsoft.Extensions.*` 10.0.8 to 10.0.11 — purely because those names appear
  as direct versions. Adopting CPM was meant to change *where* versions are
  declared, not *which ones* resolve; transitive governance is a separate,
  reviewed change.

## npm

```meta
status: adopted
type: tool
depends-on: [".tech/shared.md#nodejs"]
```

The package manager for JavaScript/TypeScript dependencies.

- **Used for** — the VS Code extension's dev dependencies (`npm ci` then
  `npm run compile` in the pull-request workflow, `npm run watch` as the
  `ide-vscode-build` Aspire resource).
- **Why** — the default package manager of the Node.js ecosystem. Note that the
  repository's own Node tooling deliberately avoids it: Archify is vendored under
  `tools/archify/` with no `node_modules`, so `tools/diagrams` runs on a bare
  Node install.

## PowerShell

```meta
status: adopted
type: language
```

The scripting language for build, release, and hook automation on Windows.

- **Used for** — `build/New-AppInstaller.ps1`, `build/Get-ReleasePaths.ps1`,
  `build/Install-AndroidApp.ps1`, `build/stop-aspire-before-pr.ps1`, the
  `spawn-task-to-issue` hook, and the `run:` steps of both release workflows and
  the Foundry deployment.
- **Why** — the release workflows run on Windows runners, and packaging, signing,
  and certificate handling are all PowerShell-native there.

## Bicep

```meta
status: adopted
type: language
depends-on: [".tech/tooling.md#azure-cli"]
related: [".tech/cloud.md#azure-ai-foundry"]
```

The infrastructure-as-code language for the Azure resources this repository
deploys.

- **Used for** — `infra/foundry/main.bicep` and its `.bicepparam`, which declare
  the AI Services account and its model deployments.
- **Why** — first-party, typed, and it what-ifs before it deploys.

## Azure CLI

```meta
status: adopted
type: tool
related: [".tech/cloud.md#azure-ai-foundry", ".tech/tooling.md#github-actions"]
```

The deployment and diagnostics entry point for Azure.

- **Used for** — `.github/workflows/deploy-foundry.yml`, which builds the Bicep,
  validates, runs `deployment group what-if`, and then creates the deployment on
  a self-hosted runner.
- **Why** — it is what the self-hosted runner is already signed in to; the
  workflow fails with an explicit message when it is not, rather than deploying
  into the wrong subscription.

## Aspire CLI

```meta
status: adopted
type: tool
depends-on: [".tech/shared.md#net-aspire"]
```

The local orchestration and diagnostics entry point.

- **Used for** — `aspire start --isolated --non-interactive --apphost …` for
  local runs, plus logs, traces, and deployment artifacts during QA.
- **Why** — the paired CLI for the app model. `--isolated` is what keeps parallel
  worktree sessions from fighting over ports and user-secrets state; the AppHost
  itself stays opted out of the CLI bundle, so `aspire` is invoked explicitly
  rather than through `dotnet run`.

## GitHub Actions

```meta
status: adopted
type: service
depends-on: [".tech/shared.md#github-platform"]
```

The CI/CD automation platform.

- **Used for** — six workflows: `pull-request` (build, test, TRX artifact, and
  the VS Code extension compile), `codeql`, `knowledge-meta`, `deploy-foundry`,
  `release-desktop`, and `release-mobile`.
- **Why** — native to the repository host, with least-privilege and OIDC support.
- **How** — every third-party action is pinned to a full commit SHA with the
  version in a trailing comment, so a moved tag cannot change what runs.

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
related: [".tech/tooling.md#central-package-management"]
```

Automated dependency and security updates.

- **Used for** — keeping NuGet, npm, and GitHub Actions dependencies current
  (`.github/dependabot.yml`).
- **Why** — low-effort supply-chain hygiene for a single-maintainer project.

## knowledge-meta Generator

```meta
status: adopted
type: tool
depends-on: [".tech/shared.md#nodejs", ".tech/shared.md#json"]
related: [".tech/ai-development.md#knowledge-base-plugin", ".tech/tooling.md#github-actions"]
```

The generator that compiles the knowledge folders' `meta` blocks into derived
indexes.

- **Used for** — `node .github/tools/knowledge-meta/build.mjs`, producing
  `_meta/graph.json` (the reference graph) and `_meta/index.json` (the reading
  outline) per folder plus a repository-wide rollup. Installed by the
  `knowledge-base` plugin rather than hand-written here.
- **Why** — it is what turns the metadata convention into something queryable,
  and it is where a broken `depends-on` or `related` reference is caught.
- **How** — `.github/workflows/knowledge-meta.yml` fails on an unresolvable
  reference or an invalid `meta` block, then regenerates and diffs the committed
  indexes and reports drift as a *warning* only: making every pull request carry a
  regenerated index is what turned these files into merge conflicts. Refresh is
  deliberate instead — `build/Update-KnowledgeIndex.ps1` on demand,
  `.github/workflows/knowledge-meta-nightly.yml` on a schedule. The drift step runs
  the generator rather than trusting `--check`, because `--check` misses
  line-number drift.

## Archify

```meta
status: adopted
type: tool
version: "2.15.0"
depends-on: [".tech/shared.md#nodejs", ".tech/shared.md#mermaid"]
related: [".tech/tooling.md#ajv", ".tech/tooling.md#simple-icons"]
```

A JSON-IR diagram renderer, vendored under `tools/archify/`.

- **Used for** — re-authoring a knowledge-chapter mermaid fence as a
  specification and rendering a self-contained HTML artifact from it, which the
  desktop app shows in place of the drawn mermaid behind the `archify-diagrams`
  flag. `tools/diagrams/archify-artifacts.mjs` wraps it with `scan`, `scaffold`,
  `render`, and `verify`.
- **Why** — it is a visualization layer over the canonical fence, not a
  replacement: an artifact is matched to a diagram by the SHA-256 of the
  normalized fence, so an edited diagram misses the lookup and falls back to
  mermaid with an out-of-date note instead of confidently showing a stale
  picture.
- **How** — vendored with no `node_modules`, so the commands run on a bare Node
  install. A specification is accepted only at 9/9 checks; three diagrams in the
  repository render at the `standard` profile instead of `showcase`, and say so
  in their filename.

## Ajv

```meta
status: adopted
type: package
version: "^8.17.1"
depends-on: [".tech/tooling.md#archify", ".tech/shared.md#json"]
```

JSON Schema validation.

- **Used for** — validating an Archify specification before it renders, through
  the generated validators `tools/archify/scripts/generate-validators.mjs`
  produces.
- **Why** — a specification that fails validation must not produce an artifact;
  the check is compiled rather than interpreted at render time.

## simple-icons

```meta
status: adopted
type: package
version: "16.28.0"
depends-on: [".tech/tooling.md#archify"]
```

The brand-mark icon set.

- **Used for** — the technology marks Archify draws into an artifact, generated
  into the renderer by `generate-brand-marks.mjs`.
- **Why** — pinned to an exact version, and both generators ship a `--check` mode
  that the vendored test script runs, so a regenerated output cannot drift from
  what is committed.
