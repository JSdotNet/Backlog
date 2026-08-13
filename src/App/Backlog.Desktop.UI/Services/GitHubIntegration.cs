using Backlog.Modules.Backlog;
using Backlog.Modules.Backlog.DomainModels;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.Services;

/// <summary>
/// Joins a backlog entry to a GitHub repository — the one place that knows an
/// entry's <c>`@area`</c> can name a configured repository, and that a pushed
/// entry keeps a <see cref="ProjectionRef"/> pointing at the issue it became.
/// <para>
/// Deliberately a cross-cutting service rather than something the list owns:
/// pushing and monitoring are wanted from wherever entries are shown, not just
/// the desktop quick-edit list.
/// </para>
/// </summary>
public sealed class GitHubIntegration(GitHubSettingsStore settings, IGitHubClient client, IGitHubConnectionProbe probe)
{
    private const string FeedbackOwner = "JSdotNet";
    private const string FeedbackRepository = "Backlog";

    /// <summary>The <c>target_type</c> recorded on the projection an entry keeps
    /// after it has been pushed.</summary>
    public const string IssueTargetType = "issue";

    public GitHubSettingsStore Settings => settings;

    /// <summary>True once at least one repository is configured. Nothing about
    /// GitHub is shown on an entry before that — the feature stays invisible
    /// until it has been asked for.</summary>
    public bool IsConfigured => settings.Current.Repositories.Count > 0;

    public IReadOnlyList<GitHubRepositoryRef> Repositories => settings.Current.Repositories;

    /// <summary>The repository an entry's area names, or null when the area is
    /// blank or not assigned to a configured repository.</summary>
    public GitHubRepositoryRef? ResolveRepository(string? area) => settings.Current.Find(area);

    /// <summary>Re-checks how the app can reach GitHub.</summary>
    public Task<GitHubConnection> DescribeConnectionAsync(CancellationToken cancellationToken = default)
    {
        probe.Invalidate();
        return probe.DescribeAsync(cancellationToken);
    }

    /// <summary>The issue an entry was already pushed to, or null.</summary>
    public static GitHubIssueLink? FindLink(BacklogEntry entry)
    {
        var projection = entry.ProjectionRefs.LastOrDefault(
            p => string.Equals(p.TargetType, IssueTargetType, StringComparison.OrdinalIgnoreCase));

        if (projection is null) return null;
        if (!int.TryParse(projection.ExternalId, out var number)) return null;

        var parts = projection.RepoId.Split('/', 2);
        if (parts.Length != 2) return null;

        return new GitHubIssueLink(projection.RepoId, number);
    }

    /// <summary>Creates the issue for an entry and records the link on the
    /// aggregate. Saving the entry afterwards is the caller's job, so the push
    /// and the write stay one transaction from the list's point of view.</summary>
    public async Task<GitHubIssueLink> PushAsync(
        BacklogEntry entry,
        GitHubRepositoryRef repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(repository);

        if (FindLink(entry) is { } existing)
        {
            throw new GitHubException($"This entry is already {existing.RepoFullName}#{existing.IssueNumber}.");
        }

        var issue = await client.CreateIssueAsync(
            repository,
            entry.Title,
            entry.ContentMd,
            entry.Tags,
            cancellationToken);

        entry.SetRepoIds([repository.FullName]);
        entry.AddProjectionRef(new ProjectionRef(
            repository.FullName,
            issue.Number.ToString(),
            IssueTargetType));

        return new GitHubIssueLink(repository.FullName, issue.Number);
    }

    internal async Task<GitHubIssueLink> PushSubItemAsync(
        BacklogEntry parent,
        EntryTextParser.ParsedSubItem subItem,
        GitHubRepositoryRef repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(subItem);
        ArgumentNullException.ThrowIfNull(repository);

        var issue = await client.CreateIssueAsync(
            repository,
            subItem.Title,
            BuildSubItemBody(parent, subItem),
            subItem.MetadataTags,
            cancellationToken);

        return new GitHubIssueLink(repository.FullName, issue.Number);
    }

