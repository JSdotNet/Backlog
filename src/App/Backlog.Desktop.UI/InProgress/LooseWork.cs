using Backlog.UI.Components.Integrations;

namespace Backlog.Desktop.UI.InProgress;

/// <summary>
/// A running session or an open pull request no task links: what a task's "Link a
/// session or pull request…" offers. Exactly one of the two is set.
/// </summary>
/// <param name="Repository">Where it lives, as the host names a repository, or null.</param>
/// <param name="Colour">The repository identity hue, or null.</param>
public sealed record LooseWork(
    WorkSession? Session = null,
    WorkPullRequest? PullRequest = null,
    string? Repository = null,
    int? Colour = null)
{
    /// <summary>The host's key for the item: the session id, or the pull request key.</summary>
    public string Key => Session?.Id ?? PullRequest?.Key ?? string.Empty;
}
