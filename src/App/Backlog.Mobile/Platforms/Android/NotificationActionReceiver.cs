using Android.App;
using Android.Content;

using RemoteInput = AndroidX.Core.App.RemoteInput;

using Backlog.Mobile.UI.Notifications;
using Backlog.Mobile.UI.Outbox;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backlog.Mobile;

/// <summary>
/// Where a set answer or an inline reply tapped in the tray arrives (local ADR 0038 §4). The
/// reply is queued exactly as one made in the app — <see cref="PushActions.ReplyAsync"/> through
/// <see cref="NotificationViewProjection.ReplyAsync"/>, a <see cref="ReplyOutboxKind"/> entry —
/// and the outbox is flushed once, so it leaves now when there is signal and waits in the outbox
/// when there is none.
/// </summary>
/// <remarks>
/// <para>
/// Android gives a receiver about ten seconds, and only while <see cref="BroadcastReceiver.OnReceive"/>
/// runs; <see cref="BroadcastReceiver.GoAsync"/> keeps the process for the asynchronous work,
/// which is held under <see cref="Budget"/> so it finishes inside that (guideline ADR 0015).
/// Services are resolved from <see cref="IPlatformApplication.Current"/> on each broadcast, as
/// <see cref="MainActivity"/> does: Android creates a fresh receiver for every one.
/// </para>
/// <para>
/// A reply queued takes the entry out of the tray; the owner's verdict comes back as the next
/// push. One that could not be queued — the question settled since, the phone no longer holds
/// it — puts the entry back without actions, so the tray stops waiting and the tap opens the app.
/// Not exported: only this app's own pending intents reach it.
/// </para>
/// </remarks>
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class NotificationActionReceiver : BroadcastReceiver
{
    /// <summary>The most the queue and its one flush may take, inside Android's ten seconds.</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(8);

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != PushTrayPresenter.AnswerAction) return;
        if (!Guid.TryParse(intent.GetStringExtra(PushTrayPresenter.NotificationIdExtra), out var notificationId)) return;

        var services = IPlatformApplication.Current?.Services;
        if (services is null) return;

        var key = intent.GetStringExtra(PushTrayPresenter.ActionKeyExtra);
        var title = intent.GetStringExtra(PushTrayPresenter.TitleExtra);
        var text = RemoteInput.GetResultsFromIntent(intent)?.GetCharSequence(PushTrayPresenter.ReplyTextKey)?.ToString();

        var pending = GoAsync();

        _ = Task.Run(async () =>
        {
            try
            {
                await AnswerAsync(services, notificationId, key, text, title);
            }
            finally
            {
                pending?.Finish();
            }
        });
    }

    private static async Task AnswerAsync(IServiceProvider services, Guid notificationId, string? key, string? text, string? title)
    {
        var presenter = services.GetRequiredService<PushTrayPresenter>();
        var logger = services.GetService<ILogger<NotificationActionReceiver>>();

        using var budget = new CancellationTokenSource(Budget);
        var queued = false;

        try
        {
            var reply = await PushActions.ReplyAsync(services.GetRequiredService<NotificationViewProjection>(), notificationId, key, text, budget.Token);

            if (reply is null)
            {
                presenter.ShowUnanswered(notificationId, title);
                return;
            }

            queued = true;
            presenter.Cancel(notificationId);

            // The reply is kept whatever this flush does: a failed send stays queued, and the
            // next resume or reconnect sends it.
            await services.GetRequiredService<DeviceOutbox>().FlushAsync(budget.Token);
        }
        catch (Exception ex)
        {
            // Never the answer or the text: only that a tray answer did not complete.
            logger?.LogWarning("An answer from the tray did not complete: {Error}", ex.GetType().Name);

            if (!queued) presenter.ShowUnanswered(notificationId, title);
        }
    }
}
