using Backlog.Modules.Backlog.Abstractions;
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

    /// <summary>The catalog, in the order the settings screen lists it.</summary>
    public static IReadOnlyList<AppFeatureDefinition> All { get; } =
    [
        new(BacklogFeatures.Backlog, "Backlog", "Create, edit, filter, reorder, and store backlog entries.", AlwaysEnabled: true),
        new(InboxPane, "Inbox pane", "Show the Inbox option and pane in the Home shell.", EnabledByDefault: false),
        new(RoadmapFeatures.Roadmap, "Roadmap band", "Show the roadmap band above the panes in the Home shell."),
        new(KnowledgeFeatures.KnowledgeSections, "Knowledge sections", "Show design, architecture, domain, technology, and instruction sections in the knowledge pane and header."),
        new(KnowledgeFeatures.RepositoryKnowledge, "Repository knowledge", "Show the side pane for repository knowledge."),
        new(BacklogFeatures.AdditionalRepositories, "Additional repositories", "Configure multiple repositories and switch repository-specific knowledge."),
        new(DevPcFeatures.SystemTools, "System tools", "Check, update, enable, and disable configured Copilot plugins, repository tools, and MCP servers."),
        new(SessionFeatures.Sessions, "Sessions", "Open the full-screen list of Claude and Copilot sessions this PC has a record of, grouped by environment or by assistant."),
        new(DashboardFeatures.Dashboard, "Dashboard", "Open the full-screen dashboard of your productivity and what your assistants cost."),
        new(BacklogFeatures.GitHubIntegration, "GitHub integration", "Configure GitHub access, push entries to issues, and refresh issue or pull request state."),
        new(FeedbackReporting, "Feedback reporting", "Report Desktop app issues to GitHub with current-screen context and a screenshot."),
        new(AppFeatureKeys.CopilotCli, "Copilot CLI", "Start GitHub Copilot CLI from Backlog workflows."),
        new(AiAssistant, "AI assistant", "Ask questions about visible backlog content through Azure Foundry."),
        new(
            UsageMetrics,
            "AI usage metrics",
            "Read Claude and GitHub Copilot usage from their organization APIs as evidence for productivity metrics. Both are organization-scoped: Claude needs an Admin API key and GitHub needs organization-owner access.",
            EnabledByDefault: false)
    ];
}
