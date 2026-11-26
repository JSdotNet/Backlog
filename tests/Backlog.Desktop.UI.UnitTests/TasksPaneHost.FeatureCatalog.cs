namespace Backlog.Tests;

internal sealed partial class TasksPaneHost
{
    /// <summary>The app's whole catalog: the shell surfaces these tests render beside
    /// the pane — the Calendar among them — ask for features the pane never does.</summary>
    private static partial IReadOnlyList<AppFeatureDefinition> FeatureCatalog => AppFeatures.All;
}
