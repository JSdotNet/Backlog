using Android.Content;

using AndroidX.Core.Content.PM;
using AndroidX.Core.Graphics.Drawable;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backlog.Mobile;

/// <summary>
/// The Capture shortcut a long press on the launcher icon lists: a tap opens the capture sheet
/// over the Inbox, through <see cref="MainActivity"/>.
/// <para>
/// Dynamic rather than a static <c>shortcuts.xml</c>: Android starts a static shortcut with
/// <c>FLAG_ACTIVITY_CLEAR_TASK</c>, which would restart the BlazorWebView and lose a capture
/// typed but not saved, and its intent would need the activity's generated Java name. A dynamic
/// one reaches the running activity through <c>OnNewIntent</c>, like a share or a tray tap. It
/// exists once the app has been opened and is not backed up, so it is published on every start.
/// </para>
/// </summary>
internal static class CaptureShortcut
{
    /// <summary>The intent action of a tap on the shortcut.</summary>
    public const string Action = "com.jsdotnet.backlog.mobile.CAPTURE";

    /// <summary>The shortcut's id, a stored value: the launcher keeps a pinned copy by it.</summary>
    public const string Id = "capture";

    /// <summary>
    /// Publishes the shortcut on a worker thread — Android may take seconds over it — and never
    /// fails the start: a shortcut that could not be published is logged and left for the next.
    /// Below Android 7.1 there are no shortcuts and the call does nothing.
    /// </summary>
    public static void Publish(Context context, IServiceProvider? services)
    {
        var application = context.ApplicationContext ?? context;

        _ = Task.Run(() =>
        {
            try
            {
                var intent = new Intent(application, typeof(MainActivity)).SetAction(Action);

                // The binding's setters answer a nullable builder, so they are not chained.
                var builder = new ShortcutInfoCompat.Builder(application, Id);
                builder.SetShortLabel("Capture");
                builder.SetLongLabel("Capture");
                builder.SetIcon(IconCompat.CreateWithResource(application, Resource.Drawable.capture_shortcut_icon));
                builder.SetIntent(intent);
                builder.SetRank(0);

                ShortcutManagerCompat.PushDynamicShortcut(application, builder.Build());
            }
            catch (Exception ex)
            {
                services?.GetService<ILoggerFactory>()?.CreateLogger(typeof(CaptureShortcut))
                    .LogWarning("The Capture shortcut could not be published: {Error}", ex.GetType().Name);
            }
        });
    }
}
