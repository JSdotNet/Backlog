namespace Backlog.Modules.Repositories.Abstractions.Environments;

/// <summary>What a screen does to open an environment.</summary>
public enum LaunchActionKind
{
    /// <summary>Nothing can be launched on this device; <see cref="LaunchAction.Reason"/> says why.</summary>
    Unavailable,

    /// <summary>Open <see cref="LaunchAction.Target"/>, an absolute address, in the browser.</summary>
    OpenUri,

    /// <summary>Run <see cref="LaunchAction.Target"/>, a command line, in <see cref="LaunchAction.WorkingDirectory"/> when one is set.</summary>
    RunCommand,

    /// <summary>Open <see cref="LaunchAction.Target"/>, a folder, as a workspace.</summary>
    OpenWorkspace
}

/// <summary>The action <see cref="EnvironmentLaunch"/> turns a launch link into.</summary>
public sealed record LaunchAction(LaunchActionKind Kind, string? Target, string? WorkingDirectory = null, string? Reason = null)
{
    public static LaunchAction Unavailable(string reason) => new(LaunchActionKind.Unavailable, null, Reason: reason);
}

/// <summary>
/// The Environment Launch service of <c>domain/repositories</c>: turns an environment's
/// launch link into the action the screen offers — open a URL, run a command, focus a
/// workspace, or open a cloud dashboard — composed with the repository's clone directory
/// on this device. A pure rule, shipped with the surface for the reason
/// <see cref="PullRequestVerdict"/> is: every screen that offers a launch must offer the
/// same one.
/// </summary>
public static class EnvironmentLaunch
{
    /// <summary>Where an Azure resource id opens: the portal's resource blade.</summary>
    public const string AzurePortalResourceBase = "https://portal.azure.com/#resource";

    public const string NoCloneForWorkspace =
        "This repository has no clone directory on this device, so a workspace path relative to it can't be opened.";

    public const string UnknownResourceId =
        "Backlog opens an Azure resource id, one starting /subscriptions/, or an https address.";

    /// <param name="link">The environment's launch link.</param>
    /// <param name="cloneDirectory">The home repository's clone directory on this device,
    /// or null when it has none or the environment is in the no-repository group.</param>
    public static LaunchAction Resolve(LaunchLink link, string? cloneDirectory)
    {
        ArgumentNullException.ThrowIfNull(link);

        var value = link.Value.Trim();
        var clone = string.IsNullOrWhiteSpace(cloneDirectory) ? null : cloneDirectory.Trim();

        return link.Kind switch
        {
            LaunchLinkKind.Url => IsWebAddress(value)
                ? new LaunchAction(LaunchActionKind.OpenUri, value)
                : LaunchAction.Unavailable("Only an http or https address can be opened."),
            LaunchLinkKind.Command => new LaunchAction(LaunchActionKind.RunCommand, value, clone),
            LaunchLinkKind.WorkspacePath => Workspace(value, clone),
            LaunchLinkKind.ResourceId => Resource(value),
            _ => LaunchAction.Unavailable("This launch link can't be opened.")
        };
    }

    /// <summary>Whether <paramref name="value"/> is an absolute <c>http</c> or <c>https</c> address.</summary>
    public static bool IsWebAddress(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static LaunchAction Workspace(string path, string? clone)
    {
        if (Path.IsPathRooted(path) && !IsDriveRelative(path))
        {
            return new LaunchAction(LaunchActionKind.OpenWorkspace, Path.GetFullPath(path));
        }

        return clone is null
            ? LaunchAction.Unavailable(NoCloneForWorkspace)
            : new LaunchAction(LaunchActionKind.OpenWorkspace, Path.GetFullPath(Path.Combine(clone, path)));
    }

    // "\folder" is rooted but names no drive: read it as relative to the clone, not to
    // whatever drive the app happens to run from.
    private static bool IsDriveRelative(string path) =>
        Path.GetPathRoot(path) is { } root && (root == "\\" || root == "/") && OperatingSystem.IsWindows();

    private static LaunchAction Resource(string id)
    {
        if (id.StartsWith("/subscriptions/", StringComparison.OrdinalIgnoreCase))
        {
            return new LaunchAction(LaunchActionKind.OpenUri, AzurePortalResourceBase + id);
        }

        return IsWebAddress(id)
            ? new LaunchAction(LaunchActionKind.OpenUri, id)
            : LaunchAction.Unavailable(UnknownResourceId);
    }
}
