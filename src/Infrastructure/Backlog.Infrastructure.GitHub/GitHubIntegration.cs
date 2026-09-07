namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// The app's connection to GitHub: which repositories are configured, how it can
/// be reached, and how to file or re-read an issue.
/// <para>
/// It reads as a port and it very nearly was one — but every type in its
/// signature is one of this adapter's own (<see cref="GitHubSettingsStore"/>,
/// <see cref="GitHubRepositoryRef"/>, <see cref="GitHubConnection"/>,
/// <see cref="GitHubIssueSnapshot"/>), so an interface declared in a module's
/// abstractions could not be written without that module referencing this
/// project, which <c>ModuleBoundaryTests.A_module_never_references_infrastructure</c>
/// forbids and rightly. It is a thin composition over the client, the settings
/// store and the connection probe, all of which already live here, so here is
/// where it belongs: a screen may take an adapter, and both screens that take
/// this one already reference this project.
/// </para>
/// <para>
/// What makes a good issue out of a backlog entry or a bug report is the asking
/// context's business, so this takes a title and a body and files it. Backlog
/// Management's version of that question lives in its own <c>TasksIssues</c>;
/// the Shell's lives in its <c>FeedbackReporter</c>.
/// </para>
/// </summary>
public sealed class GitHubIntegration(
    GitHubSettingsStore settings,
    IGitHubClient client,
    IGitHubConnectionProbe probe,
    IGhCliAccountSource? cliAccounts = null)
{
    public GitHubSettingsStore Settings => settings;

    /// <summary>True once at least one repository is configured. Nothing about
    /// GitHub is shown on an entry before that — the feature stays invisible
    /// until it has been asked for.</summary>
    public bool IsConfigured => settings.Current.Repositories.Count > 0;

    public IReadOnlyList<GitHubRepositoryRef> Repositories => settings.Current.Repositories;

    /// <summary>The repository an entry's area names, or null when the area is
    /// blank or not assigned to a configured repository.</summary>
    public GitHubRepositoryRef? ResolveRepository(string? repoId) => settings.Current.Find(repoId);

    /// <summary>Re-checks how the app can reach GitHub.</summary>
    public Task<GitHubConnection> DescribeConnectionAsync(CancellationToken cancellationToken = default)
    {
        probe.Invalidate();
        return probe.DescribeAsync(cancellationToken);
    }

    /// <summary>
    /// Every login the <c>gh</c> CLI is signed in to on this machine.
    /// <para>
    /// Here rather than injected into Settings directly, for the reason this type
    /// exists at all: a screen takes one adapter for GitHub rather than four, and
    /// "which identities can this machine speak as" is the same question
    /// <see cref="DescribeConnectionAsync"/> answers, asked as a list instead of as
    /// a sentence. It is what lets the Accounts panel offer the accounts <c>gh</c>
    /// already holds rather than making somebody paste a token per identity.
    /// </para>
    /// <para>
    /// The source is optional and an absent one answers with an empty list rather
    /// than throwing. A host that registered none is a host with no CLI to ask,
    /// which degrades the picker to manual entry in exactly the way a machine
    /// without <c>gh</c> already does.
    /// </para>
    /// </summary>
    public Task<IReadOnlyList<GhCliAccount>> ListCliAccountsAsync(CancellationToken cancellationToken = default) =>
        cliAccounts?.ListAsync(cancellationToken) ?? Task.FromResult<IReadOnlyList<GhCliAccount>>([]);

    /// <summary>Creates an issue from a title and body somebody else composed.
    /// What makes a good issue out of a backlog entry, a knowledge chapter or a
    /// bug report is that context's business — this only knows how to file
    /// one.</summary>
    public async Task<GitHubIssueLink> CreateIssueAsync(
        GitHubRepositoryRef repository,
        string title,
        string? body,
        IEnumerable<string>? labels = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var issue = await client.CreateIssueAsync(repository, title, body, labels, cancellationToken);
        return new GitHubIssueLink(repository.FullName, issue.Number);
    }

    /// <summary>Commits a file to a repository and hands back the raw URL it can
    /// be linked from. See <see cref="IGitHubClient.UploadFileAsync"/>.</summary>
    public Task<GitHubUploadedFile> UploadFileAsync(
        GitHubRepositoryRef repository,
        string path,
        string branch,
        byte[] content,
        string commitMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return client.UploadFileAsync(repository, path, branch, content, commitMessage, cancellationToken);
    }

    /// <summary>The configured repository matching <paramref name="owner"/> and
    /// <paramref name="name"/>, or an unconfigured reference to it. Filing an
    /// issue somewhere the app knows by name but has not been pointed at in
    /// Settings still has to work.</summary>
    public GitHubRepositoryRef RepositoryFor(string owner, string name) =>
        settings.Current.Repositories.FirstOrDefault(r =>
            string.Equals(r.Owner, owner, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? new GitHubRepositoryRef(GitHubRepositoryRef.NormalizeAlias(name), owner, name);

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
