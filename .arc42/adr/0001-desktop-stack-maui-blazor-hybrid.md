# ADR 0001: Desktop channel uses .NET MAUI Blazor Hybrid, not plain WinUI 3

```meta
status: active
related: [".arc42/04-solution-strategy.md", ".arc42/02-constraints.md", ".arc42/09-architecture-decisions.md"]
issue: null
```

## Status

Accepted — supersedes the initial "WinUI 3 (preferred, C#)" desktop choice recorded
in `.arc42/04-solution-strategy.md` (Technology Choices).

## Context

The Backlog desktop channel was first scaffolded as a plain WinUI 3 MVVM app
(`Backlog.Desktop`), and a first feature slice (entry CRUD, lifecycle, sub-items,
local-first markdown+JSON persistence) was built and validated end-to-end.

A new goal was introduced: the whole application stack (desktop client + optional
cloud API) should be startable as one unit from a single **.NET Aspire AppHost**,
and end-to-end scenarios should be testable with **Playwright**, matching the
tooling already used for the cloud/web side (`qa:qa`'s `playwright-validation`
skill).

Plain WinUI 3 cannot satisfy this cleanly:

- Aspire has no first-class resource type for a windowed native app; a WinUI exe can
  only be added as a generic `AddExecutable` resource.
- WinUI has no Chromium/CDP-based UI surface, so it cannot be driven by Playwright.
  Automated UI testing requires WinAppDriver/UI Automation instead — a different,
  less-uniform tooling path than the rest of the system.

At the same time, the system's local-first architecture constraints
(`.arc42/02-constraints.md`) must still hold:

- Markdown is the canonical storage format, read/written directly on the local
  filesystem.
- Capture/background workers (YouTube, website, email polling) run natively on the
  desktop so external credentials never leave the machine.
- Core workflows must work fully offline.

A pure browser-based rewrite (e.g. a standalone Blazor WebAssembly PWA) was
considered and rejected: browser-sandboxed storage (IndexedDB / File System Access
API grants) does not cleanly satisfy "markdown is canonical on disk" without
re-opening constraints, and browsers cannot host persistent native background
workers holding local credentials.

## Decision Drivers

- Aspire must be able to start the whole stack (desktop + optional cloud API) as
  one orchestrated unit.
- End-to-end tests must be automatable with Playwright, consistent with the rest of
  the system's QA tooling (`qa:qa`).
- Local-first constraints (`.arc42/02-constraints.md`) — canonical markdown on disk,
  native background workers, full offline capability — must not be weakened.
- Reuse across channels is a plus: `.arc42/04-solution-strategy.md` already selects
  **.NET MAUI** as the preferred Mobile stack.

## Considered Options

1. **Plain WinUI 3** (original choice) — native, full local-first compliance, but
   only WinAppDriver-style automation; awkward Aspire orchestration via
   `AddExecutable` only.
2. **Standalone Blazor WebAssembly PWA** — first-class Aspire resource, native
   Playwright support, installable as a desktop PWA — but runs sandboxed in the
   browser: no native background workers, and file access is limited to
   user-granted directories via the File System Access API (Chromium-only),
   weakening the "markdown is canonical on disk" and "native background worker"
   constraints.
3. **.NET MAUI Blazor Hybrid (selected)** — MAUI native shell (WinUI 3 head on
   Windows) hosting Blazor Razor Components in an embedded WebView2. Runs as a
   full native process: unrestricted local filesystem access, native background
   workers, fully offline-capable — identical guarantees to plain WinUI 3. WebView2
   is Chromium-based and exposes a CDP remote-debugging port, so Playwright can
   attach via `connectOverCDP` to drive the UI, and the process can be launched
   from an Aspire AppHost as a project/executable resource like any other.

## Decision

Adopt **Option 3: .NET MAUI Blazor Hybrid** for the Backlog desktop channel,
replacing the plain WinUI 3 app.

- `Backlog.Domain` and `Backlog.Storage` are unaffected — they are plain,
  UI-framework-agnostic C# libraries and are reused as-is.
- `Backlog.Desktop` is rebuilt as a MAUI Blazor Hybrid project targeting Windows
  (WinUI 3 head), rendering its UI as Razor components in the embedded WebView2.
  Interactive elements use stable `id`/`data-testid` attributes (replacing
  `AutomationProperties.AutomationId`) so Playwright can drive them.
- The desktop app is added to the Aspire AppHost as a launchable resource
  alongside the optional cloud API, so the whole stack can be started from one
  entry point.
- This also aligns the Desktop channel with the already-selected Mobile stack
  (.NET MAUI in `.arc42/04-solution-strategy.md`), enabling future UI/component
  reuse between Desktop and Mobile.

## Consequences

**Positive**

- Aspire can launch and coordinate the desktop client and the cloud API together.
- End-to-end UI testing is unified on Playwright across desktop and any future web
  surface, consistent with existing `qa:qa` tooling.
- All local-first constraints (canonical markdown on disk, native background
  workers, full offline capability) are preserved — MAUI's Windows head is WinUI 3,
  so the native guarantees are identical to the original choice.
- Opens a path to share Razor UI components between Desktop and Mobile.

**Negative / Risks**

- More moving parts than plain WinUI 3: MAUI workload, Blazor Hybrid wiring, and an
  explicit WebView2 remote-debugging-port configuration are required for Playwright
  to attach.
- The already-built first-version WinUI 3 app (`Backlog.Desktop`) must be
  reimplemented as MAUI Blazor Hybrid; only `Backlog.Domain`/`Backlog.Storage` and
  the feature scope carry over unchanged.
- MAUI on Windows remains a thin abstraction over WinUI 3, not a different runtime,
  so packaging/deployment characteristics are broadly similar to before.

**Rollback**

If MAUI Blazor Hybrid proves unworkable (e.g. WebView2 CDP attach proves unreliable
in practice, or MAUI tooling friction outweighs the Aspire/Playwright benefit), the
system can revert to plain WinUI 3 for `Backlog.Desktop` with no impact on
`Backlog.Domain`/`Backlog.Storage`, at the cost of reverting to WinAppDriver-based
e2e testing and dropping single-AppHost startup for the desktop client.

## Links

- `.arc42/04-solution-strategy.md` — Technology Choices table (Desktop row updated
  to reference this ADR).
- `.arc42/02-constraints.md` — local-first, markdown-canonical, native
  background-worker constraints this decision preserves.
- `.arc42/09-architecture-decisions.md` — local system decisions section.
