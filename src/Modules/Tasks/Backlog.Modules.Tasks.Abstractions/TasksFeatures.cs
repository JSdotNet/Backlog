namespace Backlog.Modules.Tasks.Abstractions;

/// <summary>
/// The feature keys the Tasks context owns.
/// <para>
/// A feature key belongs with whatever the feature is a feature <em>of</em>, not
/// with the settings screen that happens to list it: the pane that hides a
/// button when GitHub integration is off and the screen that offers the switch
/// are both naming the same thing, and only one of them is this context. The
/// display copy — what the switch is called and how it is described — stays with
/// the screen, because that is product wording rather than contract.
/// </para>
/// <para>
/// <see cref="GitHubIntegration"/> is the feature key, not the adapter class of
/// the same name in <c>Backlog.Infrastructure.GitHub</c>. They read differently
/// at every call site: this one is always <c>TasksFeatures.GitHubIntegration</c>
/// and is a string.
/// </para>
/// </summary>
public static class TasksFeatures
{
    /// <summary>Create, edit, filter, reorder, and store tasks. Always
    /// on — the app without it is not this app.
    /// <para>
    /// The value stays <c>"backlog"</c> even though this context is now Tasks.
    /// It is a persisted key: it is written into <c>features.json</c> under the
    /// user's app-data folder as a member of the disabled/enabled feature sets,
    /// so changing it would orphan whatever the user had already toggled. The
    /// constant is the name this codebase reads; the string is a stored value,
    /// and only one of the two is ours to rename.
    /// </para>
    /// </summary>
    public const string Tasks = "backlog";

    /// <summary>Push a task to a GitHub issue and read its state back.</summary>
    public const string GitHubIntegration = "github-integration";

    /// <summary>Configure more than one repository and switch the pane between
    /// them.</summary>
    public const string AdditionalRepositories = "additional-repositories";
}
