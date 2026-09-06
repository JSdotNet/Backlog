using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.Modules.Knowledge.Abstractions;
using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Roadmap.Abstractions;
using Backlog.SharedKernel;

// The namespace deliberately does not match the folder, for the reason
// Settings.razor sets out at length in this same folder: a sibling namespace
// Backlog.Desktop.UI.Settings shadows the Settings component for everything
// under Backlog.Desktop.UI. This file sits beside the screen that renders it
// and keeps the Shell's published namespace.
namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// What features the app has, what they are called, and how they are described.
/// <para>
/// This is product copy rendered by one screen, so it lives with that screen
/// rather than in the shared kernel: nothing else in the solution needs to read
/// a feature's display name, and a catalog in the kernel would make every module
/// recompile for a wording change. What the modules do need — the keys — sits
/// with whatever owns each feature, and is only gathered here.
/// </para>
/// <para>
/// The keys declared on this class are the ones the Shell alone gates on. A key
/// any context also reads is that context's, and is referenced from there.
/// </para>
/// <para>
/// The catalog is also where a feature's <see cref="AppFeatureStatus"/> is
/// authored. Deliberately here and nowhere else: the status is a claim about how
/// finished something is, which is a judgement rather than something the code
/// can work out about itself, and keeping every such judgement on one screen of
/// one file is what makes it possible for a script to later write them from the
/// matching <c>.domain</c> chapters. A feature that says nothing is
/// <see cref="AppFeatureStatus.Released"/>, so the list stays quiet about the
/// ordinary case.
/// </para>
/// <para>
/// Two of the keys here have now made the same journey, which is worth recording
/// because it is the shape the rule predicts. Both sat in a <c>.UI</c> project
/// while their context had no domain module — no abstractions project to hold the
/// key, and one constant is no reason to create one. Both moved when the module
/// arrived: the dashboard key from <c>MonitoringFeatures</c> to
/// <c>DashboardFeatures</c>, and the roadmap key from the Roadmap <c>.UI</c>
/// project to <c>RoadmapFeatures</c> in its abstractions project. Neither string
/// changed, so nobody's settings file forgot what they had switched off.
/// </para>
/// </summary>
public static class AppFeatures
{
    /// <summary>Show the Inbox option and pane in the Home shell. The Inbox has
    /// no abstractions project — it is a UI project and nothing else — and one
    /// constant is not a reason to create one, so the Shell that is its only
    /// reader keeps it.</summary>
    public const string InboxPane = "inbox-pane";

    /// <summary>Report a Desktop app issue to GitHub from the app chrome.</summary>
    public const string FeedbackReporting = "feedback-reporting";

    /// <summary>The assistant panel in the app chrome.</summary>
    public const string AiAssistant = "ai-assistant";

    /// <summary>The AI usage report on the settings screen.</summary>
    public const string UsageMetrics = "usage-metrics";

