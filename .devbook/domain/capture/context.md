# Capture

```meta
status: draft
index: root
type: context
```

Capture owns **how items enter the system**. It acquires raw content from any
source — mobile devices, automated monitors, email subscriptions, web clippers,
IDE-class hosts (IDE extensions and agentic session tools such as the GitHub
Copilot App), and manual import — normalizes it, and delivers it to the
[Inbox](../inbox/domain.md#inbox-item) as an Inbox Item. Once an item
is delivered, Capture's responsibility ends.

## inbox-pane

```meta
status: draft
type: feature-flag
key: inbox-pane
related: [".devbook/domain/capture/features.md#run-capture-now"]
```

Decided at release, from configuration. Name the switch in business language, and say who owns the rollout, what turning it on changes, and when the flag is retired.

## feedback-reporting

```meta
status: draft
type: feature-flag
key: feedback-reporting
related: [".devbook/domain/capture/features.md#in-app-feedback-capture"]
```

Decided at release, from configuration. Name the switch in business language, and say who owns the rollout, what turning it on changes, and when the flag is retired.
