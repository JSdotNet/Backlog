using System.Collections.Concurrent;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Modules.Sessions.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Desktop.UI.PullRequests;

/// <summary>
/// The Sessions context's pull request activity, as the desktop reads it through Repository
/// Management's contract (<see cref="IPullRequests"/>, local ADR 0042), telling
/// Notifications when a pull request a notification can be about ends
/// (<c>.devbook/domain/notifications/domain.md#subject</c>, <c>sessions / pull-request</c>).
/// <para>
/// Here rather than in <c>Backlog.Infrastructure.Sessions</c> because this is where the
/// pull-request contract and the session list meet: the session card reads each linked pull
/// request by number (<see cref="SessionPullRequestStatuses"/>), and a reading that finds
/// it merged or closed is the end, which closes every notification about it with
/// <see cref="SessionNotificationSubjects.PullRequestMergedReason"/> or
/// <see cref="SessionNotificationSubjects.PullRequestClosedReason"/>, as the reading found
/// it — once per process, since the card reads it again on every render. It also answers
/// <see cref="ISessionPullRequestStates"/>, which the Sessions subject probe asks at
/// startup, with the same read by number, through the contract's one cache.
/// </para>
/// </summary>
/// <param name="pullRequests">The contract, resolved per call; null reads nothing.</param>
public sealed class PullRequestSubjects(
    Func<IPullRequests?> pullRequests,
    Func<INotifications?> notifications,
    ILogger<PullRequestSubjects>? logger = null) : ISessionPullRequestStates
{
    private readonly Func<IPullRequests?> _pullRequests = pullRequests ?? throw new ArgumentNullException(nameof(pullRequests));
    private readonly Func<INotifications?> _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;
    private readonly ConcurrentDictionary<string, bool> _ended = new(StringComparer.Ordinal);

    /// <summary>Closes the notifications about <paramref name="pull"/> when the reading
    /// found it merged or closed and this process has not closed them already.</summary>
    public async Task ObserveAsync(PullRequestStatus pull, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pull);

        if (pull.IsOpen || pull.Number <= 0 || string.IsNullOrWhiteSpace(pull.RepositoryFullName)) return;

        var id = SessionNotificationSubjects.PullRequestId(pull.RepositoryFullName, pull.Number);
        if (!_ended.TryAdd(id, true)) return;

        var subject = new NotificationSubject(SessionNotificationSubjects.Context, SessionNotificationSubjects.PullRequest, id);

        try
        {
            if (_notifications() is not { } port) return;

            var reason = pull.State == PullRequestState.Merged
                ? SessionNotificationSubjects.PullRequestMergedReason
                : SessionNotificationSubjects.PullRequestClosedReason;

            var closed = await port.CloseForSubjectAsync(subject, reason, cancellationToken).ConfigureAwait(false);
            if (closed.IsFailure) _logger.LogDebug("Notifications about pull request {Id} were not closed: {Error}", id, closed.Error.Message);
        }
        catch (Exception exception)
        {
            // Let the next reading try again rather than remember an end it never reported.
            _ended.TryRemove(id, out _);
            _logger.LogWarning(exception, "Closing the notifications about pull request {Id} failed.", id);
        }
    }

    /// <summary>Read by number, whatever state it is in. Null without the contract, for
    /// an archived repository nothing was kept for, or when GitHub did not answer for it.</summary>
    public async Task<bool?> IsOpenAsync(string repository, int number, CancellationToken cancellationToken = default)
    {
        if (_pullRequests() is not { } contract || string.IsNullOrWhiteSpace(repository) || number <= 0) return null;

        var listing = await contract.ReadAsync([new PullRequestReference(repository.Trim(), number)], cancellationToken: cancellationToken).ConfigureAwait(false);
        var found = listing.PullRequests.FirstOrDefault(candidate => candidate.Reference.Names(repository, number));

        return found?.IsOpen;
    }
}
