# ADR 0002: Central Package Management

```meta
status: active
related: [".arc42/09-architecture-decisions.md"]
issue: null
```

Inherited from the organization's ADR 0002 (decided 2026-05-28,
`guide/adrs/0002-central-package-management.md`), imported 2026-08-27.

## Decision

1. Every package version is declared once, centrally, in a root
   `Directory.Packages.props`.
2. Project files reference packages **by name only** — no local `Version`
   attribute.
3. A new package is added with a central `PackageVersion` entry in the same
   change.
4. Upgrades happen centrally and are validated across every project they touch.
5. A project-local version override is rare, justified, and carries a short
   rationale comment in the project file. It is an exception, not a convenience.

Shared build defaults (`TargetFramework`, `LangVersion`) belong in
`Directory.Build.props`; a subtree that needs its own default package set gets a
child `Directory.Build.props` that imports the root one.

## How Backlog applies it

- `Directory.Packages.props` at the repository root is the single version
  catalog, with `ManagePackageVersionsCentrally` on.
- `tests/Directory.Build.props` carries the test-only package defaults so test
  dependencies do not leak into production projects.
- `$(MauiVersion)` comes from the root `Directory.Build.props`, which
  `Microsoft.Common.props` imports just before the packages file.

## Deviations and gaps

- **Transitive pinning is deliberately off.** Turning it on rewrote versions the
  solution never asked for — the AppHost's transitive YamlDotNet 16.3.0,
  OpenTelemetry 1.15.3, and `Microsoft.Extensions.*` 10.0.8 were each lifted to
  whatever `Directory.Packages.props` declares for that name — purely because
  those names appear as direct versions. Those targets move with every package
  bump, so they are not recorded here; read them from the packages file. Adopting
  CPM was meant to change *where* versions are declared, not *which* ones
  resolve. The rationale is repeated in a comment at the top of
  `Directory.Packages.props`.
- `.arc42/09-architecture-decisions.md` recorded this decision as "not adopted"
  until 2026-08-27. That was stale; CPM is adopted.
