# Dev PC Management

```meta
status: draft
index: root
type: context
```

Dev PC Management supports multiple development PCs from a single interface:
register machines, wake them remotely, start remote desktop sessions, monitor
tool versions and compliance against the
[Technology Stack](../technology-stack/domain.md#technology-registry)
baseline, and trigger remote updates.

It used to track Copilot sessions too, back when Copilot was the only agent the
machines ran. That subject is
[Sessions](../sessions/domain.md#session-log) now — "which
agent worked where, for how long" turned out to be a different question in a
different language from "how is this PC configured", and it needed to describe a
second agent without a parallel list. What stays here is the machine; what left is
everything about what ran on it.

## system-tools

```meta
status: draft
type: feature-flag
key: system-tools
related: [".devbook/domain/dev-pc-management/features.md#catalog-authoring"]
```

Decided at release, from configuration. Name the switch in business language, and say who owns the rollout, what turning it on changes, and when the flag is retired.
