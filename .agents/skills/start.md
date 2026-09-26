---
name: start
description: "Start this repository's application the way this repository says to, and leave it running. Use when: starting or running the app locally, 'start the app', 'run it', resuming work on a branch, or a flow needs a runtime at app.start."
goal: "Leave this repository's application running and healthy, and report the command that started it, the health verdict, and its entry points. Never hand the person a command to run themselves."
---

# Start the Application

Backlog runs through one Aspire AppHost. Start it with the command below, judge its health
from the Aspire MCP, and report the entry points the run actually bound.

## Run

```bash
aspire start --isolated --non-interactive --apphost src/Aspire/Backlog.Aspire.AppHost/Backlog.Aspire.AppHost.csproj
```

- Run it from the worktree root. It needs the .NET SDK and a running container runtime,
  because `cosmos` and `storage` are emulators.
- Always pass `--isolated`, in every worktree and every parallel session. It gives this run
  its own ports and user-secrets state. Other worktrees on this machine run their own AppHost
  at the same time.
- Use forward slashes in `--apphost`, and pass the same path to every later `aspire`
  command for this run.
- **Never trust the exit code.** `aspire start` detaches and exits `0` even when the AppHost
  failed to come up. The health verdict comes from the Aspire MCP (see Healthy).
- To stop this worktree's AppHost, run `aspire stop --apphost <same path>`. Never use
  `--all`, and never kill `dotnet`, `dcp` or `Aspire*` processes by name, because they may
  belong to another worktree. CLI details are in `.agents/skills/aspire/`.

1. **Check that it is not already running.** Call the Aspire MCP `list_apphosts` and read the
   list titled "App hosts within scope of working directory". If this worktree's AppHost is
   in it, reuse that one and say so. An AppHost from another worktree is not yours.
2. **Run the command** in the background. If it fails, report the failure. Never swap in
   `aspire run`, `dotnet run`, or a command without `--isolated`.
3. **Select this AppHost.** Call `select_apphost` with this worktree's AppHost path. It fails,
   listing what is available, when this AppHost never registered. That failure is the
   startup failure, whatever the exit code was.
4. **Wait for the signals under Healthy** by polling `list_resources`. Stop waiting on a
   `FailedToStart` or `Exited` for a resource that should run, or after two minutes with no
   state change. Never report a partly started app as healthy.
5. **Read the entry points from this run** (see Entry points). Every port changes on every
   start.

Report the command, the health verdict (listing each expected resource with its state), and
the entry-point URLs, in a few lines. Leave the app running: `show`, `debug`, and a flow's
later stages all use it.

## Healthy

- **Check the path before trusting a state.** On any resource in `list_resources`, check
  `project.path` or `executable.workDir`: it must be under this worktree. When no AppHost of
  this worktree is running, the Aspire MCP answers about whichever AppHost it last selected,
  with healthy states and real ports. If the MCP cannot see this `--isolated` AppHost, take
  the verdict from `aspire describe --apphost <same path>` instead, and say that you did.
- **Healthy** means `sync`, `azure-foundry-test`, `desktop-web-harness`,
  `mobile-web-harness` and `ui-storybook` are all `Running`. `desktop-web-harness` waits for
  `azure-foundry-test`, and `mobile-web-harness` waits for `sync`, so a short delay before
  they start is normal.
- **Not started is healthy.** `desktop`, `mobile-android`, `mobile-maui-android-emulator`,
  `mobile-tunnel`, `ide-vscode-build` and `ide-vscode-host` use `WithExplicitStart()`, so
  sitting `NotStarted` is the healthy state, not a failure. Start one only when the task
  needs that channel.
- **Absent is healthy.** `foundry-local` is registered only when the `foundry` CLI is on
  PATH, so on most machines it does not appear at all.
- **Slow is healthy.** Nothing waits on the `cosmos` emulator, its `backlog` database or its
  containers. They can take minutes, or stay unhealthy. Until they are ready, `sync`
  answers `503 sync.replica_unavailable`. The harnesses, the Devbook pane and
  `ui-storybook` work without them.

## Entry points

Every host binds `localhost:0`, so the OS picks a new port on every run. Never hard-code a
port, and never reuse one from an earlier session, a log, or another worktree. Read each URL
from `list_resources` (or `aspire describe`) for this run. The dashboard URL is printed by
`aspire start`.

| Entry point | Answers |
| --- | --- |
| Aspire dashboard | Did it start, and what is failing? Resource states, console output, structured logs, traces. |
| `desktop-web-harness` | Does a desktop screen work? This is the main UI target: every pane (Tasks, Dashboard, Roadmap, Sessions, Devbook, DevPc, Settings), and any screen at phone width by resizing the viewport. |
| `mobile-web-harness` | Does Inbox quick-capture work? Its root is a capture box plus an inbox list whose Triage button acks through `sync`. It hosts no other pane. |
| `ui-storybook` | Does a shared component look and behave right on its own? It hosts `Backlog.UI.Components` alone, with no app or cloud behind it, next to the design chapters that govern each component. |
| `sync`, and `openapi/v1.json` on it | Does the sync API accept and answer this call? The OpenAPI document is served only in Development. |
| `azure-foundry-test` | What did the desktop harness's AI chat send? It is a local stand-in for Azure Foundry. |

A fresh load of `desktop-web-harness` reopens the surface the previous run left open. The
remembered surface is stored per worktree in `shell-navigation.settings.json`. Check where
the page actually landed before you act on it.

## Sign in

- The harnesses have no user sign-in locally. The sync features need a paired device
  instead. On `desktop-web-harness`, go to Settings → Features and turn on `sync`, then go
  to Settings → Devices and choose **Register this device**. `mobile-web-harness` shows a
  pairing-code entry while it is unpaired. Details are under `## Test Credentials` in
  `.claude/orch-context.md`.
- Restarting `sync` generates a new signing key. That drops every device token, but not
  the pairings themselves.
- Never type a password, token or key into a form yourself. Open the page, say where the
  credential lives, and let the person sign in.

## Never

- Restart an instance that is already running without saying so.
- Run destructive setup as part of starting: `desktop-web-harness`'s **Reset local data**
  command, a Cosmos volume prune, or `git clean`. Propose it instead. Every worktree on the
  machine shares one `%LOCALAPPDATA%\Backlog.Debug` workspace.
- Put a secret in this file. It is committed.
