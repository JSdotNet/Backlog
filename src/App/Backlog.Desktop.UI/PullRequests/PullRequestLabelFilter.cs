namespace Backlog.Desktop.UI.PullRequests;

/// <summary>
/// A repository's pull request label filter, applied to one pull request:
/// <see cref="Backlog.Infrastructure.GitHub.GitHubRepositoryRef.PullRequestLabels"/>
/// against the labels the pull request carries.
/// <para>
/// Any of the filter's labels lets a pull request through, because a filter of
/// "frontend, design" is somebody naming the pull requests they review, not a set
/// every one must carry. Labels compare whole and without regard to case, the way
/// GitHub compares label names. No filter lets everything through, and a pinned pull
/// request is never filtered out: a pin is somebody saying "keep this one in front of
/// me", which no filter set before or after it overrides.
/// </para>
/// </summary>
internal static class PullRequestLabelFilter
{
    public static bool Passes(IReadOnlyCollection<string> labels, IReadOnlyCollection<string> filter, bool pinned)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(filter);

        if (pinned || filter.Count == 0) return true;

        return labels.Any(label => filter.Contains(label, StringComparer.OrdinalIgnoreCase));
    }
}
