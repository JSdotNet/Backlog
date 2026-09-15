# Features

One folder per vertical slice — a single use case, with its request, its handler,
and anything that exists only to serve it, kept together:

```
Features/
└── RunCapture/
    └── RunCaptureCommand.cs      the request and the handler, returns Result<T>
```

Rules for a slice:

- It returns `Result` / `Result<T>` from `Backlog.SharedKernel.Results`. Failures a
  caller can be expected to handle are values, not exceptions.
- It talks to the outside world only through ports declared in this project
  (`ICaptureSourceAdapter`) or published in `Backlog.Modules.Capture.Abstractions`
  (`ICaptureSourceSettings`). Adapters live in `src/Infrastructure`.
- It does not call another slice. Shared behaviour moves down into `Services/`.
- Its tests live in `tests/Backlog.Modules.Capture.UnitTests`, mirroring this folder.

Slices are registered with the host through `Extensions/CaptureModuleRegistration.cs`
(`AddCaptureModule()`), and reached from the shell through the published
`ICaptureRunner` port in `Services/`, never by resolving a handler directly.
