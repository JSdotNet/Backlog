# ADR 0005: Modular monolith structure

```meta
status: active
related: [".arc42/05-building-block-view.md", ".domain/context-map.md", ".arc42/09-architecture-decisions.md"]
issue: null
```

Inherited from the organization's ADR 0005 (decided 2025-11-10,
`guide/adrs/0005-modular-monolith-structure.md`), imported 2026-08-27.

## Decision

Each domain module is its own folder and set of projects, forming an internal
boundary — module boundaries without distributed-system cost.

`src/` is organized as:

| Folder | Purpose |
|---|---|
| `App/` | The frontend applications |
| `Aspire/` | Aspire orchestration projects (see [0003](0003-aspire-for-web-services.md)) |
| `Core/` | Shared CQRS interfaces and cross-cutting abstractions every module uses |
| the module folders | One per domain module / bounded context |

A module folder holds the module project (domain plus feature slices), an
`.Abstractions` project (its published contracts), optionally an `.Api` host, and
optionally a data adapter project. Test projects live under the root `tests/`.

Boundary rules:

- No cross-module domain entity sharing — cross a boundary with a DTO from
  `.Abstractions`.
- Infrastructure types never leak out of the data adapter.
- Domain projects stay persistence-agnostic: no ORM attributes, no SDK
  references.
- Inter-module communication goes through `.Abstractions` interfaces.
- **Repository interfaces live in the module implementation project**, not in
  `.Abstractions`. They are internal persistence ports; only cross-module service
  contracts are published.
- Each module wires itself up through one registration extension method.

Start simple: introduce `.Abstractions` when another module actually needs a
stable contract, not before.

## How Backlog applies it

- `src/App/` holds the four channels (`Backlog.Desktop`, `Backlog.Mobile`, their
  `.UI` render libraries, and `Backlog.Ide.VsCode`); `src/Aspire/` the AppHost and
  ServiceDefaults; `src/Core/` the `Backlog.SharedKernel` and the shared
  `Backlog.UI.Components` library.
- Modules live under `src/Modules/`, named `Backlog.Modules.<Module>` plus the
  `.Abstractions`, `.UI`, and `.Api` projects each one needs.
- Boundaries are **enforced by tests**, not by review alone:
  `tests/Backlog.ArchitectureTests/ModuleBoundaryTests.cs`,
  `ModuleSurfaceTests.cs`, `DesktopDomainBoundaryTests.cs`, and
  `UiLibraryBoundaryTests.cs`.
- `Backlog.Modules.Tasks` shows the intended shape end to end: domain models,
  feature slices, a repository port at the module root, and a published
  `.Abstractions` surface — see
  `.arc42/adr/0002-backlog-module-owns-the-entry-text-language.md`.

## Deviations and gaps

- **Modules are nested under `src/Modules/`** rather than sitting at the top of
  `src/`. Deliberate: with eight modules plus `App`, `Aspire`, `Core`, and
  `Infrastructure`, a flat `src/` stops being readable.
- **`src/Infrastructure/` is a fifth top-level folder** the organization's layout
  does not name. Adapters that serve several modules — Sqlite, GitHub, Claude,
  Copilot, FileSystem, AzureFoundry — live there instead of as per-module
  `Data.*` projects. See
  [0014](0014-persistence-and-repository-boundaries.md).
- **Each module carries a `.UI` project.** The organization's layout assumes a
  web frontend calling module APIs; Backlog renders module screens in-process
  inside the MAUI hosts, so a module publishes Razor components alongside its
  contracts.
- `Inbox` and `Knowledge` have `.UI` projects (and Knowledge an `.Abstractions`)
  but no module implementation project yet — their logic still sits in the UI
  layer. That is the shape local ADR 0002 corrected for the Tasks module and
  has not yet corrected here.
- Only `Sync` has an `.Api` project. The other modules are in-process and need no
  HTTP host.
