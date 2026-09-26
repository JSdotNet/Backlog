---
name: show
description: "Show the feature being built the way a reviewer would see it: the app running, opened at what the current branch changes, walked through, with evidence taken. Use when: 'show me', 'demo it', 'let me see it working', a flow wants a demo of finished work, or a review needs to look rather than read."
goal: "Put the feature on the current branch in front of a reviewer: the application running through `start`, opened at the area this branch changes, its scenario walked end to end, every step evidenced through `capture` and cited by path. A demo without evidence paths is not a demo."
---

# Show the Feature

Compose `start` and `capture` into a walk a reviewer can follow. **Edit this file** — the
branch-to-area map, the walk, and the report are yours; the goal in the wrapper is not.

## Run it

1. **Start it** by invoking the `start` skill. Reuse the instance it reports; never start a
   second one from here.
2. **Sign in** the way `start` says. A credential is never typed by you.

## Go to

<!-- Area — route — the source path it owns. Used to land on what the current branch changes.
     Replace these rows; delete the table if the app has one entry point. -->

| Area | Route | Owns |
| --- | --- | --- |
| Shared component | `ui-storybook`, the component's page | `src/Core/Backlog.UI.Components/` |
| Devbook pane | `desktop-web-harness`, Devbook | `src/Modules/Devbook/`, `src/Infrastructure/Backlog.Infrastructure.Devbook/`, `.devbook/` |
| Inbox quick-capture | `mobile-web-harness` root | `src/App/Backlog.Mobile.UI/`, `src/Modules/Inbox/`, `src/Modules/Capture/` |
| Sync API | `sync`, `openapi/v1.json` | `src/Modules/Sync/`, `src/Infrastructure/Backlog.Infrastructure.Sync/` |
| Desktop pane (Tasks, Dashboard, Roadmap, Sessions, DevPc) | `desktop-web-harness`, that pane | `src/Modules/<Pane>/`, `src/App/Backlog.Desktop.UI/` |

Match `git diff --name-only main...HEAD` against the `Owns` column and open the first area
that hits, on the URL `start` reported. Use the host's inline browser when it has one;
otherwise give the plain URL and say so. No hit: open `desktop-web-harness`'s root and say
the branch changed nothing the table names. A fresh load of `desktop-web-harness` reopens the
previous run's surface — assert where you landed before the first step. Phone-width
behaviour of a pane other than Inbox is shown by resizing `desktop-web-harness`.

## Walk it

<!-- The scenario a reviewer expects to see. Replace the example. -->

1. Land on the area, wait for its heading, and take the first frame.
2. Do what the branch enables — add a task, capture an Inbox item, open a Devbook chapter,
   open the new panel — one step per frame.
3. Land on the state that proves it worked, and take the last frame.

Invoke the `capture` skill for every frame; it decides the layout and the form. Capture the
moment a step fails before doing anything about it, then stop the walk and report.

## Report

Two or three lines plus the evidence: the area shown and why it was chosen, the steps walked,
and one path per frame — a sequence cites its folder and names each step. Leave the app
running.

## Never

- Change code, data, or configuration to make the walk succeed. A walk that needs a change
  is a finding, not a demo.
- Call a screenshot sequence a video. `capture` names the form; repeat what it said.
