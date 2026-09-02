# ADR 0002: The Backlog module owns the entry text language

```meta
status: accepted
related: [".domain/tasks/domain.md", ".domain/context-map.md", ".arc42/05-building-block-view.md"]
issue: null
```

## Status

Accepted. Implements the module structure `Backlog.Modules.Tasks.csproj` has
described in a comment since it was created, and takes a deliberate position on a
conflict between the JSdotNet guidelines ADR 0005 and ADR 0009 (see below).

## Context

The desktop client had grown into the application layer of the product. The
Backlog module held domain models and a repository port and nothing else — no
`Features/`, no services, no `Abstractions` project, despite its own project file
describing all three. Everything that decided what a task *is* lived in
`Backlog.Desktop.UI`:

- `EntryTextParser` (~980 lines) defined the entry text format, and its
  `SyncSubItems` mutated the aggregate directly.
- `TasksDesktopState` created `Task` aggregates, called `Rename`,
  `SetStatus`, `SetArea` and friends by hand, and saved through
  `IBacklogRepository`.
- `GitHubIntegration` mutated the aggregate to record a projection and left
  saving to its caller.

That is the anemic-domain shape the DDD guidance warns about, one layer up: the
rules were real, but they lived wherever the screen that needed them happened to
be. Two consequences mattered in practice. A second client — the mobile app, the
IDE extension, a future API — would have had to reimplement the format to read
its own backlog. And the module could not be trusted: an invariant added to
`Task` could be bypassed by any caller that set the properties itself.

## Decision

**The module owns the entry, its text format, and every use case over it. Hosts
dispatch use cases and hold DTOs.**

### The published surface

`Backlog.Modules.Tasks.Abstractions` is new and holds exactly what a caller
outside the module may see:

| Type | Why it is public |
|---|---|
| `TaskType`, `Priority`, `TaskStatus` | the vocabulary the DTOs and the text are written in |
| `TaskDto`, `EntryProjectionDto` | what a caller gets instead of the aggregate |
| `EntryTextParser` | the entry text format — this context's **published language** |
| `IBacklogEntries` | the service port, over the feature slices |

`EntryTextParser` sits in Abstractions deliberately, even though ADR 0009
describes Abstractions as contracts rather than behaviour. The entry text format
*is* a contract here: an entry in this product is its markdown, and an editor
that cannot read and write that format cannot edit an entry at all. What was
split off is the half that touches the aggregate — `SyncSubItems` and the old
`ToRawText(Task)` — which now live in the module as `EntryTextSync` and
`TaskMapper`. Abstractions keeps `ToRawText(TaskDto)`: text in,
text out, no aggregate in reach.

### The use cases

Six feature slices per ADR 0009, each a command or query plus its handler:
`ListEntries`, `SaveEntryFromText`, `DeleteEntry`, `ReorderEntries`,
`LinkEntryToIssue`, `RecordEntryUsage`. `ICommandHandler`/`IQueryHandler` are new
in `Backlog.SharedKernel.Handlers` — ADR 0006 puts them in a shared project, and
this solution had none. There is no mediator: a caller resolves the handler it
means and calls it.

`SaveEntryFromText` is the important one. Nearly every keystroke in the app ends
there, and everything about what a token means, which fields change, and what a
new entry starts as is now behind that single call.

### What the host keeps

The desktop side references **only** `Backlog.Modules.Tasks.Abstractions` — at
the time this ADR was written that meant `Backlog.Desktop.UI`; the same code now
lives in `Backlog.Modules.Tasks.UI`, which takes that reference and no other
into the module. It no longer constructs a `Task`, calls a mutator, or
touches `IBacklogRepository`.

The executable heads (`Backlog.Desktop`, `Backlog.Desktop.WebHarness`) do
reference the module, because composition is theirs: they pick the storage
adapter and call `AddTasksModule()`. `RootedFileBacklogRepository` is new in
the file-system adapter so the repository follows a storage folder the person can
move while the app is open, which is what the workspace store used to do by
handing out a rebuilt repository. That store is now the `ITaskStore` port in
this module's Abstractions, answered by `WorkspaceTaskStore` in the file-system
adapter.

### Guidance conflict, resolved deliberately

ADR 0005 says a host depends only on Abstractions. ADR 0009's own worked example
has the API host construct commands from the module's `Features` namespace, which
requires referencing the implementation. Those cannot both hold.

We read the rule as being about **reach, not references**: what ADR 0005 protects
is that nothing outside a module manipulates its aggregate. So the UI library
takes the strict reading (Abstractions only, enforced by
`ModuleSurfaceTests`), and the composition roots take the pragmatic one. If an
API host is ever added it may inject handlers directly and skip `IBacklogEntries`.

## Consequences

Positive:

- A second client can read and write tasks correctly by referencing
  Abstractions. Nothing has to be reimplemented.
- An invariant added to `Task` now actually holds: the only way in is a
  handler.
- The parser's 82 tests moved to `Backlog.Modules.Tasks.UnitTests`, where the
  code they cover lives.
- `BacklogStore` shrank to what it is — a pointer to a folder, plus the knowledge
  folder settings.

Negative:

- Saving an entry now reads it back from disk first, where the desktop previously
  kept aggregates in memory. For a local markdown store on a personal backlog
  this is not measurable; for a large one it would want a cache in the adapter,
  not a cache in the UI.
- `IBacklogEntries` is a facade over handlers, which is one more hop. It exists
  so a screen calling six use cases does not take six constructor arguments; the
  handlers remain the real seam.

Neutral:

- The Second Brain context still has no module — its readers and parsers live in
  `src/Modules/Knowledge/Backlog.Modules.Knowledge.UI`, which is a UI project
  under a module folder rather than a module with a domain behind it. It is a
  Core subdomain in the context map with the same problem this ADR just fixed for
  Tasks, and it is the obvious next extraction.
