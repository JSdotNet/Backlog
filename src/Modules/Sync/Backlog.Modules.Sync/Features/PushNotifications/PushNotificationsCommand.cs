using System.Diagnostics;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Backlog.Modules.Sync.Features.PushNotifications;

/// <summary>
/// An owner instance hands over the notifications it published, or changed, since it last
/// managed to (local ADR 0038). The scope comes from the token and the notifications from the
/// body, and nothing in a <see cref="NotificationChange"/> can name an owner.
/// </summary>
public sealed record PushNotificationsCommand(OwnerScope Scope, IReadOnlyList<NotificationChange> Changes);

/// <summary>
/// Writes the batch and answers what it took. A notification held under another owner instance
/// is not written (one writer per notification, ADR 0038 §1) and comes back in
/// <see cref="PushNotificationsResponse.NotOwned"/> rather than refusing the batch, so one
/// colliding id never holds the owner's other notifications back.
/// <para>
/// A batch that wrote anything nudges the owner's desktops (ADR 0038 §4) — after the write, so
/// a desktop that pulls on the nudge finds what caused it. The nudge names the kind of change
/// and nothing of it.
/// </para>
/// <para>
/// Then each notification the replica reports as published now or moved state is pushed to the
/// owner's phones (ADR 0038 §4), and nothing else is: the owner's ten-minute presence refresh
/// re-pushes every open notification in the state already held. Best effort and bounded — a
/// failure is logged and counted and never fails the push, once the hub has failed the rest of
/// the batch is not sent, and the whole batch's sends share <see cref="SendBudget"/>, so a hub
/// that is slow without failing cannot hold the owner's request past it.
/// </para>
/// </summary>
public sealed partial class PushNotificationsCommandHandler(
    INotificationReplica replica,
    INotificationNudgeChannel nudges,
    INotificationPushChannel pushes,
    TimeProvider time,
    ILogger<PushNotificationsCommandHandler> log)
    : ICommandHandler<PushNotificationsCommand, Result<PushNotificationsResponse>>
{
    /// <summary>How long one batch's Android pushes may take in all. A call under way when it
    /// runs out is cut short, and the batch's remaining pushes are not sent.</summary>
    internal static readonly TimeSpan SendBudget = TimeSpan.FromSeconds(15);

    public async Task<Result<PushNotificationsResponse>> Handle(PushNotificationsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = SyncTelemetry.Source.StartActivity("sync.push_notifications", ActivityKind.Internal);
        activity?.SetTag(SyncTelemetry.OwnerIdTag, command.Scope.OwnerId.ToString());
        activity?.SetTag(SyncTelemetry.DeviceIdTag, command.Scope.DeviceId.ToString());
        activity?.SetTag(SyncTelemetry.BatchSizeTag, command.Changes.Count);

        var result = await replica.Upsert(command.Scope, command.Changes, cancellationToken);

        if (result.NotOwned.Count > 0)
        {
            activity?.SetTag(SyncTelemetry.ReasonTag, SyncErrorCodes.NotificationNotOwner);
        }

        if (result.Accepted > 0)
        {
            await nudges.PublishAsync(command.Scope.OwnerId, NotificationNudgeKinds.Notification, cancellationToken);
        }

        await PushToPhonesAsync(command.Scope.OwnerId, result.Transitions, cancellationToken);

        return new PushNotificationsResponse(result.Accepted, result.NotOwned);
    }

    private async Task PushToPhonesAsync(OwnerId owner, IReadOnlyList<NotificationChange> transitions, CancellationToken cancellationToken)
    {
        if (transitions.Count == 0) return;

        using var budget = new CancellationTokenSource(SendBudget, time);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);

        for (var index = 0; index < transitions.Count; index++)
        {
            var message = NotificationPushMessages.For(transitions[index]);

            if (budget.IsCancellationRequested)
            {
                Skip(transitions.Count - index);
                LogBudgetSpent(log, transitions.Count - index, SendBudget);
                return;
            }

            try
            {
                await pushes.SendAsync(owner, message, bounded.Token);
                SyncTelemetry.PushSent.Add(1);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // The write landed before the hub was asked; the phone finds the change on its
                // next pull. The rest of the batch is not tried against a hub that just failed,
                // or ran the batch out of time.
                Skip(transitions.Count - index);
                LogSendFailed(log, message.NotificationId, transitions.Count - index, ex);
                return;
            }
        }
    }

    private static void Skip(int count) =>
        SyncTelemetry.PushFailed.Add(
            count,
            new KeyValuePair<string, object?>(SyncTelemetry.PushOperationTag, SyncTelemetry.PushOperations.Send));

    [LoggerMessage(Level = LogLevel.Warning, Message = "The push hub did not take the Android push for notification {NotificationId}; {Skipped} push(es) of this batch are not sent, and the owner's phones find the changes on their next pull.")]
    private static partial void LogSendFailed(ILogger logger, Guid notificationId, int skipped, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The batch's Android pushes ran past their {Budget} budget; {Skipped} push(es) are not sent, and the owner's phones find the changes on their next pull.")]
    private static partial void LogBudgetSpent(ILogger logger, int skipped, TimeSpan budget);
}
