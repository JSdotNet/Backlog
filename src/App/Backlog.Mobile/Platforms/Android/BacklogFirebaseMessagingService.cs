using Android.App;

using Backlog.Mobile.UI.Notifications;

using Firebase.Messaging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backlog.Mobile;

/// <summary>
/// Where Firebase hands this app its token and the data messages Notification Hubs relays
/// (local ADR 0038 §4). The messages are data-only at high priority, so this runs whether the
/// app is on screen, in the background or not running at all.
/// </summary>
/// <remarks>
/// <para>
/// A message names a notification and its state, never its body or its answers. So the phone
/// pulls the notification feed first — the copy it pulls is what the tray's actions are built
/// from, and what an answer from the tray is checked against — and then shows the entry, or
/// takes it out of the tray when the notification has settled. A pull that fails still shows the
/// message's own words, without actions.
/// </para>
/// <para>
/// Firebase calls <see cref="OnMessageReceived"/> on a worker thread and may end the process
/// once it returns, so the work is waited for here, under <see cref="Budget"/>: Android allows
/// a high-priority message about twenty seconds (guideline ADR 0015).
/// </para>
/// </remarks>
[Service(Exported = false)]
[IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
public sealed class BacklogFirebaseMessagingService : FirebaseMessagingService
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(15);

    // The binding marks this override "deprecated" and names no replacement; Firebase's own
    // Android API still has onNewToken as the one place a refreshed token arrives. The base
    // method does nothing, so it is not called.
#pragma warning disable CS0672
    public override void OnNewToken(string token)
#pragma warning restore CS0672
    {
        if (IPlatformApplication.Current?.Services.GetService<PushRegistration>() is { } registration)
        {
            // Not awaited: registration is bounded and reports its own failures, and the next
            // start of the app tries again.
            _ = registration.TokenRefreshedAsync(token);
        }
    }

    public override void OnMessageReceived(RemoteMessage message)
    {
        base.OnMessageReceived(message);

        var services = IPlatformApplication.Current?.Services;
        if (services is null || PushMessage.Parse(message.Data?.AsReadOnly()) is not { } push) return;

        try
        {
            // A worker thread Firebase owns, which must not return before the tray is drawn.
            if (!Task.Run(() => ShowAsync(services, push)).Wait(Budget))
            {
                services.GetService<ILogger<BacklogFirebaseMessagingService>>()
                    ?.LogWarning("A push was not shown within {Budget}; the tray may not hold it.", Budget);
            }
        }
        catch (AggregateException ex)
        {
            services.GetService<ILogger<BacklogFirebaseMessagingService>>()
                ?.LogWarning("A push could not be shown: {Error}", ex.InnerException?.GetType().Name);
        }
    }

    private static async Task ShowAsync(IServiceProvider services, PushMessage push)
    {
        var presenter = services.GetRequiredService<PushTrayPresenter>();
        var projection = services.GetRequiredService<NotificationViewProjection>();

        // Settled: out of the tray at once, whatever the pull then says.
        if (!push.Shows) presenter.Cancel(push.Id);

        try
        {
            using var budget = new CancellationTokenSource(Budget - TimeSpan.FromSeconds(3));
            await projection.PullAsync(budget.Token);
        }
        catch (Exception ex)
        {
            services.GetService<ILogger<BacklogFirebaseMessagingService>>()
                ?.LogInformation("The pull after a push did not answer: {Error}", ex.GetType().Name);
        }

        if (!push.Shows) return;

        if (PushTray.Entry(push, projection.Find(push.Id)) is { } entry)
        {
            presenter.Show(entry);
        }
        else
        {
            presenter.Cancel(push.Id);
        }
    }
}
