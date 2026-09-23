# ADR 0012: Backlog is an MCP server hosted inside the running desktop application

```meta
status: active
date: 2026-09-22
related: [".arc42/03-context-and-scope.md#access-channels-scope", ".arc42/05-building-block-view.md#desktop-app", ".arc42/07-deployment-view.md#local-deployment-desktop", ".arc42/08-crosscutting-concepts.md#feature-enablement", ".arc42/08-crosscutting-concepts.md#authentication-and-authorization", ".arc42/adr/0002-backlog-module-owns-the-entry-text-language.md", ".arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md", ".arc42/adr/0004-knowledge-index-is-a-generated-local-database.md", ".arc42/adr/0008-knowledge-reads-from-a-branch-snapshot-when-there-is-no-clone.md", ".arc42/adr/0011-devbook-annotations-are-a-third-replica-container.md", ".arc42/adr/guidelines/0007-minimal-apis-over-controllers.md", ".domain/tasks/flow.md#task-lifecycle", ".domain/devbook/features.md#remarks-on-a-chapter", ".domain/dev-pc-management/features.md#configuration-and-tool-version-tracking"]
```

## Status

Accepted on 2026-09-22, **written ahead of the code and now partly built**: this
record went in first so that the `backlog-mcp-server` plan's implementation items
had a decision to build against rather than one to reconstruct afterwards.
Decisions 1, 2, 3 and 7 have since landed — the in-process host and its flag, and
the `backlog` registration the Dev PC catalog now carries. Each decision below
names the code it stands on; the **Deviations** section says, row by row, which
are built and where the rest still stops short.