    private static string BuildSubItemBody(BacklogEntry parent, EntryTextParser.ParsedSubItem subItem) =>
        $"""
        From backlog entry: {parent.Title}

        {subItem.Notes}
        """.Trim();

    /// <summary>Creates an issue in this repository from an in-app feedback report.</summary>
    public async Task<GitHubIssueLink> ReportFeedbackAsync(
        string title,
        string? details,
        string? screenArea,
        GitHubFeedbackScreenshot? screenshot,
        string? screenshotError = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new GitHubException("A feedback report needs a short title.");
        }

        var repository = FeedbackRepositoryRef();
        var issue = await client.CreateIssueAsync(
            repository,
            BuildFeedbackTitle(title),
            BuildFeedbackBody(details, screenArea, screenshot, screenshotError),
            cancellationToken: cancellationToken);

        return new GitHubIssueLink(repository.FullName, issue.Number);
    }

    internal static string BuildFeedbackTitle(string title) => $"[Feedback][Desktop app] {title.Trim()}";

    private GitHubRepositoryRef FeedbackRepositoryRef() =>
        settings.Current.Repositories.FirstOrDefault(r =>
            string.Equals(r.Owner, FeedbackOwner, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Name, FeedbackRepository, StringComparison.OrdinalIgnoreCase))
        ?? new GitHubRepositoryRef("backlog", FeedbackOwner, FeedbackRepository);

    internal static string BuildFeedbackBody(string? details, string? screenArea, GitHubFeedbackScreenshot? screenshot, string? screenshotError) =>
        $"""
        ## Desktop app screen area

        {(string.IsNullOrWhiteSpace(screenArea) ? "Unspecified" : screenArea.Trim())}

        ## Report

        {(string.IsNullOrWhiteSpace(details) ? "_No details provided._" : details.Trim())}

        ## Screenshot

        {BuildScreenshotSection(screenshot, screenshotError)}
        """;

    private static string BuildScreenshotSection(GitHubFeedbackScreenshot? screenshot, string? screenshotError)
    {
        if (screenshot is null)
        {
            return string.IsNullOrWhiteSpace(screenshotError)
                ? "No screenshot was captured."
                : $"Screenshot capture failed: {screenshotError.Trim()}";
        }

        return $"""
        Captured from the app as {screenshot.MediaType}, {screenshot.Width} x {screenshot.Height}, {screenshot.SizeBytes} bytes.

        ![Screenshot]({screenshot.DataUrl})
        """;
    }

    /// <summary>Reads the current state of a pushed entry's issue and the pull
    /// requests that reference it.</summary>
    public Task<GitHubIssueSnapshot> RefreshAsync(
        GitHubIssueLink link,
        CancellationToken cancellationToken = default)
    {
        var parts = link.RepoFullName.Split('/', 2);
        if (parts.Length != 2)
        {
            throw new GitHubException($"'{link.RepoFullName}' is not an owner/repo pair.");
        }

        // Monitoring must keep working for an entry pushed to a repository that
        // has since been removed from Settings — the link itself already says
        // everything the call needs.
        var repository = settings.Current.Repositories
                             .FirstOrDefault(r => string.Equals(r.FullName, link.RepoFullName, StringComparison.OrdinalIgnoreCase))
                         ?? new GitHubRepositoryRef(GitHubRepositoryRef.NormalizeAlias(parts[1]), parts[0], parts[1]);

        return client.GetIssueAsync(repository, link.IssueNumber, cancellationToken);
    }
}

/// <summary>The issue an entry became: which repository, and which number.</summary>
public sealed record GitHubIssueLink(string RepoFullName, int IssueNumber)
{
    public string Url => $"https://github.com/{RepoFullName}/issues/{IssueNumber}";

    public string Label => $"#{IssueNumber}";
}
