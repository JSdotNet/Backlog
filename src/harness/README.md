# Harness

**Nothing in this folder is deployed.**

These are Blazor Server hosts that exist for one reason: the shipped UI lives in
.NET MAUI Blazor Hybrid heads (`src/App/Backlog.Desktop`, `src/App/Backlog.Mobile`),
and a MAUI head cannot be started as an Aspire project resource on CI or driven by
Playwright. Each harness hosts the *same* Razor component library the real head
renders, so the UI can be run, inspected, and tested without a desktop session or
an Android emulator.

| Harness | Hosts | Aspire resource |
|---|---|---|
| `Backlog.Desktop.WebHarness` | `src/App/Backlog.Desktop.UI` | `desktop-web-harness` |
| `Backlog.Mobile.WebHarness` | `src/App/Backlog.Mobile.UI` (phone width) | `mobile-web-harness` |
| `Backlog.UI.Storybook` | `src/UI/Backlog.UI.Components` on its own | `ui-storybook` |

`Backlog.UI.Storybook` is the odd one out: it hosts no app UI. It references the shared
component library and `Backlog.Aspire.ServiceDefaults` and nothing else, so it renders every
component with realistic content and no domain behind it. That missing reference is load-bearing
— if a component ever grew a dependency on a module, the storybook would stop compiling.
Use it to review a component, and to test one without starting the application.

## Rules

- A harness may reference shipping projects under `src/`. **No shipping project may reference a
  harness.** `tests/Backlog.ArchitectureTests` fails the build if that is violated.
- No feature may live here. A harness contains only hosting glue: `Program.cs`,
  a root `App.razor`, and configuration. If behaviour is worth testing, it belongs
  in the component library or the module.
- `Directory.Build.props` in this folder marks every project `IsPublishable=false`,
  `IsPackable=false`, and `IsShippingAssembly=false`, so publishing one fails.
- The release workflow only publishes from `src/App/`.

## Why it is under `src/` but not `tests/`

`tests/` holds projects that a test runner executes. A harness is a long-running
host that other tools point a browser at, so it lives under `src/harness/` with the
rest of the runnable development projects while remaining outside shipped app folders.
