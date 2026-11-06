# Environment

```meta
index: root
type: context
```

Environment owns the user's quick access to named environments:

- local harnesses;
- development, staging and production;
- cloud dashboards;
- repository-hosted apps;
- other frequently used destinations.

It stores launch preferences and shortcuts; repository ownership and health
remain with their supplier contexts.

This context's [Environment](domain.md#environment) is a launchable destination.
It is not the [Sessions Environment](../sessions/domain.md#environment), which is
where an agent session ran.