    /// <summary>The catalog, in the order the settings screen lists it —
    /// the domain features first, then the cross-cutting ones, matching the two
    /// headings the screen draws. One list in one order, so what the screen
    /// shows and what the store is handed cannot disagree.</summary>
    public static IReadOnlyList<AppFeatureDefinition> All { get; } =
    [
        // --- Domain: an area of the product ---------------------------------
        new(TasksFeatures.Tasks, "Tasks", "Create, edit, filter, reorder, and store tasks.", AlwaysEnabled: true),
        new(InboxPane, "Inbox pane", "Show the Inbox option and pane in the Home shell.", EnabledByDefault: false, Status: AppFeatureStatus.Dev),
        new(RoadmapFeatures.Roadmap, "Roadmap band", "Show the roadmap band above the panes in the Home shell.", Status: AppFeatureStatus.Dev),
        new(KnowledgeFeatures.KnowledgeSections, "Knowledge sections", "Show design, architecture, domain, technology, and instruction sections in the knowledge pane and header."),
        new(KnowledgeFeatures.RepositoryKnowledge, "Repository knowledge", "Show the side pane for repository knowledge."),
        new(
            KnowledgeFeatures.ArchifyDiagrams,
            "Archify diagrams",
            "Draw a knowledge chapter's diagrams from their generated Archify artifacts where one exists, and offer to generate the rest. Chapters whose artifact is missing or was authored from an earlier version of the diagram keep their mermaid rendering.",
            EnabledByDefault: false,
            Status: AppFeatureStatus.Dev),
        new(
            KnowledgeFeatures.C4Diagrams,
            "C4 diagrams",
            "Show the C4 model kept beside the architecture chapters in .arc42/_c4/, authored as Structurizr DSL in c4hero. Its views are listed with the chapters, and a chapter that references a view links to it and back.",
            EnabledByDefault: false,
            Status: AppFeatureStatus.Dev),
        new(
            DevPcFeatures.SystemTools,
            "System tools",
            "Check, update, enable, and disable what this machine is configured to have: Copilot and Claude plugins, marketplaces, MCP servers, and the applications and checks the setup guide asks for.",
            Status: AppFeatureStatus.Dev),
        new(
            SessionFeatures.Sessions,
            "Sessions",
            "Open the full-screen list of Claude and Copilot sessions this PC has a record of, grouped by environment or by assistant.",
            Status: AppFeatureStatus.Dev),
        new(DashboardFeatures.Dashboard, "Dashboard", "Open the full-screen dashboard of your productivity and what your assistants cost.", Status: AppFeatureStatus.Dev),

        // --- Cross-cutting: something the whole product uses -----------------
        new(
            TasksFeatures.AdditionalRepositories,
            "Additional repositories",
            "Configure multiple repositories and switch repository-specific knowledge.",
            Group: AppFeatureGroup.CrossCutting),
        new(
            TasksFeatures.GitHubIntegration,
            "GitHub integration",
            "Configure GitHub access, push entries to issues, and refresh issue or pull request state.",
            Status: AppFeatureStatus.Dev,
            Group: AppFeatureGroup.CrossCutting),
        new(
            FeedbackReporting,
            "Feedback reporting",
            "Report Desktop app issues to GitHub with a title, details, and an attached screenshot.",
            Group: AppFeatureGroup.CrossCutting),
        new(
            AppFeatureKeys.CopilotCli,
            "Copilot CLI",
            "Start GitHub Copilot CLI from Backlog workflows.",
            Status: AppFeatureStatus.Dev,
            Group: AppFeatureGroup.CrossCutting),
        new(
            AiAssistant,
            "AI assistant",
            "Ask questions about visible task content through Azure Foundry.",
            Status: AppFeatureStatus.Dev,
            Group: AppFeatureGroup.CrossCutting),
        new(
            UsageMetrics,
            "AI usage metrics",
            "Read Claude and GitHub Copilot usage from their organization APIs as evidence for productivity metrics. Both are organization-scoped: Claude needs an API key an organization admin can use, and GitHub needs organization-owner access.",
            EnabledByDefault: false,
            Status: AppFeatureStatus.Dev,
            Group: AppFeatureGroup.CrossCutting)
    ];

    /// <summary>The catalog split into the sections the settings screen draws,
    /// in <see cref="All"/>'s order.
    ///
    /// <para>Derived rather than declared, so a feature cannot be listed in a
    /// section and left out of the catalog — or listed twice. The headings are
    /// product copy and belong with the rest of it here; the enum they key off
    /// is in the kernel with the record.</para></summary>
    public static IReadOnlyList<AppFeatureSection> Sections { get; } =
    [
        new(
            AppFeatureGroup.Domain,
            "Product areas",
            "Parts of the product with a screen of their own.",
            [.. All.Where(feature => feature.Group == AppFeatureGroup.Domain)]),
        new(
            AppFeatureGroup.CrossCutting,
            "Cross-cutting",
            "Integrations and assistants the whole product reaches through.",
            [.. All.Where(feature => feature.Group == AppFeatureGroup.CrossCutting)])
    ];
}

/// <summary>One heading on the settings screen and the switches under it.</summary>
public sealed record AppFeatureSection(
    AppFeatureGroup Group,
    string Title,
    string Description,
    IReadOnlyList<AppFeatureDefinition> Features);
