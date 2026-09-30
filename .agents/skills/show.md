---
name: show
description: "Show the feature being built the way a reviewer would see it: the app running, opened at what the current branch changes, walked through, with evidence taken. Use when: 'show me', 'demo it', 'let me see it working', a flow wants a demo of finished work, or a review needs to look rather than read."
goal: "Put the feature on the current branch in front of a reviewer: the application running through `start`, opened at the area this branch changes, its scenario walked end to end, every step evidenced through `capture` and cited by path. A demo without evidence paths is not a demo."
---

# Show the Feature

Combine `start` and `capture` into a walk a reviewer can follow. Start the app, open the
harness that serves the area the branch changes, walk the change, and capture every step.

## Run it

1. **Start the app** by invoking the `start` skill. Reuse the instance it reports; never
   start a second one from here.
2. **Pair a device only if the walk needs sync**, the way `start` describes. Never type a
   credential yourself.

## Go to

Each harness serves one kind of surface, and none of them stands in for another:

- `desktop-web-harness` serves every desktop pane.
- `mobile-web-harness` serves the phone app: the pairing box, the sync status line, and the
  Inbox, Note and Tasks tabs. It has no Devbook or other desktop pane. To show a desktop pane
  at phone width, resize `desktop-web-harness` to 390×844.
- `ui-storybook` hosts the shared component library alone, with no app behind it. A
  component change is shown there first, then in the pane that uses it.

| Area | Where | Owns |
| --- | --- | --- |
| Shared component | `ui-storybook`, that component's page | `src/Core/Backlog.UI.Components/`, `src/Harness/Backlog.UI.Storybook/` |
| Design tokens and styling | `ui-storybook`, then one pane that uses the token | `src/Core/Backlog.UI.Components/wwwroot/` |
| Devbook pane | `desktop-web-harness`, Devbook | `src/Modules/Devbook/`, `src/Infrastructure/Backlog.Infrastructure.Devbook/` |
| Tasks, Dashboard, Roadmap, Sessions, DevPc | `desktop-web-harness`, that pane | `src/Modules/<Pane>/` |
| Desktop shell, Settings | `desktop-web-harness` root, or Settings | `src/App/Backlog.Desktop.UI/`, `src/Harness/Backlog.Desktop.WebHarness/` |
| Phone app: pairing, Inbox, Note, Tasks | `mobile-web-harness` root, or that tab | `src/App/Backlog.Mobile.UI/`, `src/Modules/Inbox/`, `src/Modules/Capture/`, `src/Harness/Backlog.Mobile.WebHarness/` |
| Sync API | `sync`, `openapi/v1.json`, and then the harness that calls it | `src/Modules/Sync/`, `src/Infrastructure/Backlog.Infrastructure.Sync/` |
| MAUI heads, VS Code extension | Not walkable here: say so and show the shared screen in its harness | `src/App/Backlog.Desktop/`, `src/App/Backlog.Mobile/`, `src/App/Backlog.Ide.VsCode/` |

To choose the area:

1. Run `git diff --name-only main...HEAD` and match the result against the `Owns` column.
2. Open the first area that matches, on the URL `start` reported. Use the host's inline
   browser when it has one; otherwise give the plain URL and say so.
3. If nothing matches, open `desktop-web-harness`'s root and say that the branch changes
   nothing the table names. A branch that touches only `.devbook/`, `.agents/` or
   `.github/` has nothing to walk; say that instead of walking anyway.

Before the first step, check that the page is this worktree's build:

- A fresh load of `desktop-web-harness` reopens whichever surface the previous run left
  open, so check where you landed.
- Several worktrees can serve identical pages at once, so look for something only this
  branch has.

## Walk it

1. Land on the area, wait for its heading, and take the first frame.
2. Do what the branch enables, one step per frame: add a task, capture an Inbox item, open
   a Devbook chapter, open the new panel.
3. End on the state that proves it worked, and take the last frame.

Invoke the `capture` skill for every frame; it decides where the file goes and what form it
takes. When a step fails, capture that moment before doing anything else, then stop the walk
and report.

## Report

Two or three lines, plus the evidence:

- the area shown and why it was chosen;
- the steps walked;
- one path per frame. For a sequence, cite its folder and name each step.

Leave the app running.

## Never

- Change code, data or configuration to make the walk succeed. A walk that needs a change
  is a finding, not a demo.
- Call a screenshot sequence a video. `capture` names the form it produced; repeat what it
  said.
