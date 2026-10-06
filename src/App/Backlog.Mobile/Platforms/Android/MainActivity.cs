using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

using Backlog.Mobile.Services;

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
    /// Resolved from the app's services on each intent instead of being held in a
    /// field: the activity can outlive a MauiApp rebuild, and a stale receiver
    /// would publish into a buffer nothing is subscribed to.
    /// </summary>
    private static void Forward(Intent? intent) =>
        IPlatformApplication.Current?.Services.GetService<AndroidShareTargetReceiver>()?.Receive(intent);
}
