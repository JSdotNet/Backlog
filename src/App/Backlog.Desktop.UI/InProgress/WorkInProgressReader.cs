using Backlog.Desktop.UI.Tasks;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Desktop.UI.InProgress;

/// <summary>
/// The sessions and the open pull requests the In progress view and a task's
/// "Sessions and pull requests" section draw from, read on demand and kept until the
/// next read.
/// <para>
/// One reader the shell owns and hands to both, so opening a task after the view
/// has read costs nothing, and the side panel never reads GitHub on its own: it reads
/// the session record — local files — and leaves the pull requests to the view, or to
/// the reader's own "Link a session or pull request…", which is an act a person asked
/// for. A task's recorded pull requests still draw from what the task list already
/// read about them, so the panel is never empty for want of a GitHub read.
/// </para>
/// <para>
/// Both sources are optional, the way the pull requests list resolves them: a host
/// without GitHub reads no pull requests, a host without Sessions reads no sessions,
/// and a source that cannot be read is said rather than thrown.
/// </para>
/// <para>
/// Beside the open read, the pull requests tasks link that it did not list — merged,
/// closed, or open in a repository no longer configured — are read by number, with
/// their state, merge date and author (<see cref="LinkedPullRequests"/>), once the open
/// read has landed rather than as part of it. A merged one cannot change again, so it
/// is read once and kept for the life of the reader, which is the shell's; a closed or
/// open one is read again with the next read of the open list, at most once per
/// <see cref="FreshFor"/> like the open list itself.
/// </para>
/// </summary>
/// <param name="linked">The pull requests the tasks link, asked at the start of every
/// read of the pull requests; null reads none by number. A read that starts before the
/// task list has loaded finds no links, and the next read reads them.</param>
public sealed class WorkInProgressReader(
    GitHubIntegration? gitHub,
    IAgentSessionSource? sessions,
    TimeProvider? clock = null,
    Func<IEnumerable<GitHubPullRequestReference>>? linked = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private Task? _sessionsRead;
    private Task? _allRead;

    /// <summary>A reader over whatever the host composed — each source optional, the
    /// way the pull requests list resolves them — so the shell that owns it names
    /// neither.</summary>
    public static WorkInProgressReader From(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var tasks = services.GetService(typeof(TasksDesktopState)) as TasksDesktopState;

        return new WorkInProgressReader(
            services.GetService(typeof(GitHubIntegration)) as GitHubIntegration,
            services.GetService(typeof(IAgentSessionSource)) as IAgentSessionSource,
            services.GetService(typeof(TimeProvider)) as TimeProvider,
            tasks is null ? null : () => LinksOf(tasks.Rows));
    }

    /// <summary>Every pull request the given entries link, each once.</summary>
    public static IEnumerable<GitHubPullRequestReference> LinksOf(IEnumerable<EntryRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return rows
            .SelectMany(row => row.PullRequestLinks)
            .Select(link => new GitHubPullRequestReference(link.Repository, link.Number))
            .DistinctBy(link => Key(link), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The sessions the last read found, newest activity first.</summary>
    public IReadOnlyList<AgentSession> Sessions { get; private set; } = [];

    /// <summary>The open pull requests of every registered repository the last read found.</summary>
    public IReadOnlyList<GitHubOpenPullRequest> PullRequests { get; private set; } = [];

    /// <summary>
    /// The linked pull requests the open read did not list, read by number — merged ones
    /// kept from the first read that found them, the rest from the last read. One that
    /// could not be read is absent until a later read finds it.
    /// </summary>
    public IReadOnlyList<GitHubLinkedPullRequest> LinkedPullRequests { get; private set; } = [];

    /// <summary>The repositories GitHub would not answer for on the last read.</summary>
    public IReadOnlyList<GitHubRepositoryFailure> Failures { get; private set; } = [];

    /// <summary>Whether the session record could not be read last time.</summary>
    public bool SessionsUnreadable { get; private set; }

    /// <summary>Whether the sessions have been read at least once.</summary>
    public bool SessionsRead { get; private set; }

    /// <summary>Whether the pull requests have been read at least once.</summary>
    public bool PullRequestsRead { get; private set; }

    /// <summary>Whether there is a GitHub to read: the adapter composed and a
    /// repository registered with it.</summary>
    public bool GitHubConfigured => !ReadsPullRequests || gitHub is { IsConfigured: true };

    /// <summary>When the last read finished, for "6 min ago".</summary>
    public DateTimeOffset ReadAt { get; private set; }

    /// <summary>Whether a read is out.</summary>
    public bool Reading => _reads > 0;

    /// <summary>Whether the session record is read at all — the host's Sessions
    /// feature. Off, the reader holds no sessions.</summary>
    public bool ReadsSessions { get; set; } = true;

    /// <summary>Whether GitHub is read at all — the host's Pull requests feature.
    /// Off, the reader holds no pull requests.</summary>
    public bool ReadsPullRequests { get; set; } = true;

    /// <summary>How long an <c>Ensure</c> call answers with the read already made
    /// before it reads again. Long enough that opening task after task costs one
    /// read; short enough that a session started since is offered.</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(1);

    /// <summary>Raised after every read lands.</summary>
    public event Action? Changed;

    private int _reads;
    private int _sessionGeneration;
    private int _pullGeneration;

    /// <summary>The sessions, read again only once the last read is older than
    /// <see cref="FreshFor"/>; a read still out is answered rather than doubled.</summary>
    public Task EnsureSessionsAsync() =>
        _sessionsRead is { } read && (!read.IsCompleted || Fresh(_sessionsReadAt)) ? read : _sessionsRead = ReadSessionsOnlyAsync();

    /// <summary>The sessions and the pull requests, on the same terms.</summary>
    public Task EnsureAllAsync() =>
        _allRead is { } read && (!read.IsCompleted || Fresh(_pullsReadAt)) ? read : ReadAsync();

    private DateTimeOffset _sessionsReadAt = DateTimeOffset.MinValue;
    private DateTimeOffset _pullsReadAt = DateTimeOffset.MinValue;

    private bool Fresh(DateTimeOffset readAt) => _clock.GetUtcNow() - readAt < FreshFor;

    /// <summary>A fresh read of both, side by side — Refresh, and the first opening.</summary>
    public Task ReadAsync()
    {
        var read = ReadBothAsync();
        _allRead = read;
        _sessionsRead = read;
        return read;
    }

    private async Task ReadSessionsOnlyAsync()
    {
        _reads++;
        try
        {
            await ReadSessionsAsync();
            ReadAt = _clock.GetUtcNow();
        }
        finally
        {
            _reads--;
        }

        Changed?.Invoke();
    }

    private async Task ReadBothAsync()
    {
        _reads++;
        try
        {
            await Task.WhenAll(ReadSessionsAsync(), ReadPullRequestsAsync());
            ReadAt = _clock.GetUtcNow();
        }
        finally
        {
            _reads--;
        }

        Changed?.Invoke();
    }

    /// <summary>One read of the session record, kept only if no read started after
    /// it has landed first.</summary>
    private async Task ReadSessionsAsync()
    {
        var generation = ++_sessionGeneration;

        if (sessions is null || !ReadsSessions)
        {
            Sessions = [];
            return;
        }

        IReadOnlyList<AgentSession> read;
        bool unreadable;
        try
        {
            read = [.. (await sessions.GetSessionsAsync()).Sessions.OrderByDescending(session => session.LastActivityAt)];
            unreadable = false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            read = Sessions;
            unreadable = true;
        }

        if (generation != _sessionGeneration) return;

        Sessions = read;
        SessionsUnreadable = unreadable;
        SessionsRead = true;
        _sessionsReadAt = _clock.GetUtcNow();
    }

    private async Task ReadPullRequestsAsync()
    {
        var generation = ++_pullGeneration;

        if (!ReadsPullRequests || gitHub is not { IsConfigured: true } configured)
        {
            PullRequests = [];
            LinkedPullRequests = [];
            Failures = [];
            return;
        }

        // Asked before the first await, on the caller's dispatcher, which is where the
        // task list's rows may be read.
        GitHubPullRequestReference[] links = linked is null ? [] : [.. linked()];

        GitHubPullRequestListing listing;
        try
        {
            listing = await configured.ListOpenPullRequestsAsync(configured.Repositories);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The adapter keeps each repository's refusal as that repository's
            // failure; anything that still escapes is the whole read failing, and is
            // said as one failure rather than taking the view down with it.
            listing = new GitHubPullRequestListing(PullRequests, [new GitHubRepositoryFailure("GitHub", exception.Message)]);
        }

        if (generation != _pullGeneration) return;

        PullRequests = listing.PullRequests;
        Failures = listing.Failures;
        PullRequestsRead = true;
        _pullsReadAt = _clock.GetUtcNow();

        // After the open read has landed and not as part of it: the open list is what
        // the view draws, and nothing it draws waits on a read it does not draw from.
        LinkedRead = ReadLinkedAsync(configured, links, listing.PullRequests, generation);
    }

    /// <summary>The read by number that followed the last read of the open list —
    /// what a caller awaits to see <see cref="LinkedPullRequests"/> settle.</summary>
    internal Task LinkedRead { get; private set; } = Task.CompletedTask;

    /// <summary>The linked pull requests read merged, by key. Merged is for good, so
    /// these are never read again. Closed is not: GitHub can reopen a closed one.</summary>
    private readonly Dictionary<string, GitHubLinkedPullRequest> _merged = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Links whose repository answered without them — a number that is not a
    /// pull request, or one deleted — by key. Not asked about again.</summary>
    private readonly HashSet<string> _missing = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pull requests read by <see cref="ReadPullRequestAsync"/> that no read of
    /// the linked pull requests has yet been asked about, by key.</summary>
    private readonly Dictionary<string, GitHubLinkedPullRequest> _byReference = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// One read by number of the linked pull requests the open read did not list and
    /// that are not known merged or missing, kept only if no read started after it has
    /// landed first — apart from what it found merged, which no later read can change.
    /// <para>
    /// Quiet: a repository that refuses is not added to <see cref="Failures"/>, which
    /// the view draws as a line of its own — the open read already says when a
    /// configured repository cannot be read, and a linked pull request that cannot be
    /// read is asked about again on the next read, as the task list's own background
    /// check of them does.
    /// </para>
    /// </summary>
    private async Task ReadLinkedAsync(
        GitHubIntegration configured,
        IReadOnlyCollection<GitHubPullRequestReference> links,
        IReadOnlyList<GitHubOpenPullRequest> open,
        int generation)
    {
        var listed = open.Select(pull => InProgressProjection.Key(pull.RepositoryFullName, pull.Number)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wanted = links
            .Where(link => link.Number > 0 && !string.IsNullOrWhiteSpace(link.RepositoryFullName))
            .Where(link => !listed.Contains(Key(link)) && !_merged.ContainsKey(Key(link)) && !_missing.Contains(Key(link)))
            .ToList();

        var listing = GitHubLinkedPullRequestListing.Empty;
        if (wanted.Count > 0)
        {
            try
            {
                listing = await configured.ListLinkedPullRequestsAsync(wanted);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Quiet, as above: what was not read is asked about next time.
                listing = new GitHubLinkedPullRequestListing([], [new GitHubRepositoryFailure("GitHub", exception.Message)]);
            }
        }

        foreach (var pull in listing.PullRequests.Where(pull => pull.State is GitHubItemState.Merged))
        {
            _merged[InProgressProjection.Key(pull.RepositoryFullName, pull.Number)] = pull;
        }

        if (generation != _pullGeneration) return;

        var refused = listing.Failures.Select(failure => failure.RepositoryFullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = listing.PullRequests.Select(pull => InProgressProjection.Key(pull.RepositoryFullName, pull.Number)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!refused.Contains("GitHub"))
        {
            foreach (var link in wanted.Where(link => !refused.Contains(link.RepositoryFullName.Trim()) && !found.Contains(Key(link))))
            {
                _missing.Add(Key(link));
            }
        }

        var linkedKeys = links.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A pull request read by reference since this read asked for its links was linked
        // after that list was taken, so this read knows nothing of it: it is kept as that
        // read found it until a read whose links include it reads it in its own right.
        foreach (var known in _byReference.Keys.Where(linkedKeys.Contains).ToList()) _byReference.Remove(known);

        List<GitHubLinkedPullRequest> read =
        [
            .. _merged.Where(merged => linkedKeys.Contains(merged.Key) && !listed.Contains(merged.Key)).Select(merged => merged.Value),
            .. listing.PullRequests.Where(pull => pull.State is not GitHubItemState.Merged)
        ];
        var readKeys = read.Select(pull => InProgressProjection.Key(pull.RepositoryFullName, pull.Number)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        LinkedPullRequests = [.. read, .. _byReference.Where(added => !readKeys.Contains(added.Key) && !listed.Contains(added.Key)).Select(added => added.Value)];

        if (wanted.Count > 0) Changed?.Invoke();
    }

    /// <summary>
    /// One pull request, read by number whatever its state — what "Link a session or
    /// pull request…" asks before it records a pull request named by number or URL.
    /// <para>
    /// Through the same read by number the linked pull requests are read with, and into
    /// the same cache: one found merged is kept for the life of the reader and never read
    /// again, and the rest join <see cref="LinkedPullRequests"/> until the next read of
    /// the open list reads them again — so the link carries the state GitHub gave here,
    /// for a reader that counts what merged, rather than waiting for that read. The card
    /// still draws the task list's record, as it does for every linked pull request.
    /// </para>
    /// </summary>
    /// <returns>The pull request; else null and why not — no GitHub, the repository's
    /// refusal, or no pull request by that number.</returns>
    public async Task<(GitHubLinkedPullRequest? PullRequest, string? Failure)> ReadPullRequestAsync(
        GitHubPullRequestReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var key = Key(reference);
        if (_merged.TryGetValue(key, out var known)) return (known, null);

        if (!ReadsPullRequests || gitHub is not { IsConfigured: true } configured)
        {
            return (null, "GitHub is not set up here, so there is no pull request to read.");
        }

        GitHubLinkedPullRequestListing listing;
        try
        {
            listing = await configured.ListLinkedPullRequestsAsync([reference]);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (null, exception.Message);
        }

        if (listing.PullRequests.FirstOrDefault(pull =>
                pull.Number == reference.Number
                && string.Equals(pull.RepositoryFullName, reference.RepositoryFullName.Trim(), StringComparison.OrdinalIgnoreCase)) is not { } found)
        {
            return listing.Failures.FirstOrDefault() is { } failure
                ? (null, $"GitHub would not read {failure.RepositoryFullName}: {failure.Message}")
                : (null, $"{reference.RepositoryFullName.Trim()} has no pull request #{reference.Number}.");
        }

        _missing.Remove(key);
        if (found.State is GitHubItemState.Merged) _merged[key] = found;
        _byReference[key] = found;

        LinkedPullRequests =
        [
            .. LinkedPullRequests.Where(pull => !string.Equals(InProgressProjection.Key(pull.RepositoryFullName, pull.Number), key, StringComparison.OrdinalIgnoreCase)),
            found
        ];
        Changed?.Invoke();

        return (found, null);
    }

    private static string Key(GitHubPullRequestReference link) => InProgressProjection.Key(link.RepositoryFullName.Trim(), link.Number);
}
