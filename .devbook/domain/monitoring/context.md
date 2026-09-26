# Monitoring & Dashboard

```meta
status: draft
index: root
type: context
```

Monitoring tracks progress across projects and repos, surfaces items that need
attention, and provides multi-layered dashboards. It aggregates Progress Signals
from [Tasks](../tasks/domain.md#task),
[Inbox](../inbox/domain.md#inbox-item), GitHub, Application Insights,
[Dev PC Management](../dev-pc-management/domain.md#machine-registry),
[Repository Management](../repository-management/domain.md#repository-registry),
and [Sessions](../sessions/domain.md#session-log), and can run as a standalone
team service.

Outside it: every piece of state it shows, which stays with the context that
reports it. Its only write into another context is `FollowUpCaptured` to the
Inbox.
