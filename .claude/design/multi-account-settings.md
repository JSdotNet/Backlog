# Multi-account management — implementation design

Status: ready to implement. Design only; no production code or tests were written.

Scope: manage multiple GitHub and Claude accounts in Settings, **and** consume them,
so repository-scoped data resolves against the right account.

---

## 1. The failure being fixed, stated precisely

Two `gh` accounts in the keyring (`JSdotNet` active, `j-schepers_innobv` inactive).
Configured repositories span `JSdotNet/Backlog` and `innovadis-dev/spec-manager`.

Every call leaves as the **process-wide** identity, because
`ResolvingGitHubTransport` resolves `GhCliTransport` first
(`ResolvingGitHubTransport.cs:84-89`) and `gh api` has no per-call account selector.
So `innovadis-dev` calls go out as `JSdotNet` and 404. Switching the active account
breaks the other direction symmetrically.

There are **three** distinct manifestations, not one. The brief names the first;
the other two were found while reading and are in scope:

| # | Where | What happens today |
|---|---|---|
| 1 | Transport credential | `gh` active account, or `TokenForPath`'s "first repository with any token" fallback (`GitHubSettings.cs:123-128`). |
| 2 | Activity author filter | `GitHubActivitySource.cs:59` resolves the login **once, before** the repository loop at `:66`, and applies it as the `author`/`creator` filter to **every** repository. Work done as `j-schepers_innobv` in `innovadis-dev/spec-manager` is invisible even if the call authenticates. |
| 3 | Non-`repos/` paths | `RepositoryFromApiPath` (`GitHubSettings.cs:131-145`) only parses `repos/{owner}/{name}`. `orgs/{org}/…` (Copilot), `organizations/{org}/…` and `users/{login}/…` (billing) fall through to the arbitrary first token. |

A design that fixes only #1 leaves the dashboard still showing one account's work.

---

## 2. Findings that correct or extend the brief

Everything in the brief's "verified current state" checked out against the code, with
these additions and one correction.

**Confirmed by execution, not inference** (gh 2.95.0, 2026-06-17):

- `gh auth token --user <login>` **exists** and works for the **inactive** account:
  both `JSdotNet` and `j-schepers_innobv` return distinct 40-char `gho_` tokens,
  exit 0. Decision 3 is viable as proposed.
- `gh auth status --json hosts` **exists** and emits
  `{"hosts":{"github.com":[{"state","active","host","login","tokenSource","scopes","gitProtocol"},…]}}`.
  Settings can enumerate accounts without asking for a PAT.
- `gh api` has **no** `--user` flag (`gh api --help | grep -c "user string"` → 0). It has
  `--hostname` only. The CLI genuinely cannot be told which account to use per call.
- `gh auth switch --help` states it "changes the authentication configuration that will
  be used when running commands targeting the specified GitHub host" — global machine
  state. Rejected, see §5.

**Additions:**

1. **`IGitHubTransport` is never registered as a service.** Both hosts register the
   concrete `ResolvingGitHubTransport` and inject *that* into every client
   (`MauiProgram.cs:101,110,111,117,118,119-122`;
   `Backlog.Desktop.WebHarness/Program.cs:86,93,94,100,101,102-105`).
   `GhCliTransport` and `TokenTransport` are registered **nowhere** — the resolving
   transport news them up itself (`ResolvingGitHubTransport.cs:35-36`). This makes the
   seam change cheap: only two lines per host move.
2. **Live bug at `ResolvingGitHubTransport.cs:36`.** `settings.Current.TokenForPath` is
   a **method group bound to the `GitHubSettings` snapshot at construction time**, while
   `ApiEndpoint` beside it is a live lambda `() => settings.Current.ApiEndpoint`. A
   `Reload()` (wired to `workspace.RootChanged` at `MauiProgram.cs:94-100`) replaces
   `Current`, so after a workspace move the endpoint follows and the token lookup does
   not. Must be fixed as part of this work; with accounts it would be much worse.
3. **The harness is `src/Harness/Backlog.Desktop.WebHarness/Program.cs`**, not
   `WebHarness/Program.cs`. `Backlog.Mobile.WebHarness` and `Backlog.UI.Storybook`
   register **none** of these services. There is no Android or ide-vscode host in
   `src/`. So there are exactly **two** call sites for every wiring change.
4. **There is no UI anywhere for the Claude Admin API key.** `SetAdminApiKey`,
   `ClearAdminApiKey` and `SetWorkspaceId` are called only from
   `tests/Backlog.Infrastructure.Claude.UnitTests/`. The only way to configure Claude
   today is hand-editing `%LOCALAPPDATA%/Backlog/claude.json`. The AI panel offers the
   endpoint and the actor and nothing else (`Settings.razor:149-340`). The Accounts
   panel therefore *adds* the first admin-key UI rather than reshaping an existing one.
5. **A shared `SelectField` exists**, in `src/Core/Backlog.UI.Components/Selects/`,
   over `IReadOnlyList<SelectorOption>` with `Form`, `IncludeEmptyOption`/`EmptyLabel`
   and per-part class parameters — plus `RepositorySelector`, which is the same shape
   for the adjacent concept. This changes the UI answer: the binding control is a
   `SelectField`, not the hand-rolled `role="radiogroup"` of `AppButton`s that the
   colour swatches use. See §8.
6. **`AllowedRawControls` in `SharedControlAdoptionTests` is currently empty**, and
   `AllowedComponentClasses` is exactly `badge--gh`, `md-link`, `pane-resizer`. Nothing
   in this change may add to either list.
7. **`AgentSession` has no account axis**, and grouping is `None | Environment | Kind`
   only (`Sessions.cs:178-188`). Adding an account axis is additive to the record.
8. **Pre-existing, out of scope, worth recording:** `IClaudeUsageClient` is a singleton
   capturing a transient typed `HttpClient` (`MauiProgram.cs:128-131`,
   `Program.cs:110-113`) — a captive dependency in both hosts.

