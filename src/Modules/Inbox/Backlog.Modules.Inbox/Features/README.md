# Features

One folder per vertical slice — a single use case, with its request, its handler,
and anything that exists only to serve it, kept together:

```
Features/
└── RouteToBacklog/
    └── RouteToBacklogCommand.cs   the request record and its handler
```

Rules for a slice, the same ones `Backlog.Modules.Tasks/Features/README.md`
states:

- It returns `Result` / `Result<T>` from `Backlog.SharedKernel.Results`. Failures a
  caller can be expected to handle are values, not exceptions; the codes live in
  `Backlog.Modules.Inbox.Abstractions.InboxErrors`.
- It talks to the outside world only through ports: the two repository ports
  declared in this project (`IInboxItemRepository`, `IInboxOrganizerRepository`)
  and the two outward ports declared in Abstractions (`IInboxBacklogTarget`,
  `IInboxPlanDrafter`). Adapters live in `src/Infrastructure`.
- It does not call another slice. Shared behaviour moves down into
  `DomainModels/` or into `Services/`.
- It never composes entry text. What an inbox item looks like as a backlog entry
  is Tasks' grammar (local ADR 0002), and the adapter behind `IInboxBacklogTarget`
  is the one thing allowed to know it.
- Its tests live in `tests/Backlog.Modules.Inbox.UnitTests`, mirroring this folder.

Slices are registered with the host through `Extensions/InboxModuleRegistration.cs`
(`AddInboxModule()`).
