---
name: debug
description: "Find the cause of an issue observed in this repository's running application — read its logs, traces, and console, reproduce it, and set a breakpoint where a log cannot say. Use when: 'why does this fail', 'debug it', a reproduction is needed, an exception or a wrong value has no obvious origin, or a flow's defect stage needs a root cause."
goal: "Name the cause of an observed issue and prove it with evidence — the log line, the trace span, or the breakpoint state that shows it — doing the debugging yourself rather than handing the person a debugger, and leaving no breakpoint, diagnostic line, or temporary setting behind in the change."
---

# Debug the Application

Go from a symptom to a cause with the evidence attached. The web harnesses are debugged
through Aspire's logs and traces. The installed desktop app is debugged through its WebView2
DevTools endpoint.

## Reproduce

1. Decide where the symptom lives.
   - A symptom seen in a harness, or in shared code, is reproduced against this worktree's
     AppHost. Invoke the `start` skill and reuse the instance it reports.
   - A symptom seen only in the installed desktop app is read from that app over CDP (see
     below) before theorising from the web harness.
2. Reproduce it once, the narrowest way that shows it: one request, one click, or one
   command.
3. Note the exact time; every query below is windowed around it.

## Observe

Use the Aspire MCP tools, whose prefix you resolve from the live tool list.

- **Check the AppHost first.** Call `list_apphosts` and confirm that this worktree's AppHost
  is in scope. The MCP otherwise answers about whichever AppHost it last selected, which can
  be another worktree's.
- `select_apphost` it when more than one is running.

| Question | Query |
| --- | --- |
| Which resource is unhealthy? | `list_resources`: state, health and the last exit code per resource |
| What did it print? | `list_console_logs` with `resourceName`, or `aspire logs <resource> --apphost src/Aspire/Backlog.Aspire.AppHost/Backlog.Aspire.AppHost.csproj` |
| Which log line is the error? | `list_structured_logs` with `resourceName` and a `search` term, for example the exception type, a route, or an entry id |
| Where did the request go? | `list_traces` with `resourceName`, then `list_trace_structured_logs` with the `traceId` of the failing span |
| What does a person see? | The Aspire dashboard `start` reports: the same data, when you need to show it rather than quote it |
| What did the browser see? | The browser tool's console and network requests, against `desktop-web-harness` or `mobile-web-harness` |
| What is on disk? | `%LOCALAPPDATA%\Backlog.Debug`: `backlog.db`, `devbook-cache`, `activity-cache` |

The `%LOCALAPPDATA%\Backlog.Debug` folder is shared by every worktree on the machine, so
copy a file before you take any step that could change it.

- Read the window around the reproduction, not the whole log.
- Follow one request's trace end to end before reading a second.
- Quote the line or span that points at the cause.

Traces reach the dashboard from `sync`, the three harnesses, and the `desktop` resource when
Aspire started it. Two sources send nothing:

- The installed desktop app. Read it over CDP instead.
- The Android head, unless the run pins the dashboard's OTLP port.

## The installed desktop app: CDP on 9222

On Windows, every desktop build starts WebView2 with `--remote-debugging-port=9222`: the
installed MSIX, and the Aspire `desktop` resource alike (`MauiProgram.ConfigureWebView2RemoteDebugging`).

1. Check which build is answering.
   - Run `Get-Process Backlog.Desktop | Select-Object Path`. The installed app lives under
     `C:\Program Files\WindowsApps\JSdotNet.Backlog.Desktop_*`.
   - Map its version to a commit, and run `git merge-base --is-ancestor` to learn whether it
     contains the code in question.
   - Only one process can hold port 9222.
2. `GET http://localhost:9222/json/list` names the page and its `webSocketDebuggerUrl`.
3. Playwright MCP cannot attach to it. Write a scratchpad Node script (Node 22 has a global
   `WebSocket`) that sends `Runtime.evaluate` over that URL, and read the DOM and state of
   the pane the person is actually looking at.
4. Read first. Drive the app (click, type, change a setting) only when reading cannot answer
   the question. Driving it changes the person's real data.

## Break

When a log cannot tell you what a value was, find out yourself:

- **A debugger is available.** Attach with the debugger tool in the live tool list (a
  DAP-based MCP server, or the host's own debug tool) at the file and line the trace points
  at. Reproduce the symptom and read the locals.
- **No debugger tool is available.** Add one temporary diagnostic, a log line or an
  assertion, at that line. Rebuild, reproduce, and read it, then remove it before anything
  else happens, and say that you did.
  - A running harness locks the module DLLs, so a rebuild fails with `MSB3027`. Stop the
    affected harness resource with `execute_resource_command`, rebuild, and start it again:
    its proxied URL survives.
  - Never stop another worktree's AppHost.

Never ask the person to attach a debugger, set a breakpoint, or read a value for you.

## Restore

Put back everything this session changed, and list each item in the report:

- **Source.** Run `git diff` and `git status`. No diagnostic line, breakpoint file, `.env`
  change, `launchSettings.json` edit or debug flag may remain.
- **Feature flags and harness settings you toggled.** Examples: Settings → Features, the
  per-worktree harness settings under `obj/local-development/`, and a devbook folder
  override. Set each one back to the value it had before.
- **Installed desktop app, if it was driven over CDP.** Put back
  `%LOCALAPPDATA%\Backlog\github.json`, the repository registry, and any setting changed in
  the UI.
- **Resources you stopped or started for the investigation.** Return each to its prior
  state. Explicit-start channels go back to `NotStarted`.
- **Scratchpad scripts.** They stay in the scratchpad, never in the worktree.
- **`.qa-workspace/backlog.db` and `.qa-workspace/config/`.** If either changed, run
  `git checkout -- <path>`.

## Report

- The cause, in one sentence.
- The evidence that proves it: a quoted log line with its timestamp, a trace id and span,
  the CDP read-out, or the breakpoint's file, line and the values read.
- The reproduction steps.
- What a fix would touch.
- What was restored.

A cause without evidence is a hypothesis, and is reported as one.

## Never

- Change data or configuration to make the symptom go away instead of finding why it is
  there.
- Run destructive setup to reproduce: **Reset local data**, deleting from `Backlog.Debug`,
  or a Cosmos volume prune. Propose it instead.
- Leave the installed desktop app in a state the person did not put it in.
