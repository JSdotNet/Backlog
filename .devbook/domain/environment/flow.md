# Environment

```meta
type: flow
status: draft
```

> Lifecycle and process flows for this bounded context: how environment shortcuts
> are resolved and activated.

## Shortcut activation

```mermaid
sequenceDiagram
    participant User as Person
    participant Env as Environment
    participant Repo as Repository Management
    participant Monitor as Monitoring & Dashboard

    User->>Env: Activate Environment Shortcut
    Env->>Repo: Resolve repository or workspace target when needed
    Env->>Monitor: Read current health signal when available
    Env-->>User: Launch action and access hints
```

- Environment owns the shortcut and launch preference.
- Repository Management supplies repository and workspace facts.
- Monitoring supplies health or availability signals when a view wants to display
  them.