# Monitoring & Dashboard

```meta
index: root
type: context
```

Monitoring tracks progress across projects and repositories, surfaces items that
need attention, and provides multi-layered dashboards. It can run as a standalone
team service. It aggregates Progress Signals from:

- [Tasks](../tasks/domain.md#task);
- [Inbox](../inbox/domain.md#inbox-item);
- GitHub;
- Application Insights;
- [Dev PC Management](../dev-pc-management/domain.md#machine-registry);
- [Repository Management](../repository-management/domain.md#repository-registry);
- [Sessions](../sessions/domain.md#session-log).

Outside it: every piece of state it shows, which stays with the context that
reports it. Its one write into another context is
[`FollowUpCaptured`](domain.md#followupcaptured).