**Correction to the brief:** the brief asks that migration "satisfy `.arc42/adr/0006`".
ADR 0006 governs **the local SQLite store**, and its own Open Questions say so:
*"Whether the roadmap plan needs the same record. `_roadmap/plan.json` is read and
written whole as one document, so schema change there is a parsing concern rather than
a DDL one, and this record does not reach it."* `repos.json`, `github.json` and
`claude.json` are in exactly that category — read and written whole. ADR 0006 does not
literally reach them.

That is not a licence to be sloppy. The registry split already adopted ADR 0006's
discipline **voluntarily** — additive fields, idempotent by construction, deliberately
no version field (`GitHubSettings.cs:1094-1100`). This design adopts the same three
shapes and states so explicitly. No new ADR is required; if one is wanted later, it
would record "the JSON settings documents follow ADR 0006's discipline by convention".

---

## 3. Decision 1 — the Accounts model, and which file

### The shape

```csharp
// Backlog.Infrastructure.GitHub
public sealed record GitHubAccount(string Login)
{
    public string Id => Login;                       // GitHub logins are unique per host
    public string? DisplayName { get; init; }        // optional human label
    public GitHubCredentialKind Credential { get; init; } = GitHubCredentialKind.GhCli;
    public string? Token { get; init; }              // only for PersonalAccessToken
    public string? Host { get; init; }               // null = github.com; GHES hostname otherwise
    public string? ApiEndpoint { get; init; }        // null = the install-wide endpoint
}

public enum GitHubCredentialKind { GhCli, PersonalAccessToken }
```

`Login` is the id and is normalized case-insensitively (GitHub logins are
case-insensitive), mirroring how `FullName` is compared with `OrdinalIgnoreCase`
throughout the store.

### Which file — the split, and why

The existing rule is stated at `GitHubSettings.cs:189-215` and enforced by
`RepositoryRegistrySplitTests`: **identity is workspace data and lives in the shared
registry; secrets and machine paths are per-user and live in `github.json`.**

Applying it splits the account concept in two, and the split falls in a different
place than "the account record":

| Fact | File | Why |
|---|---|---|
| **Which account a repository is worked as** (the binding) | shared `{Root}/config/repos.json`, as a new optional `account` field on the existing repository row | "`innovadis-dev/spec-manager` is my work repo" is true on every install of this workspace. It is exactly as much a property of the repository as its alias and its colour, both of which are already there. |
| **The account list, with credentials** | local `%LOCALAPPDATA%/Backlog/github.json`, as a new top-level `accounts` array | A token is unambiguously a secret and is barred from the registry by `A_token_never_reaches_the_shared_registry` (`RepositoryRegistrySplitTests.cs:104-118`). *Whether `gh` is signed in as that login* is a fact about **this machine** — install #2 may have no `gh`, or a different set of accounts. The credential **kind** is per-machine for the same reason. |

So the account row is **not** split across two files. The registry states the *binding*;
the local file states *how this machine satisfies it*. That is deliberate and follows a
precedent stated in the code: `SetKnowledgeFolder` (`GitHubSettings.cs:462-476`) keeps
the whole knowledge-folder list local rather than splitting one row down the middle,
because *"splitting one row across two files would make this a two-file write whose
partial failure leaves an inconsistent row."* The same argument applies here and is why
there is no `accounts` array in the registry.

### The state this creates, and why it is a feature

A binding naming an account this machine has no local row for is an **unsatisfied
binding**. It is a real, nameable state — "this workspace expects `j-schepers_innobv`;
this machine has no credential for that account" — and it is precisely the shape a
second install has on day one. It is reported, never guessed around. See §7 for what a
call does in that state.

### Rejected alternative

*Put the account list (login + display name, no token) in the registry, tokens in
`github.json` keyed by login.* Rejected: it splits one row across two files, against the
precedent above; and it would put a second answer to "what accounts exist" into the
synced file, which is the exact class of problem the registry split was created to
remove (`GitHubSettings.cs:203-208`).

### Bounded hazard, accepted and recorded

The registry is the synced/committed half. If a workspace were ever shared between two
*people*, person B's registry would tell person B's install to work a repository as
person A's login — an unsatisfied binding, reported, not a wrong-identity call. The
product is single-user by constraint (`.arc42/02-constraints.md`, inherited ADRs
0012/0013), and "install #2" in the existing tests already means the same person's second
machine. Recorded in the style of the `WithCarryOver` hazard note
(`GitHubSettings.cs:770-777`) rather than designed around.

---

## 4. Decision 2 — how a repository binds to an account

### The grammar does not change

`alias = owner/repo` stays exactly as it is. `GitHubRepositoryRef.TryParse`,
`ToLine()`, `ParseText` and every error message are untouched.

**Justification — colour is the precedent.** `Colour` is shared identity, lives in
`repos.json`, and is deliberately *not* in the textarea grammar; it is edited in the
per-repository detail panel (`Settings.razor:684-724`) via
`SetRepositoryColour(alias, colour)`. The account binding is the same kind of fact and
gets the same treatment.

Extending the grammar (`alias = owner/repo as login`) would mean touching `TryParse`,
`ToLine`, the error messages, `ParseText`'s duplicate detection and
`PreserveExistingRepositorySettings` — five places, for a field that is chosen from a
known list and so is better picked than typed. Backward compatibility becomes free
rather than something to prove.

### The store surface

```csharp
// GitHubSettingsStore — mirrors SetRepositoryColour exactly
public string? SetRepositoryAccount(string alias, string? login);
```

- Refuses with `RegistryUnreadable` when `_registryState is RegistryState.Unreadable`
  (it is a shared write), exactly like `SetRepositoryColour` (`GitHubSettings.cs:421-423`).
- Returns `NotConfigured` when the alias resolves to nothing.
- Returns `$"'{login}' is not a configured account."` when the login names no account —
  the same shape as the colour range check at `:426-429`.
- Resolves the alias through `Current.Find` and acts on the resolved row's **id** via
  `IsSame`, per `GitHubSettings.cs:1011-1015`.
- `null` clears the binding.

