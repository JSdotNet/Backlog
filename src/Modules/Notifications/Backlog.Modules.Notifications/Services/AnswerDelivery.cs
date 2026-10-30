using Backlog.Modules.Notifications.Abstractions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Modules.Notifications.Services;

/// <summary>
/// Hands an accepted answer to the <see cref="INotificationAnswerHandler"/> registered for its
/// source, and replays the ones no handler took
/// (<c>.devbook/domain/notifications/domain.md#answer</c>).
/// <para>
/// The answer is already stored when it arrives here. It is marked delivered only once every
/// handler for the source returned, so an answer whose handler is not registered yet, or threw,
/// stays waiting and the next <see cref="ReplayAsync"/> — at startup — hands it over again. One
/// delivery at a time, each re-reading the stored notification first, so the startup replay and a
/// reply accepted while it runs never hand the same answer over twice in one session. A handler
/// still treats a second delivery for the same notification id as the same answer: delivery is at
/// least once, and a crash between the handler returning and the mark being stored repeats it.
/// </para>
/// <para>
/// Logs carry the notification's id and source, never its body or an open answer's text
/// (<c>.devbook/arc42/08-crosscutting-concepts.md#notifications</c>).
/// </para>
/// </summary>
internal sealed class AnswerDelivery(
    IEnumerable<INotificationAnswerHandler> handlers,
    INotificationRepository repository,
    TimeProvider time,
    ILogger<AnswerDelivery>? logger = null) : INotificationAnswerReplay, IDisposable
{
    private readonly IReadOnlyList<INotificationAnswerHandler> _handlers = [.. handlers];
    private readonly ILogger _logger = logger ?? NullLogger<AnswerDelivery>.Instance;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Hands the stored answer of notification <paramref name="notificationId"/> to its
    /// source's handlers, unless it was handed over already, and answers whether they all took it.</summary>
    public async Task<bool> DeliverAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var notification = await repository.GetAsync(notificationId, cancellationToken).ConfigureAwait(false);
            if (notification is null || notification.Answer is not { } answer || !notification.AwaitsDelivery) return false;

            var targets = _handlers
                .Where(handler => string.Equals(handler.Source, notification.Source.Context, StringComparison.Ordinal))
                .ToList();

            if (targets.Count == 0) return false;

            foreach (var handler in targets)
            {
                try
                {
                    await handler.HandleAnswerAsync(answer, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogWarning(
                        exception,
                        "The {Source} handler did not take the answer to notification {NotificationId}; it is replayed on the next start.",
                        notification.Source.Context,
                        notification.Id);
                    return false;
                }
            }

            notification.MarkAnswerDelivered(time.GetUtcNow());
            await repository.SaveAsync(notification, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Hands every stored answer no handler has taken yet to its source's handlers, and
    /// answers how many were taken. One that fails is logged and left for the next start; the
    /// rest still go.</summary>
    public async Task<int> ReplayAsync(CancellationToken cancellationToken = default)
    {
        var delivered = 0;

        foreach (var notification in await repository.ListAwaitingDeliveryAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                if (await DeliverAsync(notification.Id, cancellationToken).ConfigureAwait(false)) delivered++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "The answer to notification {NotificationId} was not replayed; it is tried again on the next start.", notification.Id);
            }
        }

        return delivered;
    }

    public void Dispose() => _gate.Dispose();
}
