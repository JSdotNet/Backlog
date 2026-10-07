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
/// </summary>
public sealed class WorkInProgressReader(GitHubIntegration? gitHub, IAgentSessionSource? sessions, TimeProvider? clock = null)
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

        return new WorkInProgressReader(
            services.GetService(typeof(GitHubIntegration)) as GitHubIntegration,
            services.GetService(typeof(IAgentSessionSource)) as IAgentSessionSource,
            services.GetService(typeof(TimeProvider)) as TimeProvider);
    }

    /// <summary>The sessions the last read found, newest activity first.</summary>
    public IReadOnlyList<AgentSession> Sessions { get; private set; } = [];

    /// <summary>The open pull requests of every registered repository the last read found.</summary>
    public IReadOnlyList<GitHubOpenPullRequest> PullRequests { get; private set; } = [];

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
            Failures = [];
            return;
        }

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
    }
}
