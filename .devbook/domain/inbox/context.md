# Inbox

```meta
status: draft
index: root
type: context
deployment: module
related: [.devbook/arc42/05-building-block-view.md#desktop-app]
```

The Inbox is the processing queue for all captured input. Items arrive from
[Capture](../capture/domain.md#capture) as Inbox Items. The Inbox owns
**what happens to items after they arrive** — triage, classification, and
routing — deciding whether each item becomes a
[Task](../tasks/domain.md#task), a
[Knowledge Note](../devbook/domain.md#knowledge-note), is
deferred, or is archived. Outside it: the capture sources, which
[Capture](../capture/domain.md#capture) owns, and what an item becomes once it
is routed — a [Task](../tasks/domain.md#task) or a
[Knowledge Note](../devbook/domain.md#knowledge-note), owned by their contexts.
How a synced capture reaches the Inbox's intake is
`.devbook/arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md`.

## Inbox pane

```meta
status: draft
type: feature-flag
key: inbox-pane
default: off
related: [".devbook/domain/inbox/features.md#incoming-queue", ".devbook/domain/capture/features.md#run-capture-now"]
```

Turning it on shows the Inbox option and its pane in the Home shell, the only way to reach the incoming queue on the desktop. It ships off and marked `DEV` while triage is under construction. The Shell is its only reader, so the key sits on the Shell's own catalog (`AppFeatures.InboxPane`). The settings screen lists it from the feature catalog in `AppFeatures.cs`, and a person's choice is kept in `features.json` beside the app's other per-device choices. It is retired when the Inbox is released and the pane is simply there.
