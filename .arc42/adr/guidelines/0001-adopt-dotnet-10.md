# ADR 0001: .NET 10 as the target framework

```meta
status: active
related: [".tech/shared.md", ".arc42/09-architecture-decisions.md"]
issue: null
```

Inherited from the organization's ADR 0001 (decided 2026-06-02,
`guide/adrs/0001-adopt-dotnet-10.md`), imported 2026-08-27.

## Decision

`net10.0` is the baseline target framework for every .NET project. .NET 10 is an
LTS release with three years of support, and it carries the C# 14 language
features the code is written against. Reassess when .NET 12 LTS ships, by a
superseding decision rather than a silent bump.

The organization prefers `.slnx` for solution files, with `.sln` allowed as a
compatibility fallback. That preference does not change the framework baseline.

## How Backlog applies it

- The root `Directory.Build.props` sets `<TargetFramework>net10.0</TargetFramework>`
  once for the solution.
- The MAUI heads (`Backlog.Desktop`, `Backlog.Mobile`) override it with their
  platform TFMs — `net10.0-windows…`, `net10.0-android` — which is the same
  baseline expressed per platform, not an exception to it.
- The VS Code extension (`src/App/Backlog.Ide.VsCode`) is TypeScript and sits
  outside this decision entirely.

## Deviations and gaps

- The solution is `Backlog.sln`, not `Backlog.slnx`. The `.sln` fallback the
  decision allows; converting is a mechanical change nobody has needed yet.
