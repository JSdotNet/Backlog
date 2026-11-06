using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Desktop.UI.Goals;

/// <summary>
/// The sessions and the open pull requests the Goals view and a task's
/// "Sessions and pull requests" section draw from, read on demand and kept until the
/// next read.
/// <para>
/// One reader the shell owns and hands to both, so opening a task after the view
/// has read costs nothing, and the side panel never reads GitHub on its own: it reads
/// the session record — local files — and leaves the pull requests to the view, or to
/// the section's own "Link a session or pull request…", which is an act a person asked
/// for. A task's recorded pull requests still draw from what the task list already
/// read about them, so the panel is never empty for want of a GitHub read.
/// </para>
/// <para>
/// Both sources are optional, the way the pull requests list resolves them: a host
/// without Repository Management reads no pull requests, a host without Sessions reads
/// no sessions, and a source that cannot be read is said rather than thrown.
/// </para>
/// <para>
/// Pull requests are read through Repository Management's contract
/// (<see cref="IPullRequests"/>, local ADR 0042) and its one cache, which every other
/// reader of a pull request shares: the open list, and beside it the pull requests
/// tasks link that it did not list — merged, closed, or open in a repository no longer
/// configured — by number (<see cref="LinkedPullRequests"/>), once the open read has
/// landed rather than as part of it. What the cache keeps — a merged one for good, a
/// number GitHub answered without for good, the rest for a minute — is the contract's;
/// so is skipping an archived repository (local ADR 0029). A pull request this reader
/// read is not read again by Progress within the minute (V3).
/// </para>
/// </summary>
/// <param name="linked">The pull requests the tasks link, asked at the start of every
/// read of the pull requests; null reads none by number. A read that starts before the
/// task list has loaded finds no links, and the next read reads them.</param>
public sealed class WorkInProgressReader(
    IPullRequests? pullRequests,
    IAgentSessionSource? sessions,
    TimeProvider? clock = null,
    Func<IEnumerable<PullRequestReference>>? linked = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private Task? _sessionsRead;
    private Task? _allRead;

    /// <summary>A reader over whatever the host composed — each source optional, the
    /// way the pull requests list resolves them — so the shell that owns it names
    /// neither. The pull requests tasks link are Tasks' answer
    /// (<see cref="ITaskLookup.LinkedPullRequests"/>, local ADR 0042 V8); a host that
    /// composes no lookup reads none by number.</summary>
    public static WorkInProgressReader From(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var tasks = services.GetService(typeof(ITaskLookup)) as ITaskLookup;

        return new WorkInProgressReader(
            services.GetService(typeof(IPullRequests)) as IPullRequests,
            services.GetService(typeof(IAgentSessionSource)) as IAgentSessionSource,
            services.GetService(typeof(TimeProvider)) as TimeProvider,
            tasks is null ? null : () => tasks.LinkedPullRequests().Select(link => new PullRequestReference(link.RepositoryFullName, link.Number)));
    }

    /// <summary>The sessions the last read found, newest activity first.</summary>
    public IReadOnlyList<AgentSession> Sessions { get; private set; } = [];

    /// <summary>The open pull requests of every registered repository the last read found.</summary>
    public IReadOnlyList<PullRequestStatus> PullRequests { get; private set; } = [];

    /// <summary>
    /// The linked pull requests the open read did not list, read by number. One that
    /// could not be read is absent until a later read finds it.
    /// </summary>
    public IReadOnlyList<PullRequestStatus> LinkedPullRequests { get; private set; } = [];

    /// <summary>The repositories GitHub would not answer for on the last read.</summary>
    public IReadOnlyList<RepositoryReadFailure> Failures { get; private set; } = [];

    /// <summary>Whether the session record could not be read last time.</summary>
    public bool SessionsUnreadable { get; private set; }

    /// <summary>Whether the sessions have been read at least once.</summary>
    public bool SessionsRead { get; private set; }

    /// <summary>Whether the pull requests have been read at least once.</summary>
    public bool PullRequestsRead { get; private set; }

    /// <summary>Whether there is a GitHub to read: the contract composed and a
    /// repository registered with it.</summary>
    public bool GitHubConfigured => !ReadsPullRequests || pullRequests is { IsConfigured: true };

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

    /// <summary>The sessions and the pull requests, on the same terms. The open list is
    /// taken from the contract's cache where another reader listed it inside the minute.</summary>
    public Task EnsureAllAsync() =>
        _allRead is { } read && (!read.IsCompleted || Fresh(_pullsReadAt)) ? read : Read(PullRequestReadOptions.Default);

    private DateTimeOffset _sessionsReadAt = DateTimeOffset.MinValue;
    private DateTimeOffset _pullsReadAt = DateTimeOffset.MinValue;

    private bool Fresh(DateTimeOffset readAt) => _clock.GetUtcNow() - readAt < FreshFor;

    /// <summary>A fresh read of both, side by side — Refresh, the first opening, and the
    /// link picker's own read: the open list is asked of GitHub again, whatever another
    /// reader listed inside the minute.</summary>
    public Task ReadAsync() => Read(PullRequestReadOptions.Fresh);

    private Task Read(PullRequestReadOptions options)
    {
        var read = ReadBothAsync(options);
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

    private async Task ReadBothAsync(PullRequestReadOptions options)
    {
        _reads++;
        try
        {
            await Task.WhenAll(ReadSessionsAsync(), ReadPullRequestsAsync(options));
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

    private async Task ReadPullRequestsAsync(PullRequestReadOptions options)
    {
        var generation = ++_pullGeneration;

        if (!ReadsPullRequests || pullRequests is not { IsConfigured: true } contract)
        {
            PullRequests = [];
            LinkedPullRequests = [];
            Failures = [];
            return;
        }

        // Asked before the first await, on the caller's dispatcher, which is where the
        // task list's rows may be read.
        PullRequestReference[] links = linked is null ? [] : [.. linked()];
        var startedAt = _clock.GetUtcNow();

        PullRequestListing listing;
        try
        {
            listing = await contract.ListOpenAsync(null, options);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The contract keeps each repository's refusal as that repository's
            // failure; anything that still escapes is the whole read failing, and is
            // said as one failure rather than taking the view down with it.
            listing = new PullRequestListing(PullRequests, [new RepositoryReadFailure("GitHub", exception.Message)]);
        }

        if (generation != _pullGeneration) return;

        PullRequests = listing.PullRequests;
        Failures = listing.Failures;
        PullRequestsRead = true;
        _pullsReadAt = _clock.GetUtcNow();

        // After the open read has landed and not as part of it: the open list is what
        // the view draws, and nothing it draws waits on a read it does not draw from.
        LinkedRead = ReadLinkedAsync(contract, links, listing.PullRequests, generation, startedAt);
    }

    /// <summary>The read by number that followed the last read of the open list —
    /// what a caller awaits to see <see cref="LinkedPullRequests"/> settle.</summary>
    internal Task LinkedRead { get; private set; } = Task.CompletedTask;

    /// <summary>Pull requests linked by <see cref="ReadPullRequestAsync"/> that no read of
    /// the linked pull requests has yet been asked about, by key: the read that was out
    /// when the link was made took its links before it, and would otherwise drop it.</summary>
    private readonly Dictionary<string, PullRequestReference> _byReference = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// One read by number of the linked pull requests the open read did not list, kept
    /// only if no read started after it has landed first.
    /// <para>
    /// Quiet: a repository that refuses is not added to <see cref="Failures"/>, which
    /// the view draws as a line of its own — the open read already says when a
    /// configured repository cannot be read, and a linked pull request that cannot be
    /// read is asked about again on the next read, as the task list's own background
    /// check of them does.
    /// </para>
    /// </summary>
    private async Task ReadLinkedAsync(
        IPullRequests contract,
        IReadOnlyCollection<PullRequestReference> links,
        IReadOnlyList<PullRequestStatus> open,
        int generation,
        DateTimeOffset startedAt)
    {
        var listed = open.Select(pull => pull.Reference.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var linkedKeys = links.Select(link => link.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A pull request linked by reference since this read took its links is still
        // asked about, until a read whose links include it reads it in its own right.
        foreach (var known in _byReference.Keys.Where(linkedKeys.Contains).ToList()) _byReference.Remove(known);

        var wanted = links
            .Concat(_byReference.Values)
            .Where(link => link.Number > 0 && !string.IsNullOrWhiteSpace(link.RepositoryFullName))
            .DistinctBy(link => link.Key, StringComparer.OrdinalIgnoreCase)
            .Where(link => !listed.Contains(link.Key))
            .ToList();

        var listing = PullRequestListing.Empty;
        if (wanted.Count > 0)
        {
            try
            {
                // Fresh means read since this read began: what another view asked on
                // the same Refresh, not what an earlier one left behind. A merged pull
                // request, and a number GitHub answered without, the cache keeps for good.
                listing = await contract.ReadAsync(wanted, new PullRequestReadOptions(_clock.GetUtcNow() - startedAt));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Quiet, as above: what was not read is asked about next time.
            }
        }

        if (generation != _pullGeneration) return;

        LinkedPullRequests = [.. listing.PullRequests.Where(pull => !listed.Contains(pull.Reference.Key))];

        if (wanted.Count > 0) Changed?.Invoke();
    }

    /// <summary>
    /// One pull request, read by number whatever its state — what "Link a session or
    /// pull request…" asks before it records a pull request named by number or URL.
    /// <para>
    /// Read fresh through the contract, a person having just named it; a merged one the
    /// cache already holds is not read again. It joins <see cref="LinkedPullRequests"/>
    /// at once, so the link carries the state GitHub gave here, for a reader that counts
    /// what merged, rather than waiting for the next read of the open list. The card
    /// still draws the task list's record, as it does for every linked pull request.
    /// </para>
    /// </summary>
    /// <returns>The pull request; else null and why not — no GitHub, the repository's
    /// refusal, or no pull request by that number.</returns>
    public async Task<(PullRequestStatus? PullRequest, string? Failure)> ReadPullRequestAsync(
        PullRequestReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (!ReadsPullRequests || pullRequests is not { IsConfigured: true } contract)
        {
            return (null, "GitHub is not set up here, so there is no pull request to read.");
        }

        var asked = reference with { RepositoryFullName = reference.RepositoryFullName.Trim() };

        PullRequestListing listing;
        try
        {
            listing = await contract.ReadAsync([asked], PullRequestReadOptions.Fresh);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (null, exception.Message);
        }

        if (listing.PullRequests.FirstOrDefault(pull => pull.Reference.Names(asked.RepositoryFullName, asked.Number)) is not { } found)
        {
            return listing.Failures.FirstOrDefault() is { } failure
                ? (null, $"GitHub would not read {failure.RepositoryFullName}: {failure.Message}")
                : (null, $"{asked.RepositoryFullName} has no pull request #{asked.Number}.");
        }

        _byReference[asked.Key] = asked;

        LinkedPullRequests =
        [
            .. LinkedPullRequests.Where(pull => !pull.Reference.Names(asked.RepositoryFullName, asked.Number)),
            found
        ];
        Changed?.Invoke();

        return (found, null);
    }
}
