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
  transitive YamlDotNet 16.3.0, OpenTelemetry 1.15.3, and
  `Microsoft.Extensions.*` 10.0.8 were each lifted to whatever
  `Directory.Packages.props` declares for that name — purely because those names
  appear there as direct versions. Those targets move with every package bump, so
  they are not repeated here; the chapter that owns each package carries the
  current one in its `version` field. Adopting CPM was meant to change *where*
  versions are declared, not *which ones* resolve; transitive governance is a
  separate, reviewed change.

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
  the AI Services account and its model deployments; and `infra/sync/main.bicep`,
  which declares the cloud sync tier — Cosmos DB, Container Apps, Key Vault, Log
  Analytics, and Application Insights.
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

## Azure Developer CLI

```meta
status: adopted
type: tool
depends-on: [".tech/tooling.md#bicep"]
related: [".tech/cloud.md#azure-cosmos-db", ".tech/cloud.md#azure-container-apps", ".tech/tooling.md#github-actions", ".arc42/07-deployment-view.md#provisioning-and-delivery"]
alternatives: ["Azure CLI"]
```

The provision-and-deploy driver for the cloud sync tier.

- **Used for** — `azure.yaml` and `.github/workflows/deploy-sync.yml`, which
  provision `infra/sync/main.bicep` and then build, push, and deploy
  `Backlog.Modules.Sync.Api` as a container app.
- **Why** — the Azure CLI deploys a template but does not build and push a
  container image; `azd` does both from one service definition, so the workflow
  does not have to hand-roll a build-tag-push-update sequence around it. The
  foundry deployment stays on the Azure CLI: it deploys no code, so the half of
  `azd` that earns its keep here would be unused there.
- **How** — resource-group scoped, so `azd` deploys into a group created by hand
  rather than creating one. `azd auth login --federated-credential-provider
  github` means the workflow stores no secret; the runner mints a short-lived
  OIDC token instead. See `docs/deployment/sync.md`.

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
  reference, then regenerates and diffs the committed indexes and reports drift as
  a *warning* only: making every pull request carry a regenerated index is what
  turned these files into merge conflicts. Refresh is deliberate instead —
  `build/Update-KnowledgeIndex.ps1` on demand,
  `.github/workflows/knowledge-meta-nightly.yml` on a schedule. The drift step runs
  the generator rather than trusting `--check`, because `--check` misses
  line-number drift. `--check` says nothing about the *values* in a `meta` block,
  though, so a second hard failure covers those:
  `.github/workflows/knowledge-metadata.yml` runs
  `tools/knowledge/check-metadata.mjs`, this repository's own caller of the
  generator's exported `validateDocument`, and a status outside a folder's ladder,
  an unknown `.domain` `type` or a field no schema defines fails the pull request.
  It is a separate script and a separate workflow because the generator, both
  `knowledge-meta*` workflows and `Update-KnowledgeIndex.ps1` are installed copies
  of the plugin's tooling, re-synced rather than edited here.
- **Caveat** — the installed generator is four plugin releases behind, and the
  check is pinned to it. Two consequences, both listed as *pending re-sync* in its
  report rather than hidden: `.tech` `type` values go unvalidated, because the
  installed copy still expects the pre-rename `kind` and cannot judge the new
  field; and `type`, `date`, `tests`, `index` and `number` are exempt everywhere,
  because chapter authors write the current schema while the validator knows the
  old one. Re-syncing the generator is what retires both.

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

## c4hero

```meta
status: adopted
type: tool
version: "0.4.0"
depends-on: [".tech/shared.md#nodejs"]
related: [".tech/tooling.md#archify", ".design/component-libraries.md#c4-workspaces"]
```

A browser-based visual editor for C4 architecture diagrams that saves Structurizr
DSL. Apache-2.0. **Not vendored and not a runtime dependency** — see below.

- **Used for** — authoring `.arc42/_c4/*.dsl`, the C4 model kept beside the
  architecture chapters. The desktop app reads that DSL and explores it on the
  Architecture panel's C4 tab behind the `c4-diagrams` flag — Views panel,
  click-to-drill, breadcrumb, search, Highlighter, pan and zoom, minimap and
  presentation mode, over a picture the app draws itself. c4hero is what a person
  opens to change the model.
- **Why** — the model wanted a real editor and this repository wanted no new build.
  c4hero is local-first and saves plain `.dsl` through the File System Access API,
  so the authored artifact is reviewable text in git and the editor never has to be
  part of the product. Structurizr DSL rather than a format of our own because it
  is what Structurizr Lite, Studio and the JSON exporters already read.
- **How** — opened at `app.c4hero.com` or run from a clone (Node 22+), pointed at
  the `_c4` folder. Nothing here installs it, vendors it, or shells out to it: it
  has no CLI, and the app reads the DSL with its own reader
  (`src/Core/Backlog.UI.Components/Diagrams/C4/`) rather than through anything
  c4hero ships. That reader is therefore a second implementation of c4hero's
  dialect, pinned against c4hero's own conformance fixture and required to report
  rather than guess — `tools/diagrams/C4.md` says why.


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
