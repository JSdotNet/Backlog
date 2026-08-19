using Backlog.Modules.Backlog.Abstractions;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.Modules.Knowledge.Abstractions;
using Backlog.Modules.Monitoring.UI;
using Backlog.Modules.Roadmap.UI;
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
/// Roadmap and Monitoring are the two that read as exceptions and are not. Only
/// the Shell gates on their keys today, so by the rule above they could sit here
/// — but each declares its own in its <c>.UI</c> project, because that is where
/// the context itself currently lives. Neither has a domain module yet, so there
/// is no abstractions project to hold the key and no reason to create one for a
/// single constant; when the module arrives the key moves with it, and nothing
/// here has to be untangled first. See <c>RoadmapFeatures</c> and
/// <c>MonitoringFeatures</c>.
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
        new(MonitoringFeatures.Dashboard, "Dashboard", "Open the full-screen dashboard of progress across projects and repositories."),
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
