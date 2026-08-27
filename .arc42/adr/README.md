# Architecture Decision Records

```meta
status: active
related: [".arc42/09-architecture-decisions.md", ".arc42/adr/guidelines/README.md"]
issue: null
```

The decisions this architecture is built on. `.arc42/09-architecture-decisions.md`
says which one governs which part of the system; this folder holds the records
themselves.

They come from two authors, and are kept apart for that reason alone:

| Where | What | Numbering |
|---|---|---|
| `*.md` here | Decisions **Backlog took for itself** — specific to this product, taken by this repository. | Local, from 0001. |
| `guidelines/` | Decisions **Backlog inherited** from the organization, imported on 2026-08-27 and authoritative here since. | The organization's, from 0001. |

Both sequences start at 0001, so **always say which set you mean**: inherited ADR
0003 is Aspire, local ADR 0003 is SQLite as the canonical store.

A local ADR may deliberately override an inherited one. When it does it says so —
see `0002-backlog-module-owns-the-entry-text-language.md`, which takes a position
between inherited ADRs 0005 and 0009.

## Local decisions

- **[0001 — Desktop channel uses .NET MAUI Blazor Hybrid, not plain WinUI 3](0001-desktop-stack-maui-blazor-hybrid.md)**
- **[0002 — The Backlog module owns the entry text language](0002-backlog-module-owns-the-entry-text-language.md)**
- **[0003 — SQLite is the canonical local task store; markdown is the content](0003-sqlite-is-the-canonical-local-task-store.md)**

## Inherited decisions

Indexed in **[guidelines/README.md](guidelines/README.md)**, which also records
what was deliberately *not* imported and why. Each document there ends with a
**Deviations and gaps** section stating where Backlog actually stands against the
rule — read that before planning work in its area.
