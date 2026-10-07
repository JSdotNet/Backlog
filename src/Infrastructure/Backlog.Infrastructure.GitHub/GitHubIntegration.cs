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
public sealed partial class GitHubIntegration(
    GitHubSettingsStore settings,
    IGitHubClient client,
    IGitHubConnectionProbe probe,
    IGhCliAccountSource? cliAccounts = null,
    IGitHubAccountProbe? accountProbe = null,
    TimeProvider? time = null)
{
    /// <summary>How far back "Recently merged" reaches. Two weeks: long enough to find
    /// what landed before a weekend away or over a sprint, short enough that the view
    /// stays a list of recent work rather than a second copy of GitHub's history.</summary>
    public static readonly TimeSpan RecentlyMergedWindow = TimeSpan.FromDays(14);

    public GitHubSettingsStore Settings => settings;

    /// <summary>True once at least one repository is configured. Nothing about
    /// GitHub is shown on an entry before that — the feature stays invisible
    /// until it has been asked for.</summary>
    public bool IsConfigured => settings.Current.Repositories.Count > 0;

    public IReadOnlyList<GitHubRepositoryRef> Repositories => settings.Current.Repositories;

    /// <summary>The repository an entry's area names, or null when the area is
    /// blank or not assigned to a configured repository.</summary>
    public GitHubRepositoryRef? ResolveRepository(string? repoId) => settings.Current.Find(repoId);

    /// <summary>
    /// Asks GitHub whether one account's credential really authenticates as that
    /// account - the card's "Test this account". Optional the way the CLI source is:
    /// a host that registered no probe is answered with a sentence saying so rather
    /// than a throw, so the button degrades to a message instead of a fault.
    /// </summary>
    public Task<GitHubAccountCheck> CheckAccountAsync(GitHubAccount account, CancellationToken cancellationToken = default) =>
        accountProbe?.CheckAccountAsync(account, cancellationToken)
        ?? Task.FromResult(new GitHubAccountCheck(false, "This host cannot test a GitHub account."));

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

        var issue = await client.CreateIssueAsync(repository, title, body, labels, cancellationToken).ConfigureAwait(false);
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
        CancellationToken cancellationToken = default) =>
        client.GetIssueAsync(RepositoryForFullName(link.RepoFullName), link.IssueNumber, cancellationToken);

    /// <summary>Reads the current state of one pull request an entry's work
    /// recorded, named by <c>owner/repo</c> and number.</summary>
    public Task<GitHubPullRequest> ReadPullRequestAsync(
        string repoFullName,
        int number,
        CancellationToken cancellationToken = default) =>
        client.GetPullRequestAsync(RepositoryForFullName(repoFullName), number, cancellationToken);

    /// <summary>Reads one recorded pull request's state, check roll-up and
    /// auto-merge request. See <see cref="IGitHubClient.GetPullRequestStatusAsync"/>.</summary>
    public Task<GitHubPullRequestStatus> ReadPullRequestStatusAsync(
        string repoFullName,
        int number,
        CancellationToken cancellationToken = default) =>
        client.GetPullRequestStatusAsync(RepositoryForFullName(repoFullName), number, cancellationToken);

    /// <summary>
    /// Hands the pull request to GitHub's native auto-merge, with the method the
    /// repository allows. GitHub does the waiting: nothing here polls, and nothing
    /// here merges later on its own.
    /// </summary>
    /// <exception cref="GitHubException">GitHub refused, in a sentence naming the
    /// pull request and what would fix it where the refusal is a known one.</exception>
    public Task EnableAutoMergeAsync(GitHubPullRequestStatus pullRequest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);

        return Explained(
            MergeAct.EnableAutoMerge,
            pullRequest,
            () => client.EnableAutoMergeAsync(
                RepositoryForFullName(pullRequest.RepositoryFullName),
                pullRequest.NodeId,
                pullRequest.PreferredMergeMethod,
                cancellationToken));
    }

    /// <summary>Withdraws a pending auto-merge request.</summary>
    /// <exception cref="GitHubException">As <see cref="EnableAutoMergeAsync"/>.</exception>
    public Task DisableAutoMergeAsync(GitHubPullRequestStatus pullRequest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);

        return Explained(
            MergeAct.DisableAutoMerge,
            pullRequest,
            () => client.DisableAutoMergeAsync(
                RepositoryForFullName(pullRequest.RepositoryFullName),
                pullRequest.NodeId,
                cancellationToken));
    }

    /// <summary>Merges the pull request now — the act for one GitHub would refuse
    /// to queue because it is already mergeable.</summary>
    /// <exception cref="GitHubException">As <see cref="EnableAutoMergeAsync"/>.</exception>
    public Task MergePullRequestAsync(GitHubPullRequestStatus pullRequest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);

        return Explained(
            MergeAct.Merge,
            pullRequest,
            () => client.MergePullRequestAsync(
                RepositoryForFullName(pullRequest.RepositoryFullName),
                pullRequest.NodeId,
                pullRequest.PreferredMergeMethod,
                cancellationToken));
    }

    /// <summary>
    /// The open pull requests of every repository given, read side by side, most
    /// recently updated first across all of them.
    /// <para>
    /// Concurrently, because each repository is its own GraphQL round trip and the
    /// list waits on the slowest of them either way. And each one's refusal is caught
    /// and kept as that repository's failure rather than thrown: a repository the
    /// account cannot read says nothing about the ones it can, and the list is worth
    /// having without it. Cancellation is not a refusal and still ends the read.
    /// </para>
    /// </summary>
    public async Task<GitHubPullRequestListing> ListOpenPullRequestsAsync(
        IEnumerable<GitHubRepositoryRef> repositories,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repositories);

        var reads = repositories
            .DistinctBy(repository => repository.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(repository => ReadOneAsync(repository, cancellationToken))
            .ToList();

        var answers = await Task.WhenAll(reads).ConfigureAwait(false);

        return new GitHubPullRequestListing(
            [.. answers.SelectMany(answer => answer.PullRequests).OrderByDescending(pull => pull.UpdatedAt)],
            [.. answers.Where(answer => answer.Failure is not null).Select(answer => answer.Failure!)]);
    }

    private async Task<(IReadOnlyList<GitHubOpenPullRequest> PullRequests, GitHubRepositoryFailure? Failure)> ReadOneAsync(
        GitHubRepositoryRef repository,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await client.ListOpenPullRequestsAsync(repository, cancellationToken).ConfigureAwait(false), null);
        }
        catch (Exception ex) when (ex is GitHubException or GitHubNotConfiguredException or HttpRequestException)
        {
            return ([], new GitHubRepositoryFailure(repository.FullName, ex.Message));
        }
    }

    /// <summary>
    /// The pinned pull requests, whatever state each is in, most recently updated
    /// first — one query per repository that has pins, read side by side, each
    /// repository's refusal kept as its failure, exactly as
    /// <see cref="ListOpenPullRequestsAsync"/> reads.
    /// <para>
    /// A pin names its repository by <c>owner/name</c> and is read through it whether
    /// or not that repository is still configured or in scope: a pin is somebody
    /// saying "keep this one in front of me", and that outlives the filter it was
    /// made under. Pins differing only in the case of their repository are one
    /// repository's.
    /// </para>
    /// </summary>
    public async Task<GitHubPullRequestListing> ListPinnedPullRequestsAsync(
        IEnumerable<PullRequestPin> pins,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pins);

        var reads = pins
            .Where(pin => pin.Number > 0 && !string.IsNullOrWhiteSpace(pin.RepositoryFullName))
            .GroupBy(pin => pin.RepositoryFullName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => ReadPinnedAsync(group.Key, [.. group.Select(pin => pin.Number).Distinct()], cancellationToken))
            .ToList();

        if (reads.Count == 0) return GitHubPullRequestListing.Empty;

        var answers = await Task.WhenAll(reads).ConfigureAwait(false);

        return new GitHubPullRequestListing(
            [.. answers.SelectMany(answer => answer.PullRequests).OrderByDescending(pull => pull.UpdatedAt)],
            [.. answers.Where(answer => answer.Failure is not null).Select(answer => answer.Failure!)]);
    }

    private async Task<(IReadOnlyList<GitHubOpenPullRequest> PullRequests, GitHubRepositoryFailure? Failure)> ReadPinnedAsync(
        string repositoryFullName,
        IReadOnlyCollection<int> numbers,
        CancellationToken cancellationToken)
    {
        try
        {
            var repository = RepositoryForFullName(repositoryFullName);
            return (await client.ListPullRequestsAsync(repository, numbers, cancellationToken).ConfigureAwait(false), null);
        }
        catch (Exception ex) when (ex is GitHubException or GitHubNotConfiguredException or HttpRequestException)
        {
            return ([], new GitHubRepositoryFailure(repositoryFullName, ex.Message));
        }
    }

    /// <summary>
    /// The pull requests merged within <see cref="RecentlyMergedWindow"/> in every
    /// repository given, newest merge first across all of them — read side by side,
    /// each repository's refusal kept as its failure, exactly as
    /// <see cref="ListOpenPullRequestsAsync"/> reads.
    /// <para>
    /// The window's start is taken here, from this adapter's clock, and handed to the
    /// client, which pages back until it has passed it. A repository whose read stopped
    /// at the client's page cap inside the window is named in
    /// <see cref="GitHubMergedPullRequestListing.Truncated"/>.
    /// </para>
    /// </summary>
    public async Task<GitHubMergedPullRequestListing> ListMergedPullRequestsAsync(
        IEnumerable<GitHubRepositoryRef> repositories,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repositories);

        var since = (time ?? TimeProvider.System).GetUtcNow() - RecentlyMergedWindow;

        var reads = repositories
            .DistinctBy(repository => repository.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(repository => ReadMergedAsync(repository, since, cancellationToken))
            .ToList();

        var answers = await Task.WhenAll(reads).ConfigureAwait(false);

        return new GitHubMergedPullRequestListing(
            [.. answers.SelectMany(answer => answer.Read.PullRequests).OrderByDescending(pull => pull.MergedAt)],
            [.. answers.Where(answer => answer.Failure is not null).Select(answer => answer.Failure!)],
            [.. answers.Where(answer => answer.Read.Truncated).Select(answer => answer.Repository)]);
    }

    private async Task<(string Repository, GitHubMergedPullRequestRead Read, GitHubRepositoryFailure? Failure)> ReadMergedAsync(
        GitHubRepositoryRef repository,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        try
        {
            return (repository.FullName, await client.ListMergedPullRequestsAsync(repository, since, cancellationToken).ConfigureAwait(false), null);
        }
        catch (Exception ex) when (ex is GitHubException or GitHubNotConfiguredException or HttpRequestException)
        {
            return (repository.FullName, new GitHubMergedPullRequestRead([], Truncated: false), new GitHubRepositoryFailure(repository.FullName, ex.Message));
        }
    }

    /// <summary>
    /// Brings a pull request's branch up to date with its base — GitHub's "Update
    /// branch". The head the list read travels with it, so a branch that moved since
    /// is refused rather than merged into.
    /// </summary>
    /// <exception cref="GitHubException">As <see cref="EnableAutoMergeAsync"/>.</exception>
    public Task UpdateBranchAsync(GitHubOpenPullRequest pullRequest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);

        return Explained(
            MergeAct.UpdateBranch,
            pullRequest.ToStatus(),
            () => client.UpdateBranchAsync(
                RepositoryForFullName(pullRequest.RepositoryFullName),
                pullRequest.Number,
                pullRequest.HeadSha,
                cancellationToken));
    }

    /// <summary>Takes a draft out of draft, so its reviewers are asked and it can
    /// merge.</summary>
    /// <exception cref="GitHubException">As <see cref="EnableAutoMergeAsync"/>.</exception>
    public Task MarkReadyForReviewAsync(GitHubOpenPullRequest pullRequest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);

        return Explained(
            MergeAct.MarkReady,
            pullRequest.ToStatus(),
            () => client.MarkReadyForReviewAsync(
                RepositoryForFullName(pullRequest.RepositoryFullName),
                pullRequest.NodeId,
                cancellationToken));
    }

    /// <summary>
    /// Runs the failed jobs again of every GitHub Actions workflow run behind one of the
    /// head commit's failed checks — GitHub's "Re-run failed jobs", once per run however
    /// many of its checks failed. A failed check another service set is left alone:
    /// GitHub cannot run it again.
    /// <para>
    /// Every run is asked, one after the other, even when one is refused: the runs are
    /// independent, and a run already re-running says nothing about the next. The first
    /// refusal is then thrown, worded, and says how many of the others went through.
    /// </para>
    /// </summary>
    /// <exception cref="GitHubException">No failed check is a GitHub Actions run, or
    /// GitHub refused a run; as <see cref="EnableAutoMergeAsync"/>.</exception>
    public async Task RerunFailedChecksAsync(GitHubOpenPullRequest pullRequest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);

        var runs = pullRequest.FailedWorkflowRunIds;
        if (runs.Count == 0)
        {
            throw new GitHubException(
                $"Couldn't re-run the failed checks of {pullRequest.RepositoryFullName}#{pullRequest.Number}: "
                + "none of them is a GitHub Actions run. Re-run them where they ran.");
        }

        var repository = RepositoryForFullName(pullRequest.RepositoryFullName);
        GitHubException? firstRefusal = null;
        var refused = 0;

        foreach (var run in runs)
        {
            try
            {
                await Explained(
                    MergeAct.RerunFailed,
                    pullRequest.ToStatus(),
                    () => client.RerunFailedJobsAsync(repository, run, cancellationToken)).ConfigureAwait(false);
            }
            catch (GitHubException ex)
            {
                refused++;
                firstRefusal ??= ex;
            }
        }

        if (firstRefusal is null) return;

        var started = runs.Count - refused;
        if (started == 0) throw firstRefusal;

        throw new GitHubException(
            $"{firstRefusal.Message} {started} of the {runs.Count} workflow runs did start again.", firstRefusal)
        {
            Status = firstRefusal.Status,
            ErrorType = firstRefusal.ErrorType
        };
    }

    /// <summary>The acts on a pull request whose refusals are put into words here.
    /// The name is older than the last three members: every act it held was a merge's
    /// until the pull requests list added acts that prepare one.</summary>
    private enum MergeAct
    {
        EnableAutoMerge,
        DisableAutoMerge,
        Merge,
        UpdateBranch,
        MarkReady,
        RerunFailed
    }

    /// <summary>
    /// Runs one merge act and, when GitHub refuses it, says so in words a person can
    /// act on.
    /// <para>
    /// Here rather than in the client, because this is the one place that knows both
    /// which pull request was asked about and which act was attempted — the two
    /// things GitHub's refusals leave out. "Pull request Auto merge is not allowed for
    /// this repository" names neither the pull request nor the switch that fixes it.
    /// </para>
    /// <para>
    /// Matched on GitHub's words, and on the GraphQL error type where there is one,
    /// because that is all a refusal carries: the CLI reports GraphQL errors on its
    /// error line and exits non-zero, the token transport reads them out of the
    /// <c>errors</c> array, and both arrive here as the same exception with the same
    /// sentence in it. A refusal not recognised keeps GitHub's own sentence behind the
    /// act and the pull request rather than being paraphrased into something vaguer.
    /// </para>
    /// </summary>
    private static async Task Explained(MergeAct act, GitHubPullRequestStatus pullRequest, Func<Task> send)
    {
        try
        {
            await send().ConfigureAwait(false);
        }
        catch (GitHubException ex)
        {
            throw new GitHubException(DescribeRefusal(act, pullRequest, ex), ex)
            {
                Status = ex.Status,
                ErrorType = ex.ErrorType
            };
        }
    }

    private static string DescribeRefusal(MergeAct act, GitHubPullRequestStatus pullRequest, GitHubException refusal)
    {
        var subject = $"{pullRequest.RepositoryFullName}#{pullRequest.Number}";
        var what = act switch
        {
            MergeAct.EnableAutoMerge => $"Couldn't turn on auto-merge for {subject}",
            MergeAct.DisableAutoMerge => $"Couldn't cancel auto-merge for {subject}",
            MergeAct.UpdateBranch => $"Couldn't update the branch of {subject}",
            MergeAct.MarkReady => $"Couldn't mark {subject} ready for review",
            MergeAct.RerunFailed => $"Couldn't re-run the failed checks of {subject}",
            _ => $"Couldn't merge {subject}"
        };

        var said = refusal.Message;

        bool Says(string phrase) => said.Contains(phrase, StringComparison.OrdinalIgnoreCase);

        // The head the list read is the one the update was asked against; GitHub
        // names the parameter when the branch has moved on since.
        if (act is MergeAct.UpdateBranch && Says("expected head sha"))
        {
            return $"{what}: the branch has moved since it was read. Refresh the list and try again.";
        }

        if (act is MergeAct.UpdateBranch && Says("merge conflict"))
        {
            return $"{what}: the base branch conflicts with it, and GitHub can't merge that by itself. "
                + "Resolve the conflicts on the branch, then push.";
        }

        // GitHub refuses to re-run a workflow run that has not finished, with a 403
        // that would otherwise read as a missing permission below.
        if (act is MergeAct.RerunFailed && (Says("already running") || Says("cannot be rerun")))
        {
            return $"{what}: a workflow run is still going or can't be run again. "
                + "Wait for it to finish, then refresh and try again.";
        }

        // And one too old to run again, also with a 403.
        if (act is MergeAct.RerunFailed && (Says("unable to retry") || Says("over 30 days")))
        {
            return $"{what}: GitHub runs a workflow again only within 30 days of its start. "
                + "Push a commit to the branch to run the checks afresh.";
        }

        if (Says("auto merge is not allowed") || Says("auto-merge is not allowed"))
        {
            return $"{what}: auto-merge is off for {pullRequest.RepositoryFullName}. "
                + "Turn on \"Allow auto-merge\" in the repository's settings (General, Pull Requests), then try again.";
        }

        // The three merge states GitHub refuses to queue auto-merge in, because the
        // pull request can merge right now. See GitHubClient.MergeableNow.
        if (Says("clean status") || Says("unstable status") || Says("has_hooks status"))
        {
            return $"{what}: it can already merge, so GitHub won't queue it. "
                + $"Choose \"Merge #{pullRequest.Number} now\" instead.";
        }

        if (Says("merge queue"))
        {
            return $"{what}: #{pullRequest.Number} must go through the merge queue — "
                + $"{pullRequest.RepositoryFullName} merges through one, so add it to the queue on GitHub.";
        }

        if (Says("protected branch rules not configured") || Says("branch protection"))
        {
            return $"{what}: auto-merge needs branch protection on the base branch with required status checks. "
                + "Add a branch protection rule or ruleset that requires checks, then try again.";
        }

        // Narrow on purpose. GitHub says "permission" in sentences about other
        // things too, and telling somebody they lack write access when they do not
        // sends them to the wrong settings page — so only the refusal's type or
        // status, or the two phrasings GitHub uses for a missing grant, count.
        if (string.Equals(refusal.ErrorType, "FORBIDDEN", StringComparison.Ordinal)
            || refusal.Status is System.Net.HttpStatusCode.Forbidden
            || Says("resource not accessible")
            || MustHavePermission().IsMatch(said))
        {
            var permission = act switch
            {
                MergeAct.UpdateBranch => "push to branches",
                MergeAct.MarkReady => "change pull requests",
                MergeAct.RerunFailed => "re-run workflows",
                _ => "merge"
            };

            return $"{what}: the GitHub account in use doesn't have permission to {permission} in "
                + $"{pullRequest.RepositoryFullName} — it needs write access to the repository.";
        }

        if (Says("not mergeable"))
        {
            return $"{what}: GitHub says it isn't mergeable. "
                + "Check it for conflicts, required reviews or failing required checks.";
        }

        return $"{what}: {said}";
    }

    /// <summary>"must have write permission", "must have admin permissions to …" —
    /// GitHub's wording for a grant the account lacks.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"must have\b.*\bpermission", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex MustHavePermission();

    private GitHubRepositoryRef RepositoryForFullName(string repoFullName)
    {
        var parts = repoFullName.Split('/', 2);
        if (parts.Length != 2)
        {
            throw new GitHubException($"'{repoFullName}' is not an owner/repo pair.");
        }

        // Monitoring must keep working for an entry pushed to a repository that
        // has since been removed from Settings — the link itself already says
        // everything the call needs.
        return settings.Current.Repositories
                   .FirstOrDefault(r => string.Equals(r.FullName, repoFullName, StringComparison.OrdinalIgnoreCase))
               ?? new GitHubRepositoryRef(GitHubRepositoryRef.NormalizeAlias(parts[1]), parts[0], parts[1]);
    }
}

/// <summary>The issue an entry became: which repository, and which number.</summary>
public sealed record GitHubIssueLink(string RepoFullName, int IssueNumber)
{
    public string Url => $"https://github.com/{RepoFullName}/issues/{IssueNumber}";

    public string Label => $"#{IssueNumber}";
}
