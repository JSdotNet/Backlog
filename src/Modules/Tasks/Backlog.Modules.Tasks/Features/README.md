# Features

One folder per vertical slice — a single use case, with its request, its handler,
and anything that exists only to serve it, kept together:

```
Features/
└── CreateEntry/
    ├── CreateEntryCommand.cs      the request
    ├── CreateEntryHandler.cs      the behaviour, returns Result<T>
    └── CreateEntryValidator.cs    only if this slice needs one
```

Rules for a slice:

- It returns `Result` / `Result<T>` from `Backlog.SharedKernel.Results`. Failures a
  caller can be expected to handle are values, not exceptions.
- It talks to the outside world only through ports declared in this project
  (`IBacklogRepository`). Adapters live in `src/Infrastructure`.
- It does not call another slice. Shared behaviour moves down into
  `DomainModels/` or into `Services/`.
- Its tests live in `tests/Backlog.Modules.Tasks.UnitTests`, mirroring this folder.

Once the first slice exists, slices are registered with the host through an
`Extensions/ServiceCollectionExtensions.cs` composition root (`AddTasksModule()`).
That file is deliberately absent until there is something to register — today the
channels construct the repository directly.

The current desktop UI still calls the repository directly from
`src/App/Backlog.Desktop.UI/Services`. Those call sites move in here slice by
slice; this folder is the destination, not a rewrite that has already happened.