`GitHubRepositoryRef` gains `public string? Account { get; init; }`.

### The one high-risk edit — say it loudly

`PreserveExistingRepositorySettings` (`GitHubSettings.cs:952-976`) **must** carry
`Account` across a retyped repository list, exactly as it carries `Colour`:

```csharp
Account = repository.Account ?? existing.Account,
```

and in the no-existing branch, `Account = CleanAccount(repository.Account)`.
`NormalizeRepositories` (`:978-988`) must clean it too.

Without this, every binding is silently dropped the moment anyone edits the repositories
textarea — because `SetRepositories` rebuilds every row from parsed text. This is the
single most likely defect in the change and needs a named test
(`Binding_survives_the_repository_list_being_retyped`), modelled on
`A_clone_directory_survives_the_alias_being_renamed`.

### What an unbound repository does

**Unbound means "whatever this machine's default is" — which is today's behaviour,
unchanged.** This is the property that makes the whole change safe: a user who never
opens the Accounts panel sees no difference at all.

Full precedence, evaluated per call in `GitHubSettings.AccountForPath`:

1. **Bound** → that account's credential. If the account has no usable credential,
   **fail with a message naming it**. Never fall through to another identity — falling
   through is the 404 bug.
2. **Unbound, repository has its own token** → that token. Unchanged from today, and the
   existing token control keeps its existing copy ("only used as a fallback … when `gh`
   is not signed in").
3. **Unbound, no token** → the default: the `gh` CLI's active account via
   `GhCliTransport`, exactly as today.
4. **Nothing** → `GitHubNotConfiguredException` with today's message.

Monotone in specificity. Binding wins over a leftover repository token because binding is
the newer, deliberate act and the token's own UI already calls itself a fallback; the
repository detail panel says so in a status line when both are present.

**The only removed behaviour is `TokenForPath`'s "first repository with any token"
fallback** (`GitHubSettings.cs:126-127`). That fallback *is* the bug. Removing it is the
fix, and it must be removed for bound and unbound alike.

### Non-`repos/` paths

`AccountForPath` replaces `RepositoryFromApiPath` as the entry point and handles all
five shapes actually used (verified against every `SendAsync` call site):

| Path shape | Resolution |
|---|---|
| `repos/{owner}/{name}/…` | the repository's binding |
| `orgs/{org}/…` (Copilot: `CopilotUsageClient.cs:118,145,168-171`) | the binding shared by every configured repository with `Owner == org`; if they disagree or none is bound → default |
| `organizations/{org}/…` (billing: `GitHubBillingClient.cs:185`) | as above |
| `users/{login}/…` (billing: `GitHubBillingClient.cs:171`) | the account whose `Login == login`; else default |
| `user`, anything else, and `null` (the pathless probe at `TokenTransport.cs:37`) | default |

This fixes manifestation #3 from §1 as a by-product, and it is a pure function over
`GitHubSettings` — fully unit-testable with no I/O.

---

## 5. Decision 3 — the gh CLI question

**Confirmed as proposed, with one refinement: `GhCliTransport` is kept, narrowed.**

### The CLI becomes a credential source

New type in `Backlog.Infrastructure.GitHub`:

```csharp
public interface IGhCliAccountSource
{
    Task<IReadOnlyList<GhCliAccount>> ListAsync(CancellationToken ct = default);
    Task<string?> GetTokenAsync(string login, string? host = null, CancellationToken ct = default);
    void Invalidate();
}

public sealed record GhCliAccount(string Login, string Host, bool Active, string? Scopes);
```

- `ListAsync` → `gh auth status --json hosts`, parsed into the record above. Feeds the
  Settings picker, so the user chooses a login instead of pasting a PAT.
- `GetTokenAsync` → `gh auth token --user <login> [--hostname <host>]`.
- Process launch **must** set `CreateNoWindow = true` — enforced by
  `tests/Backlog.ArchitectureTests/ProcessLaunchTests.cs`, whose `AllowedConsoleWindows`
  list this change must not touch.

### Hard rule: a gh-sourced token is never persisted

`gho_` tokens are OAuth tokens that `gh` refreshes and rotates. Writing one into
`github.json` would create a stale secret in a file — a correctness regression *and* a
security one.

So a gh-sourced token is held in memory only, keyed by login, with a short TTL
(5 minutes is ample; the cost is one fast subprocess) and cleared by `Invalidate()`,
which is already reachable from Settings' "Check the connection" button via
`GitHubIntegration.DescribeConnectionAsync` → `probe.Invalidate()`
(`GitHubIntegration.cs:41-45`).

Test to write: `A_gh_sourced_token_never_reaches_the_settings_file` — the mirror of the
existing `A_token_never_reaches_the_shared_registry`.

### Explicitly rejected

- **`gh auth switch`.** Mutates `~/.config/gh/hosts.yml`, which the user's terminals,
  VS Code, other `gh` invocations and any concurrent Backlog window all read. It would
  race with them and change state the app does not own. Non-starter, as the brief says.
- **`gh api` with `GH_TOKEN` injected into the child process environment.** It works
  (`gh` honours `GH_TOKEN` over stored credentials) but is strictly worse than
  `TokenTransport`: it is the *same token*, plus a subprocess per call, plus `gh`'s
  error text instead of the mapped messages in `TokenTransport.Describe`
  (`TokenTransport.cs:115-138`), and it makes `gh` a hard dependency for a call the app
  can already make over HTTP. Recorded as a considered non-choice.
- **Dropping `GhCliTransport` entirely.** Rejected. For an unbound repository with no
  token, `gh api` as the active account is exactly today's behaviour and needs no
  credential extraction at all — which preserves the property its class doc exists for:
  *"it means the app never holds a credential: `gh` keeps it, refreshes it, and revokes
  it"* (`GhCliTransport.cs:7-11`). Keep it, narrowed to the **default path only**.

### If `gh auth token --user` were absent

It is not, on gh 2.95.0. For an older `gh`, or GHES, the fallback is
`GitHubCredentialKind.PersonalAccessToken`: the Accounts panel offers "paste a token"
alongside "use the GitHub CLI", so there is always a route. `ListAsync` returning empty
degrades the picker to manual entry rather than blocking the panel.

---

## 6. Decision 4 — identity per account, without widening `SendAsync`

**`IGitHubTransport.SendAsync` is not touched.** Its five overload parameters stay as
they are, and so does every one of the ~17 call sites. The account travels **in the
path**, resolved behind the transport — the path-keyed delegate seam the brief prefers.

### The seam, made async and named

Today: `TokenTransport(Func<string?, string?> token, …)`, called as `_token(path)`
(`TokenTransport.cs:15,46`). Resolving a gh credential is a subprocess call, so the
delegate must become async. Blocking on it inside a sync delegate on the UI thread is
not acceptable.

```csharp
public interface IGitHubCredentialResolver
{
    Task<GitHubCredential?> ResolveAsync(string? path, CancellationToken ct = default);
    void Invalidate();
}

public sealed record GitHubCredential(string Token, string? ApiEndpoint, string Account);
```

An interface rather than a raw delegate, because it now composes two behaviours
(settings lookup + gh extraction) and needs a fake in tests.

`TokenTransport` takes `IGitHubCredentialResolver` instead of the two delegates.
`ResolvingGitHubTransport` constructs it — **and this is where the `Reload()` bug from
§2.2 is fixed**: the resolver reads `settings.Current` per call, so a workspace move is
picked up.

Blast radius: `TokenTransport` ctor, `ResolvingGitHubTransport` ctor, and the two host
registrations. **Zero client changes.**

### `GitHubIdentityClient` stops being one cached login

Two changes:

1. **The cache becomes per account.** `_login`/`_asked` (`GitHubIdentityClient.cs:35-37`)
   become a `Dictionary<string, string?>` keyed by account login, with `""` for the
   default account, plus an `Invalidate()`.
2. **A bound repository needs no round trip at all.** The binding *names* the login. So:

```csharp
public interface IGitHubIdentityClient
{
    Task<string?> GetLoginAsync(CancellationToken ct = default);                       // unchanged: the default account
    Task<string?> GetLoginForAsync(GitHubRepositoryRef repository, CancellationToken ct = default);
}
```

`GetLoginForAsync` answers from configuration when the repository is bound, and falls
back to the cached `user` probe when it is not. `GitHubIdentityClient` gains a
`GitHubSettingsStore` dependency — which `GitHubBillingClient` already takes
(`GitHubBillingClient.cs:110`), so it is not a new kind of dependency for this layer.

The existing `GetLoginAsync` keeps its exact signature and meaning, so
`GitHubBillingClient.cs:140,162` and `GitHubActivitySource.cs:34` compile unchanged.

### The activity fix (manifestation #2)

`GitHubActivitySource.GetActivityAsync` resolves the login once at `:59` and applies it
to every repository in the loop at `:66`. Move the resolution **inside** the loop:
`await identity.GetLoginForAsync(repositoryRef, ct)` per repository. The XML doc at
`GitHubActivitySource.cs:12-16` explains why the login is resolved here rather than
passed in; that reasoning survives — only its *arity* changes, from one login for the
app to one login per repository. Update that comment in the same edit.

---

## 7. Decision 5 — Claude accounts

### A Claude "account" is two different things, and they must not be conflated

| Thing | What it is | Repository dimension | Where it lives |
|---|---|---|---|
| **Organization credential** | Admin API key + workspace + actor. Usage is org-scoped and reported as date × model × **actor** (`ClaudeSpendSource.cs:46,61,98`). | **None, and none is possible.** The Anthropic Admin API exposes no repository axis. | `claude.json` |
| **Agent config directory** | Where Claude Code keeps `sessions/` and `projects/` — `~/.claude` by default, relocatable via `CLAUDE_CONFIG_DIR`. | Indirect: a session records its `cwd`, and `Repository` where the agent wrote one. | new `agents.json` |

**The design must not promise per-repository Claude usage.** It is impossible from the
API. Where the two lists appear in the UI they are two separate sections with different
headings, so nobody reads one as the other.

### 7a. Organizations — the single-record → list migration

```csharp
public sealed record ClaudeOrganization(string Id)   // stable slug; label for the UI
{
    public string? DisplayName { get; init; }
    public string? AdminApiKey { get; init; }
    public string? WorkspaceId { get; init; }
    public string? Actor { get; init; }
    public string ApiEndpoint { get; init; } = ClaudeSettingsStore.DefaultApiEndpoint;
    public string ApiVersion { get; init; } = ClaudeSettingsStore.DefaultApiVersion;
}

public sealed record ClaudeSettings
{
    public List<ClaudeOrganization> Organizations { get; init; } = [];

    // FROZEN LEGACY FIELDS — read so an older file opens and can be carried over,
    // written back as null. Same treatment as SettingsDto.Token in the GitHub store.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AdminApiKey  { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? WorkspaceId  { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Actor        { get; init; }

    // Compatibility surface — every existing consumer keeps compiling and passing.
    [JsonIgnore] public ClaudeOrganization? Primary => Organizations.FirstOrDefault();
    [JsonIgnore] public bool IsConfigured        => Primary?.AdminApiKey is { Length: > 0 };
    [JsonIgnore] public bool LooksLikeAdminKey   => …;
}
```

Everything the whole file is local already, so there is **no shared half** — nothing
about a Claude organization needs to travel with the workspace, because no Claude datum
is repository-scoped.

Carry-over on `Read()`, following `GitHubSettingsStore.Load` (`GitHubSettings.cs:715-758`):
if `Organizations` is empty **and** any legacy scalar is non-null, synthesize one
organization from them with `Id = "default"`. Idempotent by construction — after the
next write the legacy fields are null and the condition never matches again. Additive.
No version field, matching `GitHubSettings.cs:1094-1100`.

`ClaudeAdminTransport` (`:34,44,52-54`) and `ClaudeUsageClient` (`:49,57`) read
`settings.Current.AdminApiKey` etc. Keeping those as computed properties over `Primary`
means **stage 4 changes no consumer and breaks no existing test**.

Per-organization reporting (`IClaudeUsageClient` taking an organization, the dashboard
showing them side by side) is **stage 6**, not stage 4. Stated honestly: stage 4 gives
the user multi-organization *management* and one-organization *reporting*; stage 6
completes it.

### 7b. Session homes — inside `LocalAgentSessionSource`, and here is why

The XML doc at `LocalAgentSessionSource.cs:17-27` argues this source speaks for one
machine and a fleet should be a **second implementation** of the port. The brief asks
for an explicit decision. **Multiple accounts on one machine belongs inside this
source.**

The doc's boundary is about **environment**, not about account. Its own words: *"Every
session is stamped with this machine's name, because … a session found here ran here.
Sessions from another environment arrive when that environment reports them."* Two
Claude config directories in the same user profile on the same box are still sessions
that **ran here**, and they carry this machine's name truthfully. Nothing in that
argument is violated.

A second implementation is for sessions that ran **somewhere else** and must be reported
by that somewhere else — a fleet, a container, a hosted runner. Reading three folders on
this box instead of two is not that: the source already reads two (`~/.claude` and
`~/.copilot`), and that did not make it a fleet source. Widening the folder list is the
same port answered by the same source; widening the *environment* is the second
implementation the doc reserves.

Concretely:

- `LocalAgentSessionSource` takes `IReadOnlyList<AgentHome>` per kind instead of one
  path each; `AgentHome(string Label, string Directory)`.
- The parameterless ctor keeps today's two defaults verbatim.
- `ClaudeSessionReader` and `CopilotSessionReader` ctors are unchanged
  (`(string home, string environment, TimeProvider)`); the source constructs one reader
  per home and concatenates, reusing the existing per-reader failure collection at
  `:89-104`. An unreadable home is named as `$"{Name} ({label})"`.
- `AgentSession` gains a trailing `string? Account = null`, carrying the home's label.
  Additive to a positional record, source-compatible.
- Grouping (`Sessions.cs:178-188`) gains an `Account` axis.

**Storage.** A new local store `AgentAccountsStore` → `%LOCALAPPDATA%/Backlog/agents.json`,
holding per-kind `[{ label, configDirectory }]`. Absent file synthesizes the two defaults,
so **absent == today's behaviour exactly**. It goes in a new file rather than in
`claude.json` because a Copilot config directory has no business in the Claude API
credential file, and because Sessions owns the concept.

**Placement to verify in stage 5:** the store must be public and injectable (Settings
edits it) and live somewhere the App may reference. First choice
`src/Modules/Sessions/Backlog.Modules.Sessions.UI/`; if `ModuleSurfaceTests` or
`ModuleBoundaryTests` object to a module's UI project publishing a store, fall back to
`Backlog.Modules.Sessions.Abstractions`. Confirm before writing, do not assume.

**DI.** `SessionRegistration.cs:37` currently discards the provider
(`_ => new LocalAgentSessionSource()`). It must stop discarding it and read the
configured homes. Its XML remarks at `:25-29` justify the singleton on "the adapter
holds no state"; that stays true, but the "no dependencies" claim in the surrounding
prose needs updating in the same edit.

---

## 8. Decision 6 — the Settings UI

### The page

`enum SettingsPage` (`Settings.razor:943-949`) gains `Accounts`, inserted **immediately
before `Repositories`** — a repository binds to an account, so accounts should be read
first. The enum order drives the strip order, and `PageId`/`ShowSettingsPageById`
(`:952-959`) need no change because they are `Enum.TryParse` over the member name.

`IsSettingsPageAvailable` (`:985-990`) gains:

```csharp
SettingsPage.Accounts => Features.IsEnabled(TasksFeatures.GitHubIntegration) || UsageMetricsEnabled,
```

so the panel never appears empty. `EnsureActiveSettingsPage` (`:1084-1088`) already
handles a page becoming unavailable.

### The Accounts panel — a real list editor, from existing components only

**Not the textarea idiom.** The textarea grammar exists because a repository coordinate
is a thing you *type*. An account is a thing you *pick* — `gh auth status --json hosts`
already knows the answer. A free-text list of logins would invite typos that fail as
404s, which is the bug being fixed.

Structure, copying the repository panel's own idiom (`Settings.razor:640-910`) exactly:

- Outer `<Tabs Bare="true">` strip, one `<TabPanel>` card per configured account —
  the same shape as `repo-subpages`.
- Inside each card: the login and host as text, a **`SelectField`** for the credential
  kind (`GitHub CLI` / `Personal access token`), a `TextField type="password"` for the
  PAT shown only for the token kind, an `AppButton` "Forget this token", and a
  `p.setting__status[role=status]` connection line.
- Above the strip: a "Discovered by the GitHub CLI" list — for each `GhCliAccount` not
  yet configured, one `AppButton` "Add". Plus a `TextField` + `AppButton` for adding a
  login manually.
- A separate section for **Claude organizations**, same `Tabs`/`TabPanel` shape, with
  `TextField`s for the admin key (`type="password"`), workspace and actor — the first
  admin-key UI in the app (§2.4). The actor field currently on the AI panel
  (`Settings.razor:193-207`) moves here; leave a one-line pointer in its place.
- A third section for **agent session folders**, `TextField` per configured home plus
  add/remove `AppButton`s.

House rule holds throughout: **no save button**, commit on `@onchange`/Enter, `Escape`
resets the draft, error in the adjacent `setting__status`, per
`Settings.razor:50-55`.

Components used, all existing and all already used on this page: `Tabs`, `TabPanel`,
`TextField`, `SelectField`, `AppButton`, `Alert`, `Toggle`/`Checkbox`,
`FeatureStatusBadge`. **No new shared component. No storybook cost.**

### The binding control on the repository card

In the repository detail panel, beside the colour picker, a **`SelectField`**:

```razor
<SelectField Form="true"
             Label="Worked as"
             Id="@($"repo-account-{activeRepo.Alias}")"
             TestId="repo-account-select"
             LabelCssClass="setting__label"
             SelectCssClass="setting__input"
             CurrentValue="@activeRepo.Account"
             Options="@AccountOptions()"
             IncludeEmptyOption="true"
             EmptyLabel="Default (the signed-in account)"
             HelpText="…"
             OnChanged="login => ApplyRepositoryAccount(activeRepo, login)" />
```

plus a `p.setting__status[role=status]` saying it in words — which account, or that the
default applies, or that the bound account has no credential on this machine.

**Why `SelectField` and not the colour swatches' `role="radiogroup"` of `AppButton`s.**
The swatches are a radiogroup because a colour must be *seen* to be chosen; ARIA
`radiogroup` is the right shape for a set of visible swatches. An account is a name from
a list — the exact shape `SelectField` exists for, and `RepositorySelector` is the same
control for the adjacent concept. `IncludeEmptyOption`/`EmptyLabel` gives the unbound
state for free, so no separate "Reset to default" button is needed.

It is also the safer choice against the architecture tests. A hand-rolled radiogroup is
*invisible* to `SharedControlAdoptionTests` — its container classes are app-owned, which
is the documented blind spot at `ui-components.instructions.md:88-95` — so it would pass
the build while being exactly the "is the library already drawing this shape?" review
failure the instruction names. `SelectField` is the library drawing this shape.

`AccountOptions()` returns `IReadOnlyList<SelectorOption>` built from
`GitHub.Settings.Current.Accounts`, with `Hint` carrying "no credential on this machine"
for an unsatisfied account.

### Architecture-test constraints this must respect

- No raw `<select>`, `<input>`, `<button>` or `<textarea>` anywhere in the new markup —
  `AllowedRawControls` is empty and must stay empty.
- No plain element wearing a library class — `AllowedComponentClasses` is exactly
  `badge--gh`, `md-link`, `pane-resizer` and must stay so.
- The `Settings/` folder must **not** gain an `_Imports.razor`
  (`Settings.razor:24-31`); `@using Backlog.UI.Components.Selects` (or whatever
  `_Imports` in `Backlog.Desktop.UI` already provides — check first) goes on the page.
- `@namespace Backlog.Desktop.UI.Shell` stays.

---

## 9. Decision 7 — migration, precisely

Three shapes only, all additive, all idempotent by construction, no version field —
ADR 0006's discipline applied by convention (§2, correction).

### GitHub: shared registry (`{Root}/config/repos.json`)

- `RegistryRepositoryDto` gains `public string? Account { get; set; }`, emitted with
  `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`.
- `RegistryRow` (`GitHubSettings.cs:1043-1077`) gains `Account`; `RegistryRow.From`
  passes it through unchanged (it is a label, not a coordinate — no parsing, no
  validation at read time).
- **Reading an old registry:** no `account` key → every repository unbound → today's
  behaviour.
- **Writing an unbound workspace:** the key is omitted, so the file is **byte-identical**
  to what today's build writes. An install that never uses accounts produces no diff.
- `WriteRegistry` (`GitHubSettings.cs:562-573`) adds `Account = r.Account`.

### GitHub: local file (`%LOCALAPPDATA%/Backlog/github.json`)

- `SettingsDto` gains `public List<AccountDto> Accounts { get; set; } = [];`
  Absent → empty. Absent is today's every install.
- `Compose` (`:797-843`) joins the account list in and sets `Account` on each
  `GitHubRepositoryRef` from the registry row.
- **The legacy per-repository `token` is not migrated into an account.** It stays where
  it is and keeps working as precedence rule 2 (§4). Deliberate: a token attached to a
  repository is *not* evidence of an account identity — the login it belongs to is
  unknown without a round trip, and inventing one would be exactly the wrong-identity
  failure being fixed. **Nothing is lost, and nothing is guessed.**
- The frozen legacy fields (`Alias`, `Owner`, `Name`, `Colour` on `RepositoryDto`,
  `:1120-1156`) are untouched. Do not add to them; the comment there says so.
- **`RegistryState.Unreadable` behaviour is preserved wholesale.** `SetRepositoryAccount`
  refuses; `LocalRowsFor`'s `preserve` path (`:641-700`) is unchanged; accounts are local
  so they survive an unreadable registry, while bindings — which are registry data —
  correctly read as absent, i.e. every repository falls back to the default. Degrade,
  do not fail.

### Claude (`%LOCALAPPDATA%/Backlog/claude.json`)

As §7a: legacy scalars → `Organizations[0]` with `Id = "default"`, legacy fields frozen
and written back null, condition self-extinguishing after one write.

### Sessions (`%LOCALAPPDATA%/Backlog/agents.json`)

New file. Absent → the two defaults `~/.claude` and `~/.copilot`, which is today's
behaviour exactly. Nothing to migrate.

### The idempotence argument, per ADR 0006's three shapes

| Change | Shape | Why it matches nothing the second time |
|---|---|---|
| `account` on a registry row | add a field | absent reads as null; writing null omits the key |
| `accounts` in the local file | add a field | absent reads as empty |
| Claude scalars → `Organizations[0]` | seed a new field where there was nothing | guarded on `Organizations.Count == 0`; false after the first write |
| legacy Claude scalars written null | rewrite a retired value | the field's reader is gone; matches nothing once written |

Nothing is dropped, renamed, narrowed or overwritten. No destructive statement exists to
need a backup, satisfying inherited ADR 0014's prohibition structurally.

---

## 10. Decision 8 — staged implementation order

Each stage builds and tests green on its own. **Stage 0 is mandatory and comes first.**

### Stage 0 — characterization tests (no production change)

`tests/` has **zero** references to `ResolvingGitHubTransport` and **zero** to
`GhCliTransport`. Pin them before touching them.

- New `tests/Backlog.Infrastructure.GitHub.UnitTests/GhCliTransportTests.cs`. The ctor
  already takes `GhCliTransport(string executable = "gh")`, so point it at a stub
  executable copied to the test output. Pin: the two argv shapes (`api user`;
  `api --method {VERB} {path} --header X-GitHub-Api-Version: {v} [--input -]`), the
  default-version substitution, non-zero exit → `GitHubException` carrying stderr, empty
  stderr → the `$"The GitHub CLI failed on {method} {path}."` message, non-JSON stdout →
  `"The GitHub CLI returned something that wasn't JSON."`, empty stdout → a `null`
  element, `Account` scraped from `login`, and `Invalidate()` clearing both fields.
- New `ResolvingGitHubTransportTests.cs`. Pin: CLI preferred over token; token used when
  the CLI is unavailable; `GitHubNotConfiguredException` with today's exact message when
  neither; `DescribeAsync`'s three summaries verbatim; `Invalidate()` reaching the CLI.
  Both collaborators are already optional ctor parameters, but they are concrete sealed
  types — so drive `GhCliTransport` via the stub executable here too. Stage 2 introduces
  the interface that makes this cleaner; the characterization test is what proves stage 2
  changed nothing it did not mean to.
- Extend the existing `TokenTransportTests.cs` with the current `TokenForPath` semantics,
  **including the first-token fallback** — so its deliberate removal in stage 2 shows up
  as an edited test with a reason, not a silent behaviour change.

### Stage 1 — model, persistence, migration (Infrastructure.GitHub only)

`GitHubAccount`, `GitHubCredentialKind`, `GitHubRepositoryRef.Account`,
`GitHubSettings.Accounts`, `AccountForPath`, store mutators (`SetAccounts`,
`SetAccountCredential`, `RemoveAccount`, `SetRepositoryAccount`), DTO fields, carry-over,
and the `PreserveExistingRepositorySettings` fix.
No transport change. Tests: new `AccountBindingTests.cs` in the
`RepositoryRegistrySplitTests` idiom (assert against the **files**, not only `Current`),
plus `AccountForPath` table tests over all five path shapes.

### Stage 2 — credential resolution (the behaviour fix)

`IGhCliAccountSource` + implementation; `IGitHubCredentialResolver` + implementation;
`TokenTransport` takes the resolver; `ResolvingGitHubTransport` composes it and **fixes
the `Current` snapshot bug**; `GhCliTransport` narrowed to the default path; the
first-token fallback removed. Two host registrations updated.
**After this stage the reported 404 is gone.**

### Stage 3 — Settings: Accounts panel + binding control

`SettingsPage.Accounts`, the panel, the `SelectField` on the repository card. bUnit tests
copying `SettingsRepositoryColourTests.RenderSettings()` (`:148-178`) — including its
`NoGitHub`/`NoProbe` fakes and its `OpenRepositoriesTab` helper, plus an
`OpenAccountsTab` twin.

### Stage 4 — Claude organizations

`ClaudeOrganization`, list + carry-over, compatibility properties, and the Claude section
of the Accounts panel (the first admin-key UI). No consumer changes.

### Stage 5 — sessions across accounts

`AgentAccountsStore` (placement verified against the module boundary tests first),
`LocalAgentSessionSource` multi-home, `AgentSession.Account`, the grouping axis,
`SessionRegistration` stops discarding the provider, both hosts updated.

### Stage 6 — per-account identity and reporting

`GitHubIdentityClient` keyed cache + `GetLoginForAsync`; `GitHubActivitySource` resolves
the login **inside** the repository loop; per-organization Claude reporting;
per-account Copilot/billing.

---

## 11. File-by-file

**Modified**

| File | Change |
|---|---|
| `src/Infrastructure/Backlog.Infrastructure.GitHub/GitHubRepositoryRef.cs` | `+ string? Account` |
| `…/GitHubSettings.cs` | `Accounts`; `AccountForPath` replacing `RepositoryFromApiPath`; `TokenForPath` fallback removed; `SetRepositoryAccount` + account mutators; `PreserveExistingRepositorySettings`/`NormalizeRepositories` carry `Account`; `RegistryRepositoryDto.Account`; `RegistryRow.Account`; `SettingsDto.Accounts`; `WriteRegistry`; `Compose` |
| `…/TokenTransport.cs` | ctor takes `IGitHubCredentialResolver`; `SendAsync` and `IsAvailableAsync` await it |
| `…/ResolvingGitHubTransport.cs` | composes the resolver; **fixes the `settings.Current` method-group snapshot**; `GhCliTransport` narrowed to the default path; `DescribeAsync` reports per account |
| `…/GhCliTransport.cs` | unchanged in stage 2 except its narrowed role; keep the class |
| `…/GitHubIdentityClient.cs` | per-account cache; `GetLoginForAsync`; takes `GitHubSettingsStore` *(stage 6)* |
| `src/Infrastructure/Backlog.Infrastructure.Claude/ClaudeSettings.cs` | `ClaudeOrganization`; `Organizations`; frozen legacy scalars; compatibility properties; carry-over in `Read()` |
| `src/App/Backlog.Desktop.UI/Settings/Settings.razor` | `SettingsPage.Accounts`; the panel; `SelectField` binding control; actor field moves; `ApplyRepositoryAccount` and drafts |
| `src/Modules/Sessions/…/Adapters/LocalAgentSessionSource.cs` | multi-home; update the XML doc's arity claim |
| `src/Modules/Sessions/…/Extensions/SessionRegistration.cs` | stop discarding the provider; update the remarks |
| `src/Modules/Sessions/…Abstractions/Sessions.cs` | `AgentSession.Account`; grouping axis |
| `src/Modules/Dashboard/…/Adapters/GitHubActivitySource.cs` | resolve the login inside the loop; update the `:12-16` doc *(stage 6)* |
| `src/App/Backlog.Desktop/MauiProgram.cs` | resolver + gh source registrations; `AddAgentSessionSource` |
| `src/Harness/Backlog.Desktop.WebHarness/Program.cs` | the same two |

**New (production)**

`…/GitHubAccount.cs`, `…/GhCliAccountSource.cs`, `…/GitHubCredentialResolver.cs`,
`src/Modules/Sessions/…/AgentAccountsStore.cs`.

**New (tests)**

`GhCliTransportTests.cs`, `ResolvingGitHubTransportTests.cs`, `AccountBindingTests.cs`,
`GitHubCredentialResolverTests.cs`, `SettingsAccountsTests.cs`,
`ClaudeOrganizationMigrationTests.cs`, `AgentSessionAccountTests.cs`; extend
`TokenTransportTests.cs`.

**Must not change**

`.github/skills/`, `agents/`, `instructions/`, `skills/`; the exception lists in
`SharedControlAdoptionTests.cs`; `AllowedConsoleWindows` in `ProcessLaunchTests.cs`;
the `Settings/` folder must gain no `_Imports.razor`.

---

## 12. Risks, and the tests that retire them

| Risk | Retired by |
|---|---|
| Editing the repositories textarea silently drops every binding | `Binding_survives_the_repository_list_being_retyped` (stage 1) — the highest-value single test in this change |
| A gh-sourced `gho_` token is written to disk and goes stale | `A_gh_sourced_token_never_reaches_the_settings_file` (stage 2) |
| A bound repository silently falls back to another identity | `A_bound_repository_never_borrows_another_accounts_credential` (stage 2) |
| `GhCliTransport`/`ResolvingGitHubTransport` change behaviour unnoticed | stage 0 characterization tests, written first |
| The new panel trips the shared-control rule | `SelectField` + existing components only; no exception-list edits |
| `AgentAccountsStore` placement violates a module boundary | verify against `ModuleSurfaceTests`/`ModuleBoundaryTests` before writing (stage 5) |

**Open questions for the implementer, to raise rather than decide silently:**

1. Whether the Copilot `orgs/{org}` disagreement case (two repositories under one owner
   bound to different accounts) should warn in Settings rather than fall back to the
   default. Falling back is safe; naming it may be kinder.
2. Whether `AgentAccountsStore` belongs in `Sessions.UI` or `Sessions.Abstractions` —
   decided by the boundary tests, not by preference.
3. Whether stage 6's per-organization Claude reporting shows organizations side by side
   or summed. A product question, not an architecture one.

**Traceability.** Local ADR 0006 (discipline adopted by convention; see §2). Inherited
ADRs 0004 (mutators keep returning `string?`, unchanged), 0012/0013 (no new identity
provider; credentials stay on the machine), 0014 (no destructive migration), 0018 (no
new configuration section; these are user settings, not bound options).
`.arc42/08-crosscutting-concepts.md#authentication-and-authorization` should gain one
sentence in a follow-up: GitHub integration now selects a credential per repository
rather than per process. `.domain/` has **no** Account concept today and this change adds
none — a credential is an infrastructure concern, not domain vocabulary.

---

## 13. Amendment A — the pathless availability probe

Raised by the Stage 0 characterization tests, which pinned
`The_availability_probe_asks_with_no_path_and_means_any_token_anywhere`. Settle this
before Stage 2; it changes what Stage 2 must build.

### The gap

`TokenTransport.IsAvailableAsync` calls `_token(null)` — a pathless probe. Today its only
possible answer *is* the first-token-with-any-token fallback, because that is the only
branch `TokenForPath(null)` can reach. §4 says the fallback "must be removed for bound and
unbound alike"; §"Non-`repos/` paths" separately routes `null` to *default*, and default
(precedence rule 3) is the `gh` CLI, not a token.

Taken together those two statements make `TokenTransport` permanently unavailable, so on a
machine with no `gh` but a working repository token `ResolvingGitHubTransport` would raise
`GitHubNotConfiguredException`. That is a regression the design did not intend.

### The resolution

The fallback conflates two different questions. Split them, and delete only the one that
is the bug.

1. **"Which credential authenticates this path?"** — `AccountForPath(path)`. No cross-
   repository fallback, ever. A bound repository whose account has no usable credential
   fails naming it. This is the question every real request asks, and the first-token
   fallback is deleted from it. Unchanged from §4.

2. **"Is this machine configured to reach GitHub with a token at all?"** — a separate
   predicate, `GitHubSettings.HasAnyCredential` (a property, not a path lookup). True when
   any account holds a usable credential or any repository holds a token. This is the only
   question `IsAvailableAsync` asks, and it is never permitted to select a credential for
   a request.

`IGitHubCredentialResolver` therefore carries two members, not one: an async
`ResolveAsync(string? path, CancellationToken)` returning the credential for a real call,
and a synchronous availability predicate. `TokenTransport`'s ctor takes the resolver
rather than a bare `Func<string?, string?>`, so the pathless probe stops being expressed
as a null path at all — which is what made the two questions look like one.

### Consequence for Stage 0's pinned test

`The_availability_probe_asks_with_no_path_and_means_any_token_anywhere` is pinning a
*correct* outcome ("available") reached by a *wrong* route (the fallback). Stage 2 keeps
the outcome and rewrites the route, so the test is rewritten rather than deleted, and its
replacement asserts against `HasAnyCredential`. Say so in the commit.

The other characterization tests Stage 0 flagged as defect pins —
`A_token_configured_after_construction_is_invisible_to_the_transport` (the
`ResolvingGitHubTransport.cs:36` method-group snapshot) — are expected to *fail* at Stage
2 and be rewritten to assert the fixed behaviour. That is the intended signal, not a
breakage.

### Corrections to §5 from Stage 0's findings, to be honoured by later stages

- `DescribeAsync` has **four** summaries, not three: the CLI branch also yields
  `"Connected through the GitHub CLI."` when `gh api user` answers without a `login`.
  All four are pinned verbatim.
- The `$"The GitHub CLI failed on {method} {path}."` message uses the **untrimmed** path
  while argv gets `path.TrimStart('/')`. Both are pinned; do not "tidy" this in Stage 2
  without editing the pin deliberately.
- `IsAvailableAsync`'s `catch (Exception)` swallows three distinct conditions — an
  executable that cannot start, a non-zero exit, and non-JSON stdout. Stage 2 narrows this
  transport to the default path and will touch that catch; three separate pins exist.
