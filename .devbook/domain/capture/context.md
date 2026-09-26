# Capture

```meta
status: draft
index: root
type: context
deployment: module
related: [.devbook/arc42/05-building-block-view.md#desktop-app]
```

Capture owns **how items enter the system**. It acquires raw content from any
source — mobile devices, automated monitors, email subscriptions, web clippers,
IDE-class hosts (IDE extensions and agentic session tools such as the GitHub
Copilot App), and manual import — normalizes it, and delivers it to the
[Inbox](../inbox/domain.md#inbox-item) as an Inbox Item. Once an item
is delivered, Capture's responsibility ends.

Outside it: triage, routing, and everything that happens to an item after it arrives, which the [Inbox](../inbox/domain.md#inbox-item) answers. In-app feedback is the one capture that does not reach the Inbox: it is filed as an issue on the product's own repository.

## Feedback reporting

```meta
status: draft
type: feature-flag
key: feedback-reporting
default: on
related: [".devbook/domain/capture/features.md#in-app-feedback-capture"]
```

Turning it on puts a report action in the app chrome that files a Desktop app issue on GitHub with a title, details, and an attached screenshot — the in-app route into Capture. It is released: on by default, with no `DEV` or `BETA` mark. The settings screen lists it from the feature catalog in `AppFeatures.cs`, and a person's choice is kept in `features.json` beside the app's other per-device choices. It is retired once nobody needs to switch the report action off.
