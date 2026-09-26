# Dev PC Management

```meta
status: draft
index: root
type: context
deployment: module
related: [.devbook/arc42/05-building-block-view.md#desktop-app]
```

Dev PC Management supports multiple development PCs from a single interface:
register machines, wake them remotely, start remote desktop sessions, monitor
tool versions and compliance against the
[Technology Stack](../technology-stack/domain.md#technology-registry)
baseline, and trigger remote updates.

Outside it: which agent ran on a machine, where, and for how long, which
[Sessions](../sessions/domain.md#session-log) answers — "which agent worked
where" is a different question in a different language from "how is this PC
configured", so a Machine holds no session state. What stays here is the machine.

## System tools

```meta
status: draft
type: feature-flag
key: system-tools
default: on
related: [".devbook/domain/dev-pc-management/features.md#catalog-authoring"]
```

Turning it on offers the tools pane from the app chrome: check, update, enable, and disable what this machine is configured to have — Copilot and Claude plugins, marketplaces, MCP servers, and the applications and checks the setup guide asks for. On by default and marked `DEV`. The settings screen lists it from the feature catalog in `AppFeatures.cs`, and a person's choice is kept in `features.json` beside the app's other per-device choices. It is retired when the pane leaves `DEV`.
