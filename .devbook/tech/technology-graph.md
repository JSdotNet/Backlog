# Technology Graph

```meta
rating: adopted
related: [".devbook/arc42/04-solution-strategy.md#technology-choices", ".devbook/arc42/07-deployment-view.md", ".devbook/arc42/09-architecture-decisions.md", ".devbook/domain/devbook/features.md#technology-view"]
```

> The root view of `.devbook/tech`: which technologies this project is built with,
> how they layer on each other, and how mature each choice is. The layer files
> below hold the chapters.

```mermaid
flowchart TD
    Tooling["tooling.md"] -->|depends on| Shared["shared.md"]
    Tooling --> AiDev["ai-development.md"]
    AiDev --> Shared
    Testing["testing.md"] --> Shared
    Testing --> Desktop["desktop.md"]
    Ide["ide.md"] --> Shared
    Cloud["cloud.md"] --> Shared
    Mobile["mobile.md"] --> Cloud
    Mobile <--> Shared
    Desktop <--> Shared
```

Bootstrap is over for most of the stack. The shared runtimes and frameworks, the
desktop channel, the testing layer, and everything that builds and governs the
repository are in daily use and `adopted`.

These are still `candidate`:

- the Visual Studio channel (`Visual Studio Extensibility` and `WPF`) and the
  VS Code webview UI;
- the desktop's background workers, which are designed but not yet implemented;
- `Azure Web PubSub` and `Azure Notification Hubs`, the realtime nudge to the
  desktops and the phone;
- `Firebase Cloud Messaging`, the push transport behind the hub;
- `Xamarin.Firebase.Messaging`, its binding on the phone;
- the `Anthropic Claude Platform` and the `GitHub Copilot Usage APIs`, the two
  sources of measured AI usage.

Three things sit at `trial`:

- the Azure tier the sync service runs on, provisioned and deployed by hand, not yet through its workflow or in real use;
- the unattended schedules;
- the inventory scripts that check this graph's package versions.

## Layers

| Layer | File | Covers |
|---|---|---|
| Shared | [`shared.md`](shared.md) | Formats, languages, runtimes, and the frameworks and packages used by more than one channel |
| Desktop | [`desktop.md`](desktop.md) | The canonical local-first Windows client |
| IDE | [`ide.md`](ide.md) | VS Code and Visual Studio extensions |
| Mobile | [`mobile.md`](mobile.md) | The Android capture-and-review client |
| Cloud | [`cloud.md`](cloud.md) | The thin Azure sync service and the managed AI service the product calls |
| Testing | [`testing.md`](testing.md) | Test frameworks, runners, and end-to-end validation |
| AI development | [`ai-development.md`](ai-development.md) | The agent harnesses, protocols, and file conventions this repository is built *with* |
| Tooling | [`tooling.md`](tooling.md) | Build, packaging, CI/CD, deployment, and governance tooling |

The table is in reading order: `shared.md` first, `tooling.md` last, and the layers
between them by name.

## Graph

