using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Every branch of a repository, what each changes in the devbook against the
/// default branch and how far ahead and behind it is, and whether the local clone
/// is current — loaded once per repository per session, in the background.
/// <para>
/// The Devbook pane starts the load on opening a repository, so the source picker
/// can list only the branches that change the devbook and say what each changes.
/// Nothing else starts it: reopening the picker, switching tabs or source, or a
/// second window asks for the same load and is handed what it answered, or the
/// load still running. <see cref="RefreshAsync"/> is the one way to ask again.
/// </para>
/// <para>
/// A repository with N branches costs a listing, the default branch's head, one
/// comparison per other branch — <see cref="ParallelCompares"/> at a time, the way
/// the review reads chapters — and one <c>git fetch</c> where there is a clone.
/// The comparisons go through <see cref="DevbookBranchComparisons"/>, so the
/// review of a branch compared here asks GitHub nothing more.
/// </para>
/// <para>
/// The load runs on no caller's token, so a pane that closes stops only its own
/// wait, and is bounded by <see cref="LoadTimeout"/>, which stops the comparisons
/// still out as well as the wait for them. A failure is an answer, not an
/// exception: a refused listing is a message, a refused comparison leaves that
/// branch uncompared, and a clone that could not be checked has no answer. A
/// failed load is kept like any other until the next session or a refresh, so an
/// offline machine does not ask again on every open; a refused refresh keeps the
/// branches the last good load listed, with the refusal beside them.
/// </para>
/// </summary>
public sealed class DevbookBranchOverview(
    GitHubSettingsStore repositories,
    IGitHubBranchCatalog branches,
    DevbookBranchComparisons comparisons,
    ILocalGitRepositoryService git,
    TimeSpan? loadTimeout = null)
{
    /// <summary>How many comparisons are asked of GitHub at once. Enough that a
    /// repository of forty branches is not forty round trips end to end; few
    /// enough that a large one does not queue a burst against the rate limit.</summary>
    public const int ParallelCompares = 4;

    /// <summary>How long one load may take by default before what has not
    /// answered is left out. A branch still waiting then reads as uncompared.</summary>
    public static readonly TimeSpan LoadTimeout = TimeSpan.FromMinutes(2);

    private readonly TimeSpan _loadTimeout = loadTimeout ?? LoadTimeout;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The overview, loaded the first time it is asked for and handed back from
    /// then on. A load already running is shared rather than started twice.
    /// </summary>
    /// <param name="cancellationToken">Stops this caller's wait, never the load.</param>
    public Task<DevbookBranchList> LoadAsync(string? repositoryAlias, CancellationToken cancellationToken = default) =>
        StartAsync(repositoryAlias, refresh: false, cancellationToken);

    /// <summary>Loads again, whatever is held — the explicit request. A load
    /// already running is the refresh.</summary>
    /// <param name="cancellationToken">Stops this caller's wait, never the load.</param>
    public Task<DevbookBranchList> RefreshAsync(string? repositoryAlias, CancellationToken cancellationToken = default) =>
        StartAsync(repositoryAlias, refresh: true, cancellationToken);

    /// <summary>The last overview that landed for this repository, or null while
    /// none has. A refresh in flight leaves the previous one here until it lands.</summary>
    public DevbookBranchList? Current(string? repositoryAlias)
    {
        if (Repository(repositoryAlias) is not { } repository) return null;

        lock (_gate) return _entries.TryGetValue(repository.Alias, out var entry) ? entry.Result : null;
    }

    /// <summary>Whether a load for this repository is running now.</summary>
    public bool IsLoading(string? repositoryAlias)
    {
        if (Repository(repositoryAlias) is not { } repository) return false;

        lock (_gate) return _entries.TryGetValue(repository.Alias, out var entry) && entry.Running is not null;
    }

    /// <summary>
    /// Replaces the clone's answer with a newer one — what a pull or a check left
    /// it at — and keeps every branch as it was: a pull moves the clone and
    /// nothing GitHub said about the branches. A load still running when this is
    /// recorded looked at the clone before it, so when that load lands this
    /// answer is kept over the load's own.
    /// </summary>
    public void RecordLocal(string? repositoryAlias, LocalGitRepositoryUpdateCheck? check)
    {
        if (check is null || Repository(repositoryAlias) is not { } repository) return;

        lock (_gate)
        {
            if (!_entries.TryGetValue(repository.Alias, out var entry)) return;

            entry.RecordedLocal = check;
            entry.LocalVersion++;
            if (entry.Result is { } result) entry.Result = result with { Local = check };
        }
    }

    /// <summary>
    /// How many loads have landed for this repository this session, zero before
    /// the first. A screen keying a control on the list rebuilds it when this
    /// moves, which a refresh that changes nothing else still does.
    /// </summary>
    public int Generation(string? repositoryAlias)
    {
        if (Repository(repositoryAlias) is not { } repository) return 0;

        lock (_gate) return _entries.TryGetValue(repository.Alias, out var entry) ? entry.Generation : 0;
    }

    private async Task<DevbookBranchList> StartAsync(string? repositoryAlias, bool refresh, CancellationToken cancellationToken)
    {
        if (Repository(repositoryAlias) is not { } repository) return DevbookBranchList.Empty;

        Task<DevbookBranchList> load;

        lock (_gate)
        {
            if (!_entries.TryGetValue(repository.Alias, out var entry)) _entries[repository.Alias] = entry = new Entry();

            if (entry.Running is { } running)
            {
                load = running;
            }
            else if (!refresh && entry.Result is { } result)
            {
                return result;
            }
            else
            {
                load = entry.Running = RunAsync(repository, entry, entry.LocalVersion);
            }
        }

        return await load.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs one load into its entry. Yields first, so the entry's
    /// <see cref="Entry.Running"/> is set before the load can finish and clear it.</summary>
    /// <param name="localVersion">The clone's record version when the load
    /// started; a record made after it is newer than the load's look.</param>
    private async Task<DevbookBranchList> RunAsync(GitHubRepositoryRef repository, Entry entry, int localVersion)
    {
        await Task.Yield();

        DevbookBranchList result;
        try
        {
            using var timeout = new CancellationTokenSource(_loadTimeout);

            var local = CheckCloneAsync(repository, timeout.Token);
            var remote = ReadBranchesAsync(repository, timeout.Token);

            result = (await remote.ConfigureAwait(false)) with { Local = await local.ConfigureAwait(false) };
        }
        catch (Exception exception)
        {
            // Nothing above throws on purpose; anything that still does is a
            // load that failed, and a failed load is an answer the picker can show.
            result = DevbookBranchList.Failed(Refusal(exception));
        }

        lock (_gate)
        {
            // A refused load over a good one keeps what the good one listed:
            // asking again offline must not empty the picker. The refusal rides
            // beside the branches, and the clone takes the newer look if any.
            if (result.DefaultBranch is null && result.Error is not null && entry.Result is { DefaultBranch: not null } previous)
            {
                result = previous with { Error = result.Error, Local = result.Local ?? previous.Local };
            }

            // A clone recorded after this load started (a pull, a check) is
            // newer than the look this load took.
            if (entry.LocalVersion != localVersion) result = result with { Local = entry.RecordedLocal };

            entry.Result = result;
            entry.Running = null;
            entry.Generation++;
        }

        return result;
    }

    private async Task<DevbookBranchList> ReadBranchesAsync(GitHubRepositoryRef repository, CancellationToken cancellationToken)
    {
        IReadOnlyList<GitHubBranchHead> heads;
        GitHubBranchHead? @default;

        try
        {
            heads = await branches.ListBranchHeadsAsync(repository, cancellationToken).ConfigureAwait(false);
            @default = await branches.ResolveHeadAsync(repository, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return DevbookBranchList.Failed(Refusal(exception));
        }

        var others = heads
            .Where(head => @default is null || !string.Equals(head.Branch, @default.Branch, StringComparison.Ordinal))
            .ToList();

        if (@default is null)
        {
            return new DevbookBranchList(
                null,
                [.. others.Select(head => new DevbookBranchRow(head.Branch, head.Sha, null))],
                null,
                $"GitHub did not say which branch {repository.FullName} treats as its default.");
        }

        var folders = DevbookChapterFolders.For(repository);
        using var parallel = new SemaphoreSlim(ParallelCompares);

        var rows = await Task.WhenAll(others.Select(async head =>
        {
            try
            {
                await parallel.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new DevbookBranchRow(head.Branch, head.Sha, null);
            }

            try
            {
                // The load's own deadline stops the read too, so a load that ran
                // out of time stops spending the rate limit. No caller's token
                // reaches here: a caller giving up stops only its own wait.
                var comparison = await comparisons.CompareAsync(repository, @default.Sha, head.Sha, cancellationToken, readDeadline: cancellationToken).ConfigureAwait(false);
                return new DevbookBranchRow(head.Branch, head.Sha, Change(comparison, folders));
            }
            catch (Exception)
            {
                // One branch GitHub would not compare is one branch the picker
                // cannot place; the rest are still worth listing.
                return new DevbookBranchRow(head.Branch, head.Sha, null);
            }
            finally
            {
                parallel.Release();
            }
        })).ConfigureAwait(false);

        return new DevbookBranchList(@default.Branch, rows, null, null);
    }

    private async Task<LocalGitRepositoryUpdateCheck?> CheckCloneAsync(GitHubRepositoryRef repository, CancellationToken cancellationToken)
    {
        // Whether the folder is really there is the adapter's to find out — a
        // screen does no file IO — and a missing one comes back as a check git
        // could not place, which the clone's row shows as no counts.
        if (string.IsNullOrWhiteSpace(repository.CloneDirectory)) return null;

        try
        {
            return await git.CheckForUpdatesAsync(repository, repository.CloneDirectory, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The clone's row then shows no counts, which is the honest answer to
            // a question git could not be asked.
            return null;
        }
    }

    /// <summary>
    /// The sections a comparison changed — a file under one on either side of a
    /// rename — in tab order, the instructions excluded, and whether a list GitHub
    /// cut short may hide one.
    /// </summary>
    private static DevbookBranchChange Change(GitHubComparison comparison, IReadOnlyList<DevbookFolderPrefix> folders)
    {
        var areas = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in comparison.Files)
        {
            foreach (var path in new[] { file.Path, file.PreviousPath })
            {
                if (DevbookChapterFolders.AreaOf(path, folders) is { } area
                    && !string.Equals(area, DevbookAreaCatalog.InstructionsKey, StringComparison.Ordinal))
                {
                    areas.Add(area);
                }
            }
        }

        IReadOnlyList<string> ordered = [.. areas.OrderBy(DevbookAreaCatalog.Order)];

        return new DevbookBranchChange(
            ordered,
            comparison.AheadBy,
            comparison.BehindBy,
            ordered.Count == 0 && comparison.FilesMayBeTruncated);
    }

    private static string Refusal(Exception exception) => exception switch
    {
        GitHubException or GitHubNotConfiguredException => exception.Message,
        OperationCanceledException => "GitHub did not answer in time.",
        _ => $"The branches could not be read: {exception.Message}"
    };

    private GitHubRepositoryRef? Repository(string? repositoryAlias) =>
        string.IsNullOrWhiteSpace(repositoryAlias) ? null : repositories.Current.Find(repositoryAlias);

    /// <summary>One repository's overview: the last that landed, and the load
    /// running now, if any. Guarded by the service's lock.</summary>
    private sealed class Entry
    {
        public DevbookBranchList? Result { get; set; }

        public Task<DevbookBranchList>? Running { get; set; }

        /// <summary>The clone's last recorded answer, and how many times one has
        /// been recorded.</summary>
        public LocalGitRepositoryUpdateCheck? RecordedLocal { get; set; }

        public int LocalVersion { get; set; }

        /// <summary>How many loads have landed.</summary>
        public int Generation { get; set; }
    }
}

/// <summary>What one load found.</summary>
/// <param name="DefaultBranch">The branch every other is compared with, or null
/// when GitHub did not say.</param>
/// <param name="Branches">Every other branch, in the order GitHub listed them,
/// compared or not.</param>
/// <param name="Local">Where the local clone stands against its upstream, or null
/// with no clone or no answer.</param>
/// <param name="Error">Why the branches could not be read, or null.</param>
public sealed record DevbookBranchList(
    string? DefaultBranch,
    IReadOnlyList<DevbookBranchRow> Branches,
    LocalGitRepositoryUpdateCheck? Local,
    string? Error)
{
    internal static readonly DevbookBranchList Empty = new(null, [], null, null);

    /// <summary>The branches that change the devbook, or may.</summary>
    public IEnumerable<DevbookBranchRow> DevbookBranches => Branches.Where(row => row.ChangesDevbook);

    /// <summary>The row for a branch, or null when the listing did not hold it.</summary>
    public DevbookBranchRow? Find(string? branch) =>
        Branches.FirstOrDefault(row => string.Equals(row.Branch, branch, StringComparison.Ordinal));

    internal static DevbookBranchList Failed(string error) => new(null, [], null, error);
}

/// <summary>One branch and what it changes.</summary>
/// <param name="Change">What the comparison with the default branch found, or
/// null when GitHub would not compare it.</param>
public sealed record DevbookBranchRow(string Branch, string Sha, DevbookBranchChange? Change)
{
    /// <summary>Whether the branch changes a devbook section, or may.</summary>
    public bool ChangesDevbook => Change is { } change && (change.Areas.Count > 0 || change.MayChange);
}

/// <summary>A branch against the default branch.</summary>
/// <param name="Areas">The devbook sections it changes, in tab order.</param>
/// <param name="AheadBy">Commits it has that the default branch has not.</param>
/// <param name="BehindBy">Commits the default branch has that it has not.</param>
/// <param name="MayChange">GitHub listed as many files as it ever does and none
/// was in the devbook, so one past them may be.</param>
public sealed record DevbookBranchChange(IReadOnlyList<string> Areas, int AheadBy, int BehindBy, bool MayChange);
