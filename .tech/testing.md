# Testing Stack

```meta
status: adopted
related: [".tech/technology-graph.md", ".arc42/10-quality-requirements.md"]
```

> How the solution is tested and validated. This layer is fully `adopted`: every
> technology here runs on every pull request, either in `dotnet test` or in the
> QA validation phase of an orchestration run.

## xUnit v3

```meta
status: adopted
type: framework
version: "4.0.0"
depends-on: [".tech/shared.md#net-runtime", ".tech/shared.md#c-language"]
related: [".tech/testing.md#microsofttestingplatform"]
```

The unit-test framework for all eleven test projects.

- **Used for** — module, infrastructure, UI-component, and architecture tests.
  `Backlog.ArchitectureTests` uses it to enforce repository rules that no
  compiler can — shared-control adoption, and the fence keeping `src/Harness/`
  out of shipping code.
- **Why** — the default .NET test framework, and v3 is what integrates with
  Microsoft.Testing.Platform.
- **How** — v3 runs each test assembly as its own process, so every test project
  sets `OutputType=Exe`; the package set and the `Xunit` global using are
  declared once in `tests/Directory.Build.props`. The v3 analyzer `xUnit1051` is
  silenced there deliberately, so adopting `TestContext.Current.CancellationToken`
  stays its own change rather than a rewrite of every await in the suite.

## Microsoft.Testing.Platform

```meta
status: adopted
type: framework
depends-on: [".tech/testing.md#xunit-v3"]
related: [".tech/tooling.md#net-sdk"]
```

The test host that replaces VSTest for this solution.

- **Used for** — running the suite. `global.json` selects it
  (`"test": { "runner": "Microsoft.Testing.Platform" }`), which changes the
  command-line contract of `dotnet test`: the solution is named with
  `--solution`, and options are the platform's rather than VSTest's.
- **Why** — it is what xUnit v3 targets, and it makes each test assembly a
  self-contained executable.

## Microsoft.NET.Test.Sdk

```meta
status: adopted
type: package
version: "18.9.0"
depends-on: [".tech/testing.md#microsofttestingplatform"]
```

The MSBuild targets that make a project a test project.

- **Used for** — every project under `tests/`, referenced once from
  `tests/Directory.Build.props`.
- **Why** — required for discovery and for the `dotnet test` entry point.

## xunit.runner.visualstudio

```meta
status: adopted
type: package
version: "4.0.0"
depends-on: [".tech/testing.md#xunit-v3"]
```

The test adapter that surfaces xUnit tests to IDE test explorers.

- **Used for** — running and debugging individual tests from Visual Studio, VS
  Code, and Rider.
- **Why** — the suite has to be runnable from the editor, not only from CI.

## Microsoft.Testing.Extensions.TrxReport

```meta
status: adopted
type: package
version: "2.3.3"
depends-on: [".tech/testing.md#microsofttestingplatform"]
related: [".tech/tooling.md#github-actions"]
```

The TRX report writer.

- **Used for** — the test-results artifact the pull-request workflow uploads.
- **Why** — under Microsoft.Testing.Platform the reporter is an opt-in extension
  rather than a logger built into the runner, so the `--report-trx` option only
  exists because this package is referenced.

## bUnit

```meta
status: adopted
type: framework
version: "2.9.0"
depends-on: [".tech/shared.md#razor-components", ".tech/testing.md#xunit-v3"]
related: [".design/component-libraries.md"]
```

The Razor component test framework.

- **Used for** — `Backlog.UI.Components.UnitTests`,
  `Backlog.Desktop.UI.UnitTests`, and `Backlog.Mobile.UI.UnitTests`: rendering a
  component in isolation and asserting on the produced markup.
- **Why** — it tests the component layer without a browser, so component
  behaviour is pinned in `dotnet test` and Playwright is reserved for real
  end-to-end flows.

## coverlet

```meta
status: adopted
type: tool
version: "10.0.1"
depends-on: [".tech/testing.md#microsoftnettestsdk"]
```

The code-coverage collector.

- **Used for** — coverage collection during `dotnet test`, referenced from
  `tests/Directory.Build.props`.
- **Why** — the standard cross-platform collector for .NET; no separate
  instrumentation step.

## Playwright

```meta
status: adopted
type: tool
depends-on: [".tech/shared.md#blazor-server", ".tech/desktop.md#webview2"]
related: [".tech/ai-development.md#model-context-protocol-servers", ".arc42/adr/0001-desktop-stack-maui-blazor-hybrid.md", ".tech/shared.md#net-aspire"]
```

Browser automation for end-to-end validation.

- **Used for** — the QA Validation phase of a code-modifying orchestration run:
  driving the Blazor Server harnesses and the storybook, capturing screenshots
  and video as evidence, and attaching to the desktop head over WebView2's CDP
  debugging port.
- **Why** — being Playwright-drivable is one of the two reasons ADR 0001 chose
  MAUI Blazor Hybrid over plain WinUI 3. It reaches this repository as an MCP
  server supplied by the `qa` plugin, not as a checked-in dependency, so there
  is no Playwright package or config in the solution.
