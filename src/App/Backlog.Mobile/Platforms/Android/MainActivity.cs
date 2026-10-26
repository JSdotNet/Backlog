using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

using AndroidX.Core.App;
using AndroidX.Core.Content;

using Backlog.Mobile.Services;
using Backlog.Mobile.UI.Notifications;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Mobile;

// SingleTask is what makes a share reach the app the person sees. A share sheet
// starts the target activity inside the *sending* app's task, and SingleTop only
// reuses an instance that is on top of that same task — so a share from YouTube
// built a second MainActivity in YouTube's task, with its own WebView, while the
// singleton share receiver kept handing the payload to the first instance hidden
// behind it. SingleTask keeps one instance in Backlog's own task and brings it
// forward with the intent through OnNewIntent, whichever app shared. The
// IntentFilter is what puts Backlog in the share sheet at all: ACTION_SEND with
// text/plain, which is what a browser or YouTube sends when it shares a link.
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTask, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(new[] { Intent.ActionSend }, Categories = new[] { Intent.CategoryDefault }, DataMimeType = "text/plain")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // The share that launched the app. This runs before the BlazorWebView has
        // built the layout that captures it, which is why the receiver buffers
        // rather than just raising an event.
        Forward(Intent);

        AskToNotify();
    }

    /// <summary>A share into an app that is already running, thanks to
    /// <see cref="LaunchMode.SingleTask"/>.</summary>
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);

        // Kept as the activity's current intent, so anything that reads it later
        // sees the share the person just made rather than the one before it.
        Intent = intent;

        Forward(intent);
    }

    /// <summary>
    /// On screen again: an update confirmation Android asked for while Backlog was in
    /// the background, where it could not be shown, is shown now.
    /// </summary>
    protected override void OnResume()
    {
        base.OnResume();
        ApkInstallStatusReceiver.ActivityResumed(this);
    }

    protected override void OnPause()
    {
        ApkInstallStatusReceiver.ActivityPaused(this);
        base.OnPause();
    }

    /// <summary>
    /// Resolved from the app's services on each intent instead of being held in a
    /// field: the activity can outlive a MauiApp rebuild, and a stale receiver
    /// would publish into a buffer nothing is subscribed to.
    /// </summary>
    private static void Forward(Intent? intent)
    {
        var services = IPlatformApplication.Current?.Services;

        services?.GetService<AndroidShareTargetReceiver>()?.Receive(intent);

        // A tap on a notification in the tray (PushTrayPresenter): the layout opens its
        // detail, now or once it is drawn. Cleared once read, like a share, so a recreated
        // activity handed the same intent does not open it again.
        if (intent?.Action == PushTrayPresenter.OpenAction
            && Guid.TryParse(intent.GetStringExtra(PushTrayPresenter.NotificationIdExtra), out var notificationId))
        {
            intent.RemoveExtra(PushTrayPresenter.NotificationIdExtra);
            services?.GetService<NotificationOpenRequests>()?.Request(notificationId);
        }
    }

    /// <summary>
    /// Android 13 and later show a notification only once the person allowed it, so the app
    /// asks at start until they have answered; earlier versions allow it by default. Declining
    /// leaves the app as it was — the Notifications tab still shows every question.
    /// </summary>
    private void AskToNotify()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return;
        if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.PostNotifications) == Permission.Granted) return;

        ActivityCompat.RequestPermissions(this, [Manifest.Permission.PostNotifications], NotifyPermissionRequest);
    }

    /// <summary>The request code of <see cref="AskToNotify"/>; nothing waits on its answer.</summary>
    private const int NotifyPermissionRequest = 3801;
}
