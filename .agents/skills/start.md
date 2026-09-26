---
name: start
description: "Start this repository's application the way this repository says to, and leave it running. Use when: starting or running the app locally, 'start the app', 'run it', resuming work on a branch, or a flow needs a runtime at app.start."
goal: "Leave this repository's application running and healthy, and report the command that started it, the health verdict, and its entry points. Never hand the person a command to run themselves."
---

# Start the Application

Start the app from what this file declares, not from a command guessed per session. **Edit
this file** — it is yours: the facts are examples to replace, the procedure a starting point.
Whoever invokes `start` expects only what the wrapper's goal says: a running application and
the three facts reported.

A repository with nothing to start drops `start` from `components.devbook-procedures.adopted`
in `.devbook/config.json` instead of keeping this file.

## Run

<!-- The command, where it runs from, and what it needs. Replace the example. -->

```bash
aspire start --isolated --non-interactive --apphost src/Aspire/Backlog.Aspire.AppHost/Backlog.Aspire.AppHost.csproj
```

- From the worktree root; needs the .NET SDK and a container runtime. Forward slashes in
  `--apphost`; `--isolated` gives this worktree its own ports and user-secrets state.
- AppHost: `src/Aspire/Backlog.Aspire.AppHost/Backlog.Aspire.AppHost.csproj`
- `aspire start` detaches and exits `0` even when startup failed — verify with
  `aspire describe --apphost <same path>`, never from the exit code.
- Stop only this worktree's AppHost: `aspire stop --apphost <same path>`. Never `--all`, never
  kill processes by name — other worktrees run their own. CLI details: `.agents/skills/aspire/`.

1. **Check whether it is already running** (`aspire ps`) before starting a second copy. Reuse
   this worktree's instance and say so; an AppHost from another worktree is not yours.
2. **Run the declared command** in the background. Never substitute a different command when
   the declared one fails; report the failure.
3. **Wait for the signals under Healthy.** Stop waiting on a fatal error, or after two
   minutes of silence. Do not report a partially-started app as healthy.
4. **Re-read the entry points** every start — a port changes.

Report in a couple of lines: the command, the health verdict, the entry points. Leave the app
running — `show`, `debug`, and a flow's later stages work against it.

## Healthy

<!-- What a good start looks like, and which warnings are known and benign. -->

- The Aspire dashboard is reachable and `sync`, `azure-foundry-test`, `desktop-web-harness`,
  `mobile-web-harness`, and `ui-storybook` reach `Running`. The harnesses start after their
  `WaitFor` dependency (`azure-foundry-test`, `sync`) — a brief wait is normal.
- Benign: `desktop`, `mobile-android`, `mobile-maui-android-emulator`, `mobile-tunnel`,
  `ide-vscode-build`, and `ide-vscode-host` sit `NotStarted` — `WithExplicitStart()` by design.
- Benign: the `cosmos` emulator (and its `backlog` database and containers) is not waited on
  and can take minutes or stay unhealthy; `sync` answers `503 sync.replica_unavailable`
  meanwhile. The harnesses, the Devbook pane, and `ui-storybook` work without it.

## Entry points

<!-- What `show` opens and a stage validates against. -->

Ports are dynamic (`localhost:0`): never hard-code one. Read each URL from
`aspire describe --apphost <same path>` on every start. The Aspire MCP server may be attached
to another worktree's AppHost, or not see an `--isolated` one at all — check the AppHost path
it reports before trusting a URL, and fall back to the CLI.

| Entry point | URL |
| --- | --- |
| Aspire dashboard | Printed by `aspire start` / `aspire describe` |
| `desktop-web-harness` — the desktop screens, including the Devbook pane | `aspire describe` |
| `mobile-web-harness` — Inbox quick-capture at phone width | `aspire describe` |
| `ui-storybook` — every shared component with its governing design chapters | `aspire describe` |
| `sync` API — `openapi/v1.json` in Development | `aspire describe` |

## Sign in

<!-- A pointer only — where the credential lives, never its value. Delete if there is no sign-in. -->

- The harnesses have no user sign-in locally. Sync features need a device pairing instead:
  on `desktop-web-harness`, Settings → Features → turn on `sync`, then Settings → Devices →
  **Register this device**; `mobile-web-harness` shows a pairing-code entry while unpaired.
  Details: `## Test Credentials` in `.claude/orch-context.md`.
- Never type a password, token, or key into a form yourself: open the page, name where the
  credential lives, and let the user sign in.

## Never

- Restart a running instance without saying so.
- Run destructive setup — `desktop-web-harness`'s **Reset local data** command, a Cosmos
  volume prune, `git clean` — as part of starting. Propose it instead: the
  `%LOCALAPPDATA%\Backlog.Debug` workspace is shared by every worktree on the machine.
- Put a secret in this file. It is committed.
