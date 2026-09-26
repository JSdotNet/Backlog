# Tasks

```meta
status: draft
index: root
type: context
deployment: module
related: [.devbook/arc42/05-building-block-view.md#desktop-app]
```

Tasks maintains a personal backlog of prompts, tasks, ideas, and tests across multiple projects and repos. It converts triaged
[Inbox Items](../inbox/domain.md#inbox-item) into actionable,
prioritized Tasks and projects them to external systems such as GitHub and the Copilot CLI.

Outside it: what arrives and how it is triaged, which the
[Inbox](../inbox/domain.md#inbox-item) answers; when planned work happens and in
which order, which [Roadmap Planning](../roadmap/domain.md#roadmap-item)
answers; and the knowledge a task links to, which
[Devbook](../devbook/domain.md#knowledge-note) answers.

## Task backlog

```meta
status: draft
type: feature-flag
key: backlog
default: on
related: [".devbook/domain/tasks/features.md#task-creation"]
```

Create, edit, filter, reorder, and store tasks. It is always enabled — the catalog offers no switch, because the app without it is not this app — and it stays a chapter because the key is a persisted value: `"backlog"` is written into `features.json` and outlives this context's rename to Tasks, so the key is never renamed and never retired.

## GitHub integration

```meta
status: draft
type: feature-flag
key: github-integration
default: on
related: [".devbook/domain/tasks/features.md#issue-projection-and-state-read-back"]
```

Turning it on offers GitHub access in settings, pushing an entry to an issue, and refreshing issue or pull request state; off, the Tasks pane hides the actions that reach GitHub. On by default and marked `DEV`. The key is not the adapter of the same name in `Backlog.Infrastructure.GitHub`. The settings screen lists it from the feature catalog in `AppFeatures.cs`, and a person's choice is kept in `features.json` beside the app's other per-device choices. It is retired when the integration leaves `DEV`.

## Copilot CLI

```meta
status: draft
type: feature-flag
key: copilot-cli
default: on
related: [".devbook/domain/tasks/features.md#hand-off-to-copilot-cli"]
```

Turning it on offers to start GitHub Copilot CLI from an entry, and from a devbook chapter in Devbook — two contexts on one key, which is why it sits in the shared kernel's `AppFeatureKeys` rather than in either context. On by default and marked `DEV`. The settings screen lists it from the feature catalog in `AppFeatures.cs`, and a person's choice is kept in `features.json` beside the app's other per-device choices. It is retired when the hand-off leaves `DEV`.

## AI assistant

```meta
status: draft
type: feature-flag
key: ai-assistant
default: on
related: [".devbook/domain/tasks/features.md#ai-assistance-over-the-visible-backlog"]
```

Turning it on shows the assistant panel in the app chrome, which answers questions about the visible task content through Azure Foundry. On by default and marked `DEV`. The settings screen lists it from the feature catalog in `AppFeatures.cs`, and a person's choice is kept in `features.json` beside the app's other per-device choices. It is retired when the assistant leaves `DEV`.
