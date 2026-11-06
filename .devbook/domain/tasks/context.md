# Tasks

```meta
index: root
type: context
deployment: module
related: [.devbook/arc42/05-building-block-view.md#desktop-app]
```

Tasks maintains a personal backlog of prompts, tasks, ideas and tests across multiple projects and repos. It converts triaged
[Inbox Items](../inbox/domain.md#inbox-item) into actionable,
prioritized Tasks and projects them to external systems such as GitHub and the Copilot CLI.

Outside it:

| Concern | Owner |
|---|---|
| What arrives and how it is triaged | [Inbox](../inbox/domain.md#inbox-item) |
| When planned work happens and in which order | [Roadmap Planning](../roadmap/domain.md#roadmap-item) |
| The knowledge a task links to | [Brain](../brain/domain.md#knowledge-note) |

The settings screen lists each feature flag below, except Task backlog, from the feature catalog in `AppFeatures.cs`. A person's choice is kept in `features.json` beside the app's other per-device choices.

## Task backlog

```meta
type: feature-flag
key: backlog
default: on
related: [".devbook/domain/tasks/features.md#task-creation"]
```

- **Does:** Create and edit tasks, filter and reorder them, and store them.
- **Default:** Always enabled, and the catalog offers no switch.
- **Why:** The app without it is not this app.
- **Retired:** Never, and the key is never renamed.
- **Why:** The key is a persisted value, which is also why this stays a chapter. `"backlog"` is written into `features.json` and outlives this context's name, Tasks.

## GitHub integration

```meta
type: feature-flag
key: github-integration
default: on
related: [".devbook/domain/tasks/features.md#issue-projection-and-state-read-back"]
```

- **On:** Offers GitHub access in settings, pushing an entry to an issue, and refreshing issue or pull request state.
- **Off:** The Tasks pane hides the actions that reach GitHub.
- **Default:** On, and marked `DEV`, the settings screen's mark for a feature still under construction (`AppFeatureStatus.Dev`; see [Feature Enablement](../../arc42/08-crosscutting-concepts.md#feature-enablement)).
- **Retired:** When the integration leaves `DEV`.
- **Key:** Not the adapter of the same name in `Backlog.Infrastructure.GitHub`.

## Copilot CLI

```meta
type: feature-flag
key: copilot-cli
default: on
related: [".devbook/domain/tasks/features.md#hand-off-to-copilot-cli"]
```

- **On:** Offers to start GitHub Copilot CLI from an entry, and from a devbook chapter in Devbook.
- **Default:** On, and marked `DEV`.
- **Retired:** When the hand-off leaves `DEV`.
- **Key:** In the shared kernel's `AppFeatureKeys` rather than in either context.
- **Why:** Two contexts read this one key.

## Copy entries

```meta
type: feature-flag
key: copy-entries
default: on
related: [".devbook/domain/tasks/features.md#copy-as-a-runnable-paste", ".devbook/domain/roadmap/features.md#tagging-planned-work", ".devbook/domain/sessions/features.md#start-a-session-from-an-entry"]
```

- **On:** Shows the copy-to-clipboard button on a task's row, a board card, the open entry and the pressed plan chip, and beside the Tag field of a planned item's dialog on the roadmap.
- **Off:** Those buttons are hidden, and the [queue icon](../sessions/features.md#start-a-session-from-an-entry) beside each is untouched.
- **Default:** On, and listed under Cross-cutting.
- **Retired:** With the copy, once queueing a session does the copy's job.
- **Key:** In the shared kernel's `AppFeatureKeys` as `CopyEntries`.
- **Why:** Tasks and Roadmap read this one key. It is recorded here because four of its five places are Tasks'.

## AI assistant

```meta
type: feature-flag
key: ai-assistant
default: on
related: [".devbook/domain/tasks/features.md#ai-assistance-over-the-visible-backlog"]
```

- **On:** Shows the assistant panel in the app chrome, which answers questions about the visible task content through Azure Foundry.
- **Default:** On, and marked `DEV`.
- **Retired:** When the assistant leaves `DEV`.

## Clear My Day overnight

```meta
type: setting
key: my-day-preferences.json
scope: user
default: on
related: [.devbook/domain/tasks/features.md#my-day, .devbook/domain/tasks/domain.md#my-day, .devbook/domain/tasks/requirements.md#my-day, .devbook/arc42/adr/0032-my-day-preferences-ride-the-task-feed.md]
```

Whether today's [My Day](domain.md#my-day) starts empty each morning. On, the
default, a task is in My Day only on the date it was picked for, so yesterday's
list clears itself. Off, an unfinished task picked on an earlier day stays in My
Day until the person ticks it off or takes it out. A task ticked off on an
earlier day leaves either way, and a task picked for a later day never shows
early.

The person sets it on the desktop, on the Tasks page of Settings. The phone has
no switch of its own and follows the desktop's choice. The choice syncs, so
every paired device lists the same My Day
([ADR 0032](../../arc42/adr/0032-my-day-preferences-ride-the-task-feed.md),
accepted). Flipping it never rewrites a task, because each device applies it
when it reads My Day. A device that never received a choice reads it as on.