The application reads this metadata into the
[Technology view](../domain/devbook/features.md#technology-view) of its Devbook
pane. One tab bar switches between five views over the same graph:

- **Overview** tables every technology with its measures.
- **Dependencies** follows one technology's edges hop by hop.
- **Complexity** places each technology by how many use it against how much sits
  under it.
- **Clusters** opens the stack layer file by layer file.
- **Type lenses** reads one technology through the section its type chooses.

The application computes every measure from `depends-on`: used by, what breaks if
it changes, what it rests on, its depth, and a complexity band.

It colours a technology by its type's family and draws it hollow until it is
`adopted`, so the ladder position never rests on colour. Every pointer view has a
list or table companion the keyboard operates. A person changes a rating from
the inspector, which writes it back to the technology's chapter.

The Mermaid graph below is the portable fallback for reading this file without
the application. Its edges point from a technology to what it sits on, mirroring
each chapter's `depends-on` field.

<details>
<summary>Mermaid source graph</summary>
```mermaid
flowchart LR
    subgraph Legend["Status legend"]
        CandidateLegend["candidate"]:::candidate
        TrialLegend["trial"]:::trial
        AdoptedLegend["adopted"]:::adopted
        HoldLegend["hold"]:::hold
        FoundationLegend["foundation / external"]:::foundation
    end

    subgraph Foundations["Foundations"]
        Windows["Windows"]:::adopted
        Android["Android"]:::adopted
        JavaJdk["Java JDK"]:::adopted
        GitHubPlatform["GitHub Platform"]:::adopted
        Anthropic["Anthropic Claude Platform"]:::candidate
        Foundry["Azure AI Foundry"]:::adopted
        Cosmos["Azure Cosmos DB"]:::trial
        KeyVault["Azure Key Vault"]:::trial
        AzureMonitor["Azure Monitor"]:::trial
        FCM["Firebase Cloud Messaging"]:::candidate
        Git["Git"]:::adopted
        PowerShell["PowerShell"]:::adopted
        AzureCli["Azure CLI"]:::adopted
        ClaudeCode["Claude Code"]:::adopted
        YAML["YAML"]:::adopted
        Markdown["Markdown"]:::adopted
        JSON["JSON"]:::adopted
        DotNet[".NET Runtime"]:::adopted
        NodeJS["Node.js"]:::adopted
    end

    subgraph Shared["Shared technologies"]
        Mermaid["Mermaid"]:::adopted
        CSharp["C# Language"]:::adopted
        TypeScript["TypeScript"]:::adopted
        AspNetCore["ASP.NET Core"]:::adopted
        RazorComponents["Razor Components"]:::adopted
        BlazorServer["Blazor Server"]:::adopted
        MAUI[".NET MAUI"]:::adopted
        BlazorHybrid["Blazor Hybrid"]:::adopted
        Aspire[".NET Aspire"]:::adopted
        Otel["OpenTelemetry"]:::adopted
        Sqlite["SQLite"]:::adopted
        DataSqlite["Microsoft.Data.Sqlite"]:::adopted
        MexDi["Microsoft.Extensions.DependencyInjection"]:::adopted
        MexHttp["Microsoft.Extensions.Http"]:::adopted
        MexResilience["Microsoft.Extensions.Http.Resilience"]:::adopted
        MexDiscovery["Microsoft.Extensions.ServiceDiscovery"]:::adopted
        MexLogging["Microsoft.Extensions.Logging"]:::adopted
        Polly["Polly"]:::adopted
        YamlDotNet["YamlDotNet"]:::hold
        CopilotUsage["GitHub Copilot Usage APIs"]:::candidate
    end

    subgraph ProductChannels["Product channels"]
        direction TB

        subgraph Desktop["Desktop - local-first Windows client"]
            WinAppSDK["Windows App SDK"]:::adopted
            WinUI["WinUI 3"]:::adopted
            WebView2["WebView2"]:::adopted
            LocalStore["Local Task Store"]:::adopted
            Workers["Background Workers"]:::candidate
            ProtectedData["System.Security.Cryptography.ProtectedData"]:::adopted
            McpSdk["Model Context Protocol C# SDK"]:::adopted
            Roslyn["Microsoft.CodeAnalysis.CSharp"]:::adopted
            GhCli["GitHub CLI"]:::adopted
            MSIX["MSIX Packaging"]:::adopted
            AppInstaller["App Installer (.appinstaller)"]:::adopted
        end

        subgraph Mobile["Mobile - Android capture and review"]
            OfflineStore["Local Offline Store"]:::adopted
            SkiaSharp["SkiaSharp"]:::adopted
            FirebaseMessaging["Xamarin.Firebase.Messaging"]:::candidate
            AndroidTools["Android SDK Build Tools"]:::adopted
            APK["APK Packaging"]:::adopted
        end

        subgraph IDE["IDE - editor extensions"]
            VSCodeApi["VS Code Extension API"]:::adopted
            Webview["VS Code Webview UI"]:::candidate
            VSSDK["Visual Studio Extensibility"]:::candidate
            WPF["WPF"]:::candidate
        end

        subgraph Cloud["Cloud - Azure services"]
            MinimalApis["ASP.NET Core Minimal APIs"]:::adopted
            JwtBearer["Microsoft.AspNetCore.Authentication.JwtBearer"]:::adopted
            JsonWebTokens["Microsoft.IdentityModel.JsonWebTokens"]:::adopted
            OpenApi["Microsoft.AspNetCore.OpenApi"]:::adopted
            AzureIdentity["Azure.Identity"]:::adopted
            BlobStorage["Azure Blob Storage"]:::trial
            ACA["Azure Container Apps"]:::trial
            WebPubSub["Azure Web PubSub"]:::candidate
            NotificationHubs["Azure Notification Hubs"]:::candidate
        end
    end

    subgraph Testing["Testing and validation"]
        XUnit["xUnit v3"]:::adopted
        MTP["Microsoft.Testing.Platform"]:::adopted
        TestSdk["Microsoft.NET.Test.Sdk"]:::adopted
        VsRunner["xunit.runner.visualstudio"]:::adopted
        TrxReport["Microsoft.Testing.Extensions.TrxReport"]:::adopted
        CodeCoverage["Microsoft.Testing.Extensions.CodeCoverage"]:::adopted
        MvcTesting["Microsoft.AspNetCore.Mvc.Testing"]:::adopted
        FakeTime["Microsoft.Extensions.TimeProvider.Testing"]:::adopted
        BUnit["bUnit"]:::adopted
        Playwright["Playwright"]:::adopted
    end

    subgraph AiDev["AI-assisted development"]
        Plugins["Claude Code Plugins"]:::adopted
        Skills["Agent Skills"]:::adopted
        Subagents["Subagents"]:::adopted
        MCP["Model Context Protocol Servers"]:::adopted
        DeliveryEngine["Delivery Engine"]:::adopted
        DeliverySchedule["Delivery Schedule"]:::trial
        Surfaces["Delivery Surfaces"]:::adopted
        BacklogTools["Backlog Tools Plugin"]:::adopted
        InstructionFiles["Repository Instruction Files"]:::adopted
        Hooks["Claude Code Hooks"]:::adopted
        DevbookPlugin["Devbook Plugin"]:::adopted
        DevbookConfig["Devbook Config"]:::adopted
        DevbookProcedures["Devbook Procedures"]:::retired
    end

    subgraph Tooling["Build and governance tooling"]
        DotNetSdk[".NET SDK"]:::adopted
        MSBuild["MSBuild"]:::adopted
        MauiWorkloads[".NET MAUI Workloads"]:::adopted
        NuGet["NuGet"]:::adopted
        CPM["Central Package Management"]:::adopted
        Npm["npm"]:::adopted
        Bicep["Bicep"]:::adopted
        Azd["Azure Developer CLI"]:::adopted
        AspireCli["Aspire CLI"]:::adopted
        Actions["GitHub Actions"]:::adopted
        CodeQL["CodeQL"]:::adopted
        Dependabot["Dependabot"]:::adopted
        KnowledgeMeta["devbook-meta Generator"]:::adopted
        DevbookTech["devbook-tech Inventory Scripts"]:::trial
        DbWriter["Devbook Database Writer"]:::adopted
        C4Hero["c4hero"]:::adopted
        Archify["Archify"]:::adopted
        Ajv["Ajv"]:::adopted
        SimpleIcons["simple-icons"]:::adopted
    end

    Mermaid --> Markdown
    CSharp --> DotNet
    TypeScript --> NodeJS
    AspNetCore --> DotNet
    AspNetCore --> CSharp
    RazorComponents --> AspNetCore
    RazorComponents --> CSharp
    BlazorServer --> RazorComponents
    BlazorServer --> AspNetCore
    MAUI --> WinUI
    MAUI --> Android
    MAUI --> CSharp
    BlazorHybrid --> MAUI
    BlazorHybrid --> RazorComponents
    Aspire --> AspNetCore
    Aspire --> CSharp
    Otel --> DotNet
    Sqlite --> Markdown
    DataSqlite --> Sqlite
    DataSqlite --> DotNet
    MexDi --> DotNet
    MexHttp --> DotNet
    MexResilience --> MexHttp
    MexDiscovery --> Aspire
    MexLogging --> DotNet
    Polly --> DotNet
    YamlDotNet --> DotNet
    CopilotUsage --> GitHubPlatform

    WinAppSDK --> Windows
    WinAppSDK --> DotNet
    WinUI --> WinAppSDK
    WinUI --> CSharp
    WebView2 --> WinAppSDK
    LocalStore --> Sqlite
    LocalStore --> DataSqlite
    LocalStore --> JSON
    Workers --> DotNet
    ProtectedData --> Windows
    ProtectedData --> DotNet
    McpSdk --> AspNetCore
    McpSdk --> DotNet
    Roslyn --> DotNet
    GhCli --> GitHubPlatform
    MSIX --> WinAppSDK
    AppInstaller --> MSIX

    OfflineStore --> Sqlite
    OfflineStore --> DataSqlite
    OfflineStore --> JSON
    SkiaSharp --> DotNet
    FirebaseMessaging --> Android
    FirebaseMessaging --> FCM
    AndroidTools --> Android
    AndroidTools --> JavaJdk
    APK --> Android
    APK --> AndroidTools

    VSCodeApi --> TypeScript
    VSCodeApi --> NodeJS
    Webview --> VSCodeApi
    VSSDK --> CSharp
    WPF --> VSSDK
    WPF --> DotNet

    MinimalApis --> AspNetCore
    MinimalApis --> CSharp
    JwtBearer --> AspNetCore
    JsonWebTokens --> DotNet
    OpenApi --> AspNetCore
    AzureIdentity --> DotNet
    ACA --> MinimalApis
    BlobStorage --> MinimalApis
    WebPubSub --> MinimalApis
    NotificationHubs --> MinimalApis
    NotificationHubs --> FCM

    XUnit --> DotNet
    XUnit --> CSharp
    MTP --> XUnit
    TestSdk --> MTP
    VsRunner --> XUnit
    TrxReport --> MTP
    CodeCoverage --> MTP
    MvcTesting --> AspNetCore
    MvcTesting --> XUnit
    FakeTime --> DotNet
    BUnit --> RazorComponents
    BUnit --> XUnit
    Playwright --> BlazorServer
    Playwright --> WebView2

    Plugins --> ClaudeCode
    Skills --> Plugins
    Skills --> Markdown
    Skills --> YAML
    Subagents --> Plugins
    MCP --> ClaudeCode
    DeliveryEngine --> Plugins
    DeliverySchedule --> DeliveryEngine
    DeliverySchedule --> ClaudeCode
    Surfaces --> MCP
    Surfaces --> DeliveryEngine
    BacklogTools --> Plugins
    BacklogTools --> Skills
    BacklogTools --> Hooks
    InstructionFiles --> Markdown
    Hooks --> ClaudeCode
    DevbookPlugin --> Plugins
    DevbookPlugin --> Skills
    DevbookPlugin --> NodeJS
    DevbookConfig --> Plugins
    DevbookConfig --> JSON
    DevbookProcedures --> Plugins
    DevbookProcedures --> Skills

    DotNetSdk --> DotNet
    MSBuild --> DotNetSdk
    MauiWorkloads --> DotNetSdk
    MauiWorkloads --> MAUI
    NuGet --> DotNetSdk
    CPM --> NuGet
    Npm --> NodeJS
    Bicep --> AzureCli
    Azd --> Bicep
    AspireCli --> Aspire
    Actions --> GitHubPlatform
    CodeQL --> Actions
    Dependabot --> GitHubPlatform
    KnowledgeMeta --> NodeJS
    KnowledgeMeta --> JSON
    KnowledgeMeta --> DevbookPlugin
    DevbookTech --> NodeJS
    DevbookTech --> JSON
    DevbookTech --> DevbookPlugin
    DbWriter --> NodeJS
    DbWriter --> Sqlite
    C4Hero --> NodeJS
    Archify --> NodeJS
    Archify --> Mermaid
    Ajv --> Archify
    Ajv --> JSON
    SimpleIcons --> Archify

    classDef adopted fill:#1f6f4a,stroke:#9be7c3,color:#fff,stroke-width:2px
    classDef candidate fill:#2b3245,stroke:#8aa4ff,color:#fff,stroke-width:1.5px
    classDef trial fill:#2b4a45,stroke:#8ae0d4,color:#fff,stroke-width:1.5px
    classDef hold fill:#4a2b2b,stroke:#ffa8a8,color:#fff,stroke-width:1.5px
    classDef retired fill:#2e2e2e,stroke:#9a9a9a,color:#ccc,stroke-width:1px,stroke-dasharray:4 3
    classDef foundation fill:#3a2f14,stroke:#ffd166,color:#fff,stroke-width:1.5px
```

</details>

Nodes without an outgoing edge are foundations, with nothing below them in this
project. They fall into five groups:

- the three text formats: `Markdown`, `JSON`, `YAML`;
- the two base runtimes: `.NET Runtime`, `Node.js`;
- the two operating systems and the JDK: `Windows`, `Android`, `Java JDK`;
- the external services: `GitHub Platform`, `Anthropic Claude Platform`,
  `Azure AI Foundry`, `Azure Cosmos DB`, `Azure Key Vault`, `Azure Monitor`,
  `Firebase Cloud Messaging`;
- the four host-level tools: `Git`, `PowerShell`, `Azure CLI`, `Claude Code`.

## Status ladder

| Rating | Meaning |
|---|---|
| `candidate` | Named as the intended choice, not yet validated by real use |
| `trial` | Being tried out in a limited, reversible way |
| `adopted` | In active use and the default choice for its role |
| `hold` | Kept but no longer expanded; avoid new usage |
| `retired` | No longer used; kept for history |

## AI development vocabulary

The [AI Coding Dictionary](https://www.aicodingdictionary.com/) names the
concepts of agent-driven development. The tables below index its terms: the
right-hand column says what each denotes **in this repository** and which chapter
of [`ai-development.md`](ai-development.md) carries the technology behind it.
How each is used, stage by stage, is the AI adoption record in
[`.devbook/ai/`](../ai/adoption-map.md).

### The model

| Term | In this repository |
|---|---|
| Model provider | Two: Anthropic for the harness, via [Claude Code](ai-development.md#claude-code), and [Azure AI Foundry](cloud.md#azure-ai-foundry) for the product |
| Model, inference, next-token prediction, parameters, training | Vendor-side concepts. Nothing here configures them. |
| Harness | [Claude Code](ai-development.md#claude-code) |
| Effort | Not configured. `CLAUDE.md` records that this repository sets no model or effort override. |
| Token, input/output tokens, cache tokens, prefix cache | Measured for the Productivity domain, not configured. |
| Non-determinism | Why the [testing layer](testing.md) exists in the shape it does. |

- **Model provider:** Azure AI Foundry serves `gpt-5-4`, `gpt-5-5`, and `gpt-5-6-luna`.
  A balanced model, a speech model and an embedding model sit behind Bicep parameters.
- **Effort:** Runs take each plugin's default.
- **Token:** `Backlog.Infrastructure.Claude` imports token counts and cost from the Admin
  API, and `Backlog.Infrastructure.GitHub` does the same for Copilot.
- **Non-determinism:** Deterministic checks (`dotnet test`, the devbook-meta check, CodeQL)
  gate what a non-deterministic agent produces.

### Sessions, context windows, and turns

| Term | In this repository |
|---|---|
| Agent | Every `flow-*` run of the [delivery engine](ai-development.md#delivery-engine), plus its [subagents](ai-development.md#subagents) |
| Session | One worktree's run, the isolation unit: a [worktree session per change](../ai/02-code.md#worktree-session-per-change), on [Git](tooling.md#git) |
| System prompt | Composed from the harness plus [repository instruction files](ai-development.md#repository-instruction-files) |
| Context window | The budget a run's record on the [delivery surfaces](ai-development.md#delivery-surfaces) outlives: a resumed session reattaches to it |
| Turn, stateful, stateless | Harness-level. The repository's own statefulness is the dashboard run record and the checked-in devbook folders. |

- **Session:** `.claude/` holds per-session state.

### Tools and environment

| Term | In this repository |
|---|---|
| MCP | [Model Context Protocol servers](ai-development.md#model-context-protocol-servers) |
| Tool, tool call, tool result | The MCP surfaces above, plus the harness's own file and shell tools |
| Environment | [.NET Aspire](shared.md#net-aspire)'s app model: a running system, logs, and traces for an agent to observe |
| Filesystem | The worktree. `.agents/rules/context-loading.md` limits which devbook folders a given workflow may read. |
| Sandbox | The git worktree, plus `aspire start --isolated` for ports and user-secrets state |
| Permission mode, permission request | Harness-level and unpinned. The repository-level equivalent is Personal Validation, which no run may skip. |

- **MCP:** The servers are Backlog's own, Aspire, [Playwright](testing.md#playwright), Microsoft Learn, and the [delivery surfaces](ai-development.md#delivery-surfaces). Guidance is not one of them. It is checked in under
  `.devbook/arc42/adr/guidelines/` and `.devbook/design/`.

### Failure modes

| Term | In this repository |
|---|---|
| Knowledge cutoff | Why agents consult the `microsoft-learn` MCP server and the guidelines under `.devbook/arc42/adr/guidelines/` rather than memory |
| Parametric vs. contextual knowledge | The whole reason the five `.devbook/` folders are checked in: project facts are read, not recalled |
| Attention degradation, smart zone | Why long runs hand off rather than continue, and why chapters are kept short |
| Hallucination | Deterministic checks catch it: a broken `depends-on` fails the devbook-meta check, a wrong API the build |
| Sycophancy | Every `flow-*` run closes on a review and on Personal Validation: a person decides, not the agent |

### Handoffs

| Term | In this repository |
|---|---|
| Handoff, handoff artifact | The run record on the [delivery surfaces](ai-development.md#delivery-surfaces): a resumed session reattaches instead of opening a duplicate |
| Compaction, autocompact, clearing | Harness-level. The standing brief and the devbook folders make a fresh session cheap to start. |
| Primary source | The code, `Directory.Packages.props`, the workflows, `.devbook/arc42` — what a `.devbook/tech` chapter is written *from* |
| Secondary source | This folder. `.devbook/tech` records outcomes and `.devbook/arc42` the reasoning; `.devbook/arc42` wins a disagreement. |
| Spec, ticket | Backlog entries (local ADR 0007) and the GitHub issues they sync with |

### Memory and steering

| Term | In this repository |
|---|---|
| AGENTS.md | `AGENTS.md`, imported by `CLAUDE.md` — see [repository instruction files](ai-development.md#repository-instruction-files) |
| Context pointer | Every `related` and `depends-on` reference, and the standing brief's links into `.agents/rules/` |
| Progressive disclosure | The design of the whole devbook convention, from a short brief down to its chapters |
| Skill | [Agent Skills](ai-development.md#agent-skills): the plugin `flow-*` and `schedule-*` skills, and the `.agents/skills/` procedures |
| Subagent | [Subagents](ai-development.md#subagents) — `architecture:architect`, `csharp-coding:coding`, `qa:qa`, and the rest |
| Memory system | Not adopted. Cross-session state is the checked-in devbook folders and the delivery surfaces' run record. |

- **Progressive disclosure:** A short brief points at scoped instruction files, which point
  at chapters, which point at each other.
- **Memory system:** Keeping cross-session state in those two places is deliberate, because
  a human can read both.

### Patterns of work

| Term | In this repository |
|---|---|
| Automated check | The deterministic gates listed below |
| Automated review | The [scheduled merge review](../ai/05-unattended-runs.md#scheduled-merge-review), and the `phase-verify` stage every code-modifying flow runs |
| Human review | [Personal Validation](../ai/03-test.md#personal-validation-gate) — the gate no flow may skip and no agent may self-approve |
| Human-in-the-loop | The default mode of every `flow-*` run |
| AFK | The [unattended runs](../ai/05-unattended-runs.md) selected from the `delivery-schedule` catalog |
| AX | What `src/Harness/` is for: it gives the UI a URL an agent can drive |
| DX | The Aspire one-command start, and MSBuild defaults a new project cannot forget |
| Design concept, grilling, prototyping | `.devbook/design/` and the `ux-design` plugin's wireframe and review skills |
| Vibe coding | Explicitly not the model here: the [code gate](../ai/02-code.md#code-gate-through-flow-code) exists to prevent it |

- **Automated check:** `dotnet test`, `dotnet build`, CodeQL, the devbook-meta check and `check-metadata.mjs`, `apksigner verify`, and Archify's 9/9 validation.
- **AX:** An agent cannot drive a MAUI head, so the harnesses serve the same UI at a URL.

## How to read and extend this graph

- Each `## <Technology>` chapter in a layer file is one node. Its `depends-on`
  list, in `<path>#<heading-slug>` references, is its outgoing edges.
- A technology is documented **once**, in the layer that owns it. Anything used
  by two or more channels moves to `shared.md`.
- Rationale lives in `.devbook/arc42`. Chapters here link to it with `related`.
- When a node or edge changes, update the Mermaid graph in the same change and
  run `node .devbook/_tools/devbook-meta/build.mjs --check`.
- The Technology view needs no editing of its own. The app reads the technologies
  and edges from the layer files, so it draws the same graph with or without the
  devbook database (local ADR 0004).

Full authoring rules: `.agents/rules/devbook-tech.md`.

## Open questions

- Mobile's shape has no ADR. `Backlog.Mobile` is .NET MAUI with Blazor Hybrid: its
  `MainPage` hosts a `BlazorWebView` over `Backlog.Mobile.UI`. Desktop's shape is
  in [ADR 0001](../arc42/adr/0001-desktop-stack-maui-blazor-hybrid.md).
- Transitive package pinning is off on purpose
  ([Central Package Management](tooling.md#central-package-management)), and
  nobody has scheduled the reviewed change that turns it on. `YamlDotNet` is the
  visible symptom: no project references it, it arrives only transitively, and it has no central pin.
- The Visual Studio channel has no project yet, so `Visual Studio Extensibility`
  and `WPF` remain named intentions.