A **local** decision, numbered in the local sequence. It is the MCP server
local ADR 0004 anticipated ("every channel — desktop, mobile, IDE, a future MCP
server — reads one schema") and the consumer local ADR 0011 left room for ("the
store sits behind a port an MCP tool or a review flow can serve from"). It
extends local ADR 0002 to a second client — an AI session — without opening a
second write path into an entry, and it leaves local ADRs 0003, 0004 and 0011
intact: one canonical task store, a generated devbook index nothing in C#
writes, and a private remark that never becomes a repository artefact by the
app's hand.

## Context

**A session has nothing to ask Backlog with.** The `backlog-run-plan-item`
skill runs one item of a plan a person pasted out of the app, and says so in
its own text: "consulting Backlog for the plan's other items and reporting the
item's status back is planned, not built — do not attempt either." The item
arrives as text; whether it is still outstanding is worked out from git; its
status in Backlog "has to be set by hand until the feedback loop exists". The
same gap sits on the devbook side: a person leaves a remark on a chapter in the
arc42 or domain panel and it goes into the store local ADR 0011 built, where no
session can read it. The Model Context Protocol is the one tool surface both
Claude Code and GitHub Copilot sessions on this machine already speak, and the
product already manages MCP registrations for them — `DevToolService` in
`Backlog.Desktop` registers, re-registers and unregisters servers with the
Claude CLI at user scope and describes each one as a row in the Tools pane.

**The data is in one running process.** Local ADR 0003 makes `backlog.db` the
canonical task store and the desktop app is its writer: `SaveFromTextAsync`
stamps and spawns recurrence successors inside one save, three sync loops
(`TaskSyncWorker` and its two siblings) hold per-device watermarks and push
what changed, and the panes refresh off `Changed` events raised in that
process. `DevbookAnnotationStore` keeps the remarks the same way. Anything that
wants to read or change either from outside the process has two options: join
the process, or open the files beside it.

**The desktop head has no generic host.** `TaskSyncWorker`'s own comment is the
record: "There is no hosted service anywhere in this repository because there
is nowhere for one to start: a `BackgroundService` would be started by the
generic host behind the browser harness and never by the MAUI desktop head."
Its answer — a singleton resolved once after `Build()` whose constructor starts
the timer, `_ = app.Services.GetRequiredService<TaskSyncWorker>()` in
`MauiProgram.cs` and again in the web harness's `Program.cs` — is the pattern
any long-running thing in the desktop head has to follow.

**The mobile head is fragile to framework references.** `Backlog.Mobile`
reaches `Backlog.Aspire.ServiceDefaults`, `Backlog.Infrastructure.Sync`,
`Backlog.Infrastructure.FileSystem`, `Backlog.Infrastructure.Sqlite` and
`Backlog.Modules.Tasks`. A `Microsoft.AspNetCore.App` framework reference in
any of them flows into `net10.0-android`, which cannot take it, and the
AspNetCore instrumentation package once broke that head through
`ServiceDefaults` in exactly this way. `Backlog.Desktop.csproj` references
`Microsoft.AspNetCore.Components.WebView.Maui` and nothing else of ASP.NET
Core today; `Backlog.Desktop.UI` is a Razor class library the harness and the
storybook also load.

**Status is a token, not a column.** Local ADR 0002 made the entry text the
language and the module the only thing that parses it. The status selector in
the Tasks pane does not call a status use case: `TasksDesktopState.ChangeStatusAsync`
rewrites the `!status` sigil with `EntryTextParser.WithStatus` and saves the
text through `RewriteMetadataAsync`, which is `ITaskItems.SaveFromTextAsync`.
`ITaskItems` has no status method. The lifecycle lives in `TaskItem`:
`AllowedTransitions` ("per `.domain/tasks/flow.md`"), `IsTransitionAllowed`,
`NextStatusesFrom` "so callers can explain a refusal rather than just swallow
it", and `ChangeStatus`, which throws on a jump. `SetStatus`, which the save
path calls, deliberately does not walk that graph, and its comment says why:
"Every caller is a person's edit or an import — `!done` typed on the metadata
line is the most consequential edit in the product."

**Two things are both called an annotation.** Backlog's `DevbookAnnotation`
(`RepositoryAlias`, `ChapterPath`, `BlockIndex`, `Body`, `Author`, `Resolved`,
`UpdatedAt`, `DeletedAt`) is a private reading note in a Backlog-owned store
behind `IDevbookAnnotationStore`, replicated between the person's desktops.
The devbook convention's `annotation` fence is a note written *into the
chapter* — a repository artefact with a schema (`author`, `date`, `body`,
`kind`, `status`, `quote`, `replies`), a position rule, a lifecycle in which
"resolving a note means deleting it", and one writer: "Every write goes
through `annotations.mjs` — `list`, `add`, `reply`, `resolve`. Nothing else
writes a fence with a regular expression of its own." Local ADR 0011 already
ruled that the two are different things with different owners.

**A repository has two names.** `TasksRepositoryRef` carries an `Alias` — "the
short name a person types in a `repo:` token" — and an `Id` computed as
`owner/name`, which is what an entry's `repo_ids` holds and what
`IRepositoryDirectory.Resolve` matches "without regard to case" when the name
contains a `/`. `DevbookAnnotation.RepositoryAlias` and the `repo:` token carry
the alias. A session, on the other hand, knows its git remote.

## Decision

**Backlog exposes its tools over MCP from inside the running desktop
application, and from nowhere else.** The seven decisions below are one
record because reversing any of them means reworking the same host.

### 1. The server lives in the desktop process, over Streamable HTTP on loopback

The MCP server is a **Streamable HTTP** endpoint the desktop application
listens on, bound to `127.0.0.1`, port **5757** by default and configurable in
Settings. It is protected twice: an **`Origin` check** that refuses any request
carrying an origin other than none or loopback, and a **bearer token** the app
generates once and keeps in the workspace settings — the per-user
`settings.json` under `%LOCALAPPDATA%\Backlog` that `WorkspaceSettingsStore`
owns, which is machine-local by construction and never travels with synced
content (`08-crosscutting-concepts.md#feature-enablement` makes the same point
about the feature switches). A registration hands the token to the host as an
`Authorization` header.

**There is no standalone stdio host.** A stdio server is a process the client
launches per session; that process would have to open `backlog.db` and the
annotation files beside the running app — a second writer next to the one
whose save path stamps, spawns successors and moves sync watermarks — or
tunnel to the app anyway, at which point it is a proxy in front of the
endpoint above. Loopback is the reason a bearer token is enough and the
`Origin` check is necessary: a browser page cannot reach `127.0.0.1:5757`
without a cross-origin request, and DNS rebinding is what the `Origin` check
closes.

**A session started before Backlog is open runs with the tools absent, and
that is accepted.** The client registers an HTTP server; when nothing listens,
the client reports the server unavailable and the session continues without
its tools. The skills that call them are written for that already — the
pasted plan item "is the whole input for now" — and opening Backlog is the
whole of the repair.

### 2. The host code: a framework reference in `Backlog.Desktop` only, started the way `TaskSyncWorker` is

- `<FrameworkReference Include="Microsoft.AspNetCore.App" />` goes into
  **`Backlog.Desktop.csproj` and nowhere else** — never `Backlog.Desktop.UI`,
  never a module, an infrastructure project or `ServiceDefaults`, because every
  one of those is on a path the mobile head compiles. The Kestrel listener, the
  MCP server registration and the tool classes live in `Backlog.Desktop`.
- The listener is **started after `Build()`** exactly as `TaskSyncWorker` is: a
  singleton resolved once in `MauiProgram.cs`, whose constructor binds the
  port and whose `Dispose` releases it. Not an `IHostedService`, for the reason
  `TaskSyncWorker` records — the MAUI head has nothing to start one.
- The **web harness maps the same endpoint with `MapMcp()`** on the Kestrel
  pipeline it already has, beside `MapRazorComponents`, rather than opening a
  second listener. That is what makes the server reachable under Aspire, where
  QA drives it, and what keeps the tools one implementation with two hosts —
  the same arrangement the sync loops have.
- The MCP host **resolves the MAUI container's instances** — `ITaskItems`,
  `IDevbookAnnotationStore`, `IRepositoryDirectory`, `IAppFeatureSettings` —
  and **never composes a second `ITaskItems` or opens a second SQLite
  connection.** A second composition would be a second `Changed` graph the
  panes do not listen to and a second connection the sync loops do not know,
  which is the file-syncing failure local ADR 0005 exists to rule out, inside
  one machine.

### 3. The server id is `backlog` in every registration

The server is registered under the name **`backlog`** wherever it is
registered — a project-scope `.mcp.json` and the user-scope Claude CLI
registration alike — so its tools are `mcp__backlog__*` in every host and a
skill can name one without knowing which registration is in force.
`DevToolService` is why the name has to be one: it finds a Claude registration
by name (`GetClaudeMcpServerAsync(cli, name, …)`), leaves a same-named
registration in another scope alone, and removes-and-re-adds by name when the
command drifts. One name is what lets one Tools-pane row say what state the
server is in on every host.

### 4. Scope is a `repository` argument, never the working directory

Every tool whose answer depends on a repository takes a **`repository`
argument in `owner/name` form**, read from the git remote by the calling
skill. The server maps it to the repository alias through
`IRepositoryDirectory.Resolve`, which already matches a name containing `/`
against `TasksRepositoryRef.Id` case-insensitively, and answers with the
alias's data — the `repo:` token on tasks, the `RepositoryAlias` on remarks.
An `owner/name` the directory does not know is a plain "not registered"
answer, not a registration: the server never calls `Register`, because a
session mentioning a repository is not a plan introducing one.

The server **never infers scope from a working directory.** It runs in the
desktop process, whose working directory is the app's, not the session's;
Streamable HTTP carries no directory; and several worktrees of one clone — the
normal state of this repository — share one `owner/name` while having as many
directories. The remote is the one identity every worktree agrees on, and the
skill is where the remote is.

### 5. A status change is a rewrite of the status token

The `transition` tool **rewrites the `!status` sigil in the entry text with
`EntryTextParser.WithStatus` and saves through `ITaskItems.SaveFromTextAsync`**
— the path the Tasks pane's own status selector takes. **No `SetStatus` use
case is added to `ITaskItems`**: local ADR 0002 put the whole use-case surface
behind one text grammar, and a second write path would be a second place where
"done" is decided, one that skips the recurrence spawn `UpdateAsync` performs
after a save reaches `Done`.

**The lifecycle in `TaskItem` keeps refusing jumps.** `AllowedTransitions` is
the graph; the tool asks `TaskItem.IsTransitionAllowed(current, target)`
before it rewrites anything and, on a refusal, answers with
`NextStatusesFrom(current)` so the session can say what would have been legal.
The save path itself applies status through `TaskItem.SetStatus`, which does
not walk the graph — because a person's typed `!done` is an edit, and that
stays true. The predicate is the module's; the tool consults it rather than
carrying a copy of the graph.

The moves a session makes are the two the lifecycle draws for work:
**starting work moves Ready → In progress**, and **opening the pull request
moves In progress → Done.** The pull request is the moment because it is the
one event in a delivery flow that is both observable and past the Personal
Validation gate; a commit or a push is neither. **An item whose session never
opens a pull request stays In progress** — parked, blocked, or abandoned looks
the same from the outside, and the person closes it, exactly as
`flow.md#task-lifecycle` gives `Done` to a completion and nothing else.

### 6. Two annotation kinds, one direction each

- **Backlog's private reading note is the inbox.** `DevbookAnnotation`, behind
  `IDevbookAnnotationStore`, is what a session reads over MCP: `List(alias,
  chapterPath)` and `Find(id)` serve it, drafts (`IsDraft`) and tombstones
  (`!IsLive`) excluded. It stays private on local ADR 0011's terms: the
  person's data, in Backlog's store, replicated between their desktops.
- **The answer is a devbook `annotation` fence, written by the session.** The
  session puts its reply into the chapter as a fence — beside the passage the
  remark's `BlockIndex` names, with the remark's wording as `quote` — through
  the devbook convention's own `annotations.mjs`, in the session's checkout,
  independent of Backlog. Then it **resolves the private note over MCP**
  (`SetResolved(id, true)`), which is what tells the person, on every desktop,
  that the remark was answered and where.
- **The MCP server never writes a fence.** The fence is a repository artefact
  the devbook plugin governs — its rule names one writer, and the app is not
  it; a chapter read from a branch snapshot has no file to write into (local
  ADR 0008); and a second writer with its own regular expression is precisely
  what the rule forbids. Nothing promotes a `DevbookAnnotation` into a fence by
  Backlog's hand; a session does that, in its own commit, under the rule's own
  terms ("say so, offer the commit, never push").

### 7. Exposure follows feature flags, one check per tool group

Tools are exposed in **groups, each behind one `IAppFeatureSettings` check** —
the task tools behind `TasksFeatures.Tasks`, the annotation tools behind
`DevbookFeatures.RepositoryDevbook` — and a group whose feature is off is
**absent from `tools/list`**, not present and refusing, which is what
`08-crosscutting-concepts.md#feature-enablement` asks of every switchable
capability ("its entry points are absent rather than present-but-inert"). The
listener itself answers to **the server's own flag, whose key is named once**:
`AppFeatures.McpServer = "mcp-server"` in `Backlog.Desktop.UI/Settings/AppFeatures.cs`,
beside `InboxPane` and `FeedbackReporting`, which sit there for the same
reason — a capability with no abstractions project of its own. It is
`EnabledByDefault: false`. The host follows `TaskSyncWorker.ShouldRun`'s
shape: read the flag, subscribe to `Changed`, start or stop the listener when
it flips, and never keep a port open for a feature the person switched off.

## Consequences

Positive:

- **The feedback loop the skills are waiting for has a host.** Reading a
  plan's other items, moving an item's status, and reading and resolving
  remarks are tool calls into the process that owns the data — no second
  writer, no file format, no sync race.
- **One implementation, two hosts.** The harness maps the same tools, so QA
  drives them under Aspire and a change to a tool is tested where every other
  desktop behaviour is.
- **Nothing new to parse.** A status change is entry text through the same
  save the pane uses; the lifecycle's refusals come from the module's own
  predicate.
- **The private note and the public fence stay two things.** Backlog keeps
  the inbox; the repository keeps the record; the plugin keeps its one writer.
- **The mobile head is untouched.** The framework reference cannot reach it.

Negative, and accepted:

- **No Backlog, no tools.** A session opened before the app, or with the
  feature off, runs without them and has to be told to open Backlog. Accepted
  over a stdio host that would have had to become a second writer.
- **Claude Desktop is not a registration target.** Its configuration file
  accepts stdio servers only (`DevToolClaudeDesktopServer`: "there is no
  transport to read"), so the Tools pane's Claude Desktop half has nothing to
  write for this server.
- **A fixed default port can collide.** 5757 is configurable for that reason;
  a collision at start is reported on the row, not retried on another port,
  because every registration names the port.
- **A token in a settings file.** Bearer over loopback is as strong as the
  user account is; it is not a defence against a process already running as
  that user, and does not claim to be.
- **Resolution is a two-step the session performs.** Write the fence, then
  resolve over MCP; a session that does one and not the other leaves a remark
  answered in the chapter and open in Backlog, or the reverse. The order
  chosen — fence first, resolve second — makes the recoverable case the one
  that happens.
- **Status moves are coarse.** The server offers two moves for work and the
  person makes every other one; an item a session abandons is a person's
  cleanup.

## Deviations

Where the code stands against each decision, kept current as the
`backlog-mcp-server` plan's implementation items land rather than frozen at the
day this record was accepted. A row marked **Built** is one the plan has closed.

| Decision | Where the code is today |
|---|---|
| 1 — in-process HTTP host | **Built.** `McpServerWorker` holds the loopback listener, `McpLoopbackGuard` enforces the absent-or-loopback `Origin` and the bearer token, and `WorkspaceSettingsStore` carries `McpServerPort` (default 5757), `McpServerToken` and `EnsureMcpServerToken()`, which mints on first need rather than on read. A port collision is reported on the row and never retried elsewhere, as this decision requires. |
| 2 — host code placement | **Built.** `Backlog.Desktop.csproj` carries the `Microsoft.AspNetCore.App` framework reference and nothing else does; `ModelContextProtocol.AspNetCore` is referenced by the two hosts alone. `AddBacklogMcpServer` in `Backlog.Desktop.UI` is the one registration both hosts call, and each adds its own transport — so the tools stay one implementation with two hosts. |
| 3 — `backlog` server id | **Built.** `.mcp.json` at the repository root declares the project-scope registration, and the Dev PC catalog carries a `backlog` row that `DevToolService` applies as `claude mcp add --transport http --scope user backlog <url> --header "Authorization: Bearer <token>"`. `DevToolMcpMechanism` gained `Http` beside `DotNetTool`, `Command` and `Manual`. The catalog holds `${BACKLOG_MCP_PORT}`/`${BACKLOG_MCP_TOKEN}` placeholders rather than literals — it is committed and resolves out of a synced folder — and the host expands them at apply time, from a closed vocabulary that never reads the environment. |
| 4 — `repository` argument | `IRepositoryDirectory.Resolve` already does the mapping. The skills that will supply the argument (`backlog-run-plan-item` and the delivery flows) do not read a git remote today. |
| 5 — status is a token | `EntryTextParser.WithStatus`, `SaveFromTextAsync` and `TaskItem.IsTransitionAllowed` / `NextStatusesFrom` all exist. `TaskItem.ChangeStatus`, the throwing form, has no caller in `src/`; the tool uses the predicate, not this method. Nothing today refuses a jump on the save path, so the refusal is the tool's to make. |
| 6 — two annotation kinds | `IDevbookAnnotationStore` has `List`, `Find` and `SetResolved`. `annotations.mjs` is not installed in this repository: the checked-in generator under `.github/tools/knowledge-meta/` predates it, and the installed copy arrives with the devbook contract v6 adoption (`devbook-sync`), a standing follow-up. Until then a session writes the fence with the plugin's own copy of the tool. |
| 7 — feature flags | **Built.** `AppFeatures.McpServer` (`"mcp-server"`) exists and `McpServerWorker.ShouldRun` reads it, so the listener holds nothing while the flag is off. `08-crosscutting-concepts.md#feature-enablement` is itself still `proposed` — the current build defaults optional features to enabled and records the disabled ones — so `EnabledByDefault: false` on this key is what keeps the listener off until chosen, the way `SyncFeatures.Sync` does it. The Tools pane still draws the `backlog` row while the flag is off, saying that nothing answers at the registered address: a registration made earlier is still on the machine, and hiding the row would hide the one place that says so. |

## Alternatives considered

- **A stdio host shipped as a .NET tool** (`DevToolMcpMechanism.DotNetTool`
  already installs one per row) — rejected: a process per session that opens
  `backlog.db` and the annotation files beside the running app, composing a
  second `ITaskItems` and a second `SqliteConnection`, with its own `Changed`
  graph the panes never hear. It would also have to re-read the feature
  switches from disk to honour them. The one thing it offers — tools without
  Backlog open — is the thing decision 1 accepts losing.
- **The sync service as the MCP host** — rejected: local ADR 0005 keeps each
  device's SQLite canonical and the replica a copy; a tool that wrote status
  through the cloud would be writing to the copy. It would also need the
  device JWT in every session, and would make an optional cloud a
  precondition for a local feature.
- **Inferring the repository from the session's working directory** —
  rejected: the server has no working directory of the session's, and a
  worktree's directory is not a repository's name. See decision 4.
- **A `SetStatus` use case on `ITaskItems`** — rejected: a second write path
  beside the text, skipping the recurrence spawn and the stamp the save
  performs, for a change the grammar already expresses. See decision 5.
- **Moving an item to Done on push, or on the run's Summary** — rejected: a
  push precedes the Personal Validation gate in every flow, and a Summary
  fires for a parked run too. The pull request is the only observable event
  that is both past the gate and a delivery.
- **The MCP server writing the `annotation` fence** — rejected: the devbook
  rule names one writer and the app is not it; a snapshot-read chapter has no
  file; and a Backlog-written fence would be a repository change made from a
  process that has no branch, no commit and no reviewer.
- **An unauthenticated loopback endpoint** — rejected: any page in any browser
  on the machine can send a cross-origin request to `127.0.0.1:5757`, and DNS
  rebinding lets it read the answer. The `Origin` check closes the browser;
  the token closes everything that is not the registered client.
- **One feature flag for all tools** — rejected: the task tools and the
  annotation tools belong to two switchable areas (`backlog`,
  `repository-devbook`) that a person turns on separately, and a tool for an
  area that is off would be a surface for a capability that has none.
