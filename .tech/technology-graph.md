# Technology Graph

```meta
status: candidate
related: [".arc42/04-solution-strategy.md#technology-choices", ".arc42/07-deployment-view.md", ".arc42/09-architecture-decisions.md"]
```

> The root view of `.tech`: which technologies this project is built with, how
> they layer on each other, and how mature each choice is. Individual
> technologies are documented as chapters in the layer files listed below.

The project is in bootstrap, so most nodes are `candidate` — named as the
intended choice in `.arc42` but not yet proven by running code. The tooling
layer is the exception: it is already in daily use.

## Layers

| Layer | File | Covers |
|---|---|---|
| Shared | [`shared.md`](shared.md) | Formats, languages, and runtimes used by more than one channel |
| Desktop | [`desktop.md`](desktop.md) | The canonical local-first Windows client |
| Mobile | [`mobile.md`](mobile.md) | The Android capture-and-review client |
| IDE | [`ide.md`](ide.md) | VS Code and Visual Studio extensions |
| Cloud | [`cloud.md`](cloud.md) | The optional, thin Azure sync service |
| Tooling | [`tooling.md`](tooling.md) | Build, CI/CD, AI, and governance tooling for this repository |

## Graph

Edges point from a technology to what it sits on top of, mirroring the
`depends-on` field of each chapter.

```mermaid
flowchart TB
    subgraph Shared
        Markdown["Markdown"]
        JSON["JSON"]
        YAML["YAML"]
        Mermaid["Mermaid"]
        DotNet[".NET Runtime"]
        CSharp["C#"]
        NodeJS["Node.js"]
        TypeScript["TypeScript"]
        GitHubPlatform["GitHub Platform"]
    end

    subgraph Desktop
        Windows["Windows"]
        WinAppSDK["Windows App SDK"]
        WinUI["WinUI 3"]
        LocalStore["Local Markdown + JSON Store"]
        Workers["Background Workers"]
        GhCli["GitHub CLI"]
        MSIX["MSIX Packaging"]
    end

    subgraph Mobile
        Android["Android"]
        MAUI[".NET MAUI"]
        BlazorHybrid["Blazor Hybrid"]
        OfflineStore["Local Offline Store"]
    end

    subgraph IDE
        VSCodeApi["VS Code Extension API"]
        Webview["VS Code Webview UI"]
        VSSDK["Visual Studio Extensibility"]
        WPF["WPF"]
    end

    subgraph Cloud
        MinimalApis["ASP.NET Core Minimal APIs"]
        Aspire[".NET Aspire"]
        ACA["Azure Container Apps"]
        Cosmos["Azure Cosmos DB"]
        KeyVault["Azure Key Vault"]
        FCM["Firebase Cloud Messaging"]
    end

    subgraph Tooling
        Git["Git"]
        DotNetSdk[".NET SDK"]
        NuGet["NuGet"]
        Npm["npm"]
        Actions["GitHub Actions"]
        CodeQL["CodeQL"]
        Dependabot["Dependabot"]
        CopilotCli["GitHub Copilot CLI"]
        MCP["MCP Servers"]
        Canvas["Knowledge Canvas Extension"]
        AspireCli["Aspire CLI"]
    end

    Mermaid --> Markdown
    CSharp --> DotNet
    TypeScript --> NodeJS

    WinAppSDK --> Windows
    WinAppSDK --> DotNet
    WinUI --> WinAppSDK
    WinUI --> CSharp
    LocalStore --> Markdown
    LocalStore --> JSON
    Workers --> DotNet
    GhCli --> GitHubPlatform
    MSIX --> WinAppSDK

    MAUI --> Android
    MAUI --> CSharp
    BlazorHybrid --> MAUI
    OfflineStore --> JSON

    VSCodeApi --> TypeScript
    VSCodeApi --> NodeJS
    Webview --> VSCodeApi
    VSSDK --> CSharp
    WPF --> VSSDK
    WPF --> DotNet

    MinimalApis --> DotNet
    MinimalApis --> CSharp
    Aspire --> MinimalApis
    ACA --> MinimalApis

    DotNetSdk --> DotNet
    NuGet --> DotNetSdk
    Npm --> NodeJS
    Actions --> GitHubPlatform
    CodeQL --> Actions
    Dependabot --> GitHubPlatform
    CopilotCli --> GitHubPlatform
    MCP --> CopilotCli
    Canvas --> CopilotCli
    Canvas --> NodeJS
    Canvas --> Mermaid
    AspireCli --> Aspire
```

Nodes without an outgoing edge (`YAML`, `Git`, `Windows`, `Android`,
`GitHub Platform`, `Azure Cosmos DB`, `Azure Key Vault`,
`Firebase Cloud Messaging`) are foundations: nothing in this project sits below
them.

## Status ladder

| Status | Meaning |
|---|---|
| `candidate` | Named as the intended choice, not yet validated by real use |
| `trial` | Being tried out in a limited, reversible way |
| `adopted` | In active use and the default choice for its role |
| `hold` | Kept but no longer expanded; avoid new usage |
| `retired` | No longer used; kept for history |

## How to read and extend this graph

- Each `## <Technology>` chapter in a layer file is one node. Its
  `depends-on` list is its outgoing edges, using
  `<path>#<heading-slug>` references.
- A technology is documented **once**, in the layer that owns it. Anything used
  by two or more channels moves to `shared.md`.
- Rationale lives in `.arc42` (solution strategy, ADRs). Chapters here link to
  it with `related` instead of restating it.
- When a node or edge changes, update this diagram in the same change.

Full authoring rules: `.github/instructions/tech.instructions.md`.

## Open questions

- Desktop UI framework is preferred but not yet decided by ADR (WinUI 3 vs. a
  cross-platform alternative).
- Mobile shape is unsettled: MAUI native vs. Blazor Hybrid vs. PWA.
- Cloud data store choice (Cosmos DB vs. PostgreSQL) is still open in
  `.arc42/04-solution-strategy.md#technology-choices`.
- No versions are pinned yet beyond the target .NET runtime; `version` fields
  fill in as projects are created.
