namespace Backlog.Tests;

internal sealed partial class TasksPaneHost
{
    /// <summary>The features the Tasks pane asks for, on by default as the app's
    /// catalog has them; this project composes none of the shell surfaces that ask
    /// for the rest.</summary>
    private static partial IReadOnlyList<AppFeatureDefinition> FeatureCatalog { get => field; } =
    [
        new(
            Backlog.Modules.Sessions.Abstractions.SessionFeatures.SessionRequests,
            "Session requests",
            "Start a Claude session on a prompt entry or a plan with one click.",
            EnabledByDefault: false,
            Status: AppFeatureStatus.Dev),
        new(
            TasksFeatures.GitHubIntegration,
            "GitHub integration",
            "Configure GitHub access, push entries to issues, and refresh issue or pull request state.",
            Status: AppFeatureStatus.Dev,
            Group: AppFeatureGroup.CrossCutting),
        new(
            AppFeatureKeys.CopilotCli,
            "Copilot CLI",
            "Start GitHub Copilot CLI from Backlog workflows.",
            Status: AppFeatureStatus.Dev,
            Group: AppFeatureGroup.CrossCutting),
    ];
}
