# ADR 0061: Repository automation stays PowerShell and Node, not C# file-based apps

```meta
date: 2026-11-25
related: [".devbook/arc42/adr/guidelines/README.md", ".devbook/arc42/09-architecture-decisions.md", ".devbook/arc42/adr/0016-knowledge-folders-adopt-the-devbook-convention.md", ".devbook/tech/tooling.md"]
c4: [backlog]
```

Backlog keeps its automation in `build/*.ps1` and `tools/**/*.mjs`. It adds no
`scripts/` folder of C# file-based apps, which the **Scripts** rule of org ADR 0001
asks for.

## Status

```meta
```

Proposed, 2026-11-25. Plan `app-defaults-rollout`, entry 37 (`backlog-deviation-adrs`).

A **local** decision. It records a deliberate difference from
[org ADR 0001: Platform and Build](https://github.com/JSdotNet/app-defaults/blob/main/defaults/arc42/adr/0001-platform-and-build.md),
so a later review of the app defaults reads it as decided and does not report it again.
The rest of org ADR 0001 still applies.

## Context

```meta
```

Org ADR 0001 says every piece of repository automation is a `verb-noun.cs` in
`scripts/`, run with `dotnet run`. Backlog has no `scripts/` folder. Its automation
is in two languages, and each one has a reason to stay:

| Where | What | Why this language |
| --- | --- | --- |
| `build/*.ps1` (8 scripts) | Release versioning, MSIX app installer, Android install, the Azure deploy, the self-hosted runner, stopping Aspire before a pull request | Windows-only steps that call `signtool`, `adb`, `az` and `azd`. They run in the CI jobs' PowerShell shells and on the person's own machine |
| `tools/devbook/*.mjs` | `build-database.mjs`, `check-metadata.mjs`, `brain.mjs`, the schema | `DevbookBuilderParityTests` holds the C# builder to `build-database.mjs`. The devbook plugin's own tooling is Node, and a devbook release lands as a Node diff that is ported here |
| `tools/mcp/backlog-mcp-headers.mjs` | The `headersHelper` that asks the token broker for a grant's token (local ADR 0058 §3) | Claude Code runs it at every connect. A Node process starts in tens of milliseconds, and a `dotnet run` of a file-based app takes seconds the first time |

Each of these scripts has a `*.test.mjs` or a C# test beside it.

## Decision

```meta
```

**Keep.** Backlog's automation stays PowerShell for the Windows build and release
steps and Node for the devbook and MCP tooling. No `scripts/` folder is created, and
nothing is ported.

A new script that is neither Windows release glue nor a counterpart of devbook's or
Claude Code's Node tooling is written as a C# file-based app in `scripts/`, as org
ADR 0001 says. The first one creates the folder and its `Directory.Build.props`.

## Consequences

```meta
```

- A contributor needs PowerShell 7 and Node 22.5 or later as well as the .NET SDK.
  The CI images already have all three.
- The parity test keeps the Node reference and the C# builder side by side. A port
  to C# would leave the reference without a twin in its own language.
- An app-defaults review reads this record for the 0001 Scripts row.

## Rejected

```meta
```

- **Port every script to C# (L).** That would rewrite 11 Node files and 8 PowerShell
  scripts, and break the one-to-one diff against devbook's Node tooling. The
  `headersHelper` would start slower on every MCP connect. No defect today comes from
  the language the scripts are written in.

## History

```meta
```

| Date | Change | Why |
| --- | --- | --- |
| 2026-11-25 | Proposed: keep PowerShell and Node; a new script outside those two roles is a C# file-based app in `scripts/`. | Review entry 22 of plan `app-defaults-rollout` found the org ADR 0001 Scripts difference with no local decision. |
