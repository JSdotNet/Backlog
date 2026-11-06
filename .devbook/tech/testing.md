# Testing Stack

```meta
rating: adopted
related: [".devbook/tech/technology-graph.md", ".devbook/arc42/10-quality-requirements.md"]
```

> This layer is fully `adopted`: every technology here runs on every pull
> request, in `dotnet test` or the `phase-verify` stage of a `flow-*` run.

## xUnit v3

```meta
rating: adopted
type: framework
version: "4.0.1"
depends-on: [".devbook/tech/shared.md#net-runtime", ".devbook/tech/shared.md#c-language"]
related: [".devbook/tech/testing.md#microsofttestingplatform"]
```

The unit-test framework for every test project, referenced once in
`tests/Directory.Build.props`.

- **Used for** — these kinds of test:
  - module
  - infrastructure
  - UI-component
  - architecture

  `Backlog.ArchitectureTests` enforces repository rules no compiler can, such as
  shared-control adoption and the fence keeping `src/Harness/` out of shipping
  code.
- **Why** — the default .NET test framework, and v3 integrates with
  Microsoft.Testing.Platform.
- **How** — v3 runs each test assembly as its own process, so every test project
  sets `OutputType=Exe`. The package set and the `Xunit` global using are
  declared once in `tests/Directory.Build.props`.

## Microsoft.Testing.Platform

```meta
rating: adopted
type: framework
depends-on: [".devbook/tech/testing.md#xunit-v3"]
related: [".devbook/tech/tooling.md#net-sdk"]
```

The test host that replaces VSTest for this solution.

- **Used for** — running the suite. `global.json` selects it
  (`"test": { "runner": "Microsoft.Testing.Platform" }`), which changes the
  command-line contract of `dotnet test`. The solution is named with
  `--solution`, and options are the platform's, not VSTest's.
- **Why** — xUnit v3 targets it, and it makes each test assembly a self-contained
  executable.

## Microsoft.NET.Test.Sdk

```meta
rating: adopted
type: package
version: "18.9.0"
depends-on: [".devbook/tech/testing.md#microsofttestingplatform"]
```

The MSBuild targets that make a project a test project.

- **Used for** — every project under `tests/`, referenced once from
  `tests/Directory.Build.props`.
- **Why** — required for discovery and for the `dotnet test` entry point.

## xunit.runner.visualstudio

```meta
rating: adopted
type: package
version: "4.0.0"
depends-on: [".devbook/tech/testing.md#xunit-v3"]
```

The test adapter that surfaces xUnit tests to IDE test explorers.

- **Used for** — running and debugging individual tests from Visual Studio, VS
  Code, and Rider.
- **Why** — the suite has to be runnable from the editor, not only from CI.

## Microsoft.Testing.Extensions.TrxReport

```meta
rating: adopted
type: package
version: "2.4.1"
depends-on: [".devbook/tech/testing.md#microsofttestingplatform"]
related: [".devbook/tech/tooling.md#github-actions"]
```

The TRX report writer.

- **Used for** — the test-results artifact the pull-request workflow uploads.
- **Why** — under Microsoft.Testing.Platform the reporter is an opt-in extension,
  so `--report-trx` exists only because this package is referenced.

## Microsoft.Testing.Extensions.CodeCoverage

```meta
rating: adopted
type: package
version: "18.11.2"
depends-on: [".devbook/tech/testing.md#microsofttestingplatform"]
related: [".devbook/tech/tooling.md#github-actions"]
```

The code-coverage collector, referenced from `tests/Directory.Build.props`.

- **Used for** — the Cobertura report each test module writes in the
  pull-request run, uploaded as an artifact and merged into the line-coverage
  figure on the run summary.
- **Why** — like the TRX reporter, coverage is an opt-in extension, and the
  `--coverage` options exist only because this package is referenced. Coverlet's
  collector is a VSTest data collector the platform never loads.

## bUnit

```meta
rating: adopted
type: framework
version: "2.11.3"
depends-on: [".devbook/tech/shared.md#razor-components", ".devbook/tech/testing.md#xunit-v3"]
related: [".devbook/design/component-libraries.md"]
```

The Razor component test framework.

- **Used for** — `Backlog.UI.Components.UnitTests`,
  `Backlog.Desktop.UI.UnitTests`, and `Backlog.Mobile.UI.UnitTests`: rendering a
  component in isolation and asserting on the produced markup.
- **Why** — it tests components without a browser, so their behaviour is pinned
  in `dotnet test` and Playwright is reserved for end-to-end flows.

## Microsoft.AspNetCore.Mvc.Testing

```meta
rating: adopted
type: package
version: "10.0.11"
depends-on: [".devbook/tech/shared.md#aspnet-core", ".devbook/tech/testing.md#xunit-v3"]
```

In-memory hosting of an ASP.NET Core app under test.

- **Used for** — `WebApplicationFactory` in `Backlog.Modules.Sync.Api.UnitTests`
  and `Backlog.HostComposition.UnitTests`, which exercise the real pipeline
  (authentication, routing, filters) without a socket.
- **Why** — the first-party test host. An endpoint test crosses the same
  middleware a deployed request does.

## Microsoft.Extensions.TimeProvider.Testing

```meta
rating: adopted
type: package
version: "10.10.0"
depends-on: [".devbook/tech/shared.md#net-runtime"]
```

`FakeTimeProvider`, a clock a test moves by hand.

- **Used for** — unit-test projects whose code takes a `TimeProvider`:
  - token lifetimes
  - sync cycles
  - deferral and resurfacing
  - roadmap dates
- **Why** — time-dependent behaviour is asserted without sleeping or flaking on
  a slow machine.

## Playwright

```meta
rating: adopted
type: tool
version: "1.63.0"
depends-on: [".devbook/tech/shared.md#blazor-server", ".devbook/tech/desktop.md#webview2"]
related: [".devbook/tech/ai-development.md#model-context-protocol-servers", ".devbook/arc42/adr/0001-desktop-stack-maui-blazor-hybrid.md", ".devbook/tech/shared.md#net-aspire", ".devbook/tech/testing.md#xunit-v3"]
```

Browser automation for end-to-end validation.

- **Used for** —
  - The `phase-verify` stage of a code-modifying `flow-*` run. It drives the Blazor
    Server harnesses and the storybook, and captures screenshots and video as
    evidence. It attaches to the desktop head over WebView2's Chrome DevTools
    Protocol (CDP) debugging port.
  - The checked-in end-to-end tests in `tests/Backlog.EndToEndTests`. They use
    `Microsoft.Playwright` from xUnit against the harnesses of a running AppHost, and
    reach that AppHost through the Aspire CLI. They skip unless `BACKLOG_E2E=1`.
- **Why** — being Playwright-drivable is one of the two reasons ADR 0001 chose
  MAUI Blazor Hybrid over plain WinUI 3.
  - QA reaches it as an MCP server supplied by the `qa` plugin.
  - A scenario worth repeating is checked in as a test, because an MCP walk leaves
    evidence behind but nothing to run again.
