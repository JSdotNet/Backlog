using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

using AndroidX.Core.App;
using AndroidX.Core.Content;

using Backlog.Mobile.UI.Notes;
using Backlog.Mobile.UI.Recordings;
using Backlog.Mobile.UI.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backlog.Mobile;

/// <summary>
/// The microphone foreground service a meeting recording runs in
/// (<c>.devbook/arc42/06-runtime-view.md#meeting-recording</c>): it keeps the process and the
/// microphone alive with the app in the background, and shows the ongoing notification — a timer,
/// Pause or Resume, and Stop — that drives the same <see cref="NoteRecordingSession"/> the note
/// page does.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Services.AndroidMeetingRecorder"/> starts it from the person's press on the visible
/// note, before the microphone, and stops it when the recording ends; it records nothing itself.
/// The <c>[Service]</c> attribute writes the manifest entry, with the microphone type Android 14
/// requires; the manifest declares only the permissions.
/// </para>
/// <para>
/// The notification is redrawn when the recording's state or note changes, not on every level
/// tick: the timer is Android's chronometer, counted from a start that leaves the pauses out.
/// </para>
/// </remarks>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMicrophone)]
public sealed class RecordingService : Service
{
    public const string PauseAction = "com.jsdotnet.backlog.mobile.RECORDING_PAUSE";
    public const string ResumeAction = "com.jsdotnet.backlog.mobile.RECORDING_RESUME";
    public const string StopAction = "com.jsdotnet.backlog.mobile.RECORDING_STOP";

    /// <summary>The recording's channel. A stored value: Android keeps the person's settings for
    /// it by this id.</summary>
    private const string ChannelId = "backlog-recording";

    private const int NotificationId = 4801;

    /// <summary>Guards the start handshake below, across service instances.</summary>
    private static readonly Lock Handshake = new();

    // Between startForegroundService and the service's own startForeground, a stop would make
    // Android crash the app ("did not then call Service.startForeground"). So the start answers
    // only once the service is in the foreground, and a stop asked for before then waits for it.
    private static TaskCompletionSource<bool>? s_promoted;
    private static bool s_starting;
    private static bool s_stopWhenPromoted;

    private readonly Lock _gate = new();

    private NoteRecordingSession? _session;
    private (MeetingRecorderState State, Guid? NoteId, string? Notice)? _shown;
    private bool _destroyed;

    /// <summary>
    /// Starts the service in the foreground and answers, once it is, whether it got there.
    /// Throws where Android refuses the start — the app not on screen — which the caller answers
    /// in words.
    /// </summary>
    public static Task<bool> StartAsync(Context context)
    {
        var promoted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (Handshake)
        {
            s_promoted?.TrySetResult(false);
            s_promoted = promoted;
            s_starting = true;
            s_stopWhenPromoted = false;
        }

        try
        {
            ContextCompat.StartForegroundService(context, new Intent(context, typeof(RecordingService)));
        }
        catch
        {
            lock (Handshake) s_starting = false;
            throw;
        }

        return promoted.Task;
    }

    /// <summary>Stops the service; while it is still on its way into the foreground, as soon as
    /// it is there.</summary>
    public static void Stop(Context context)
    {
        lock (Handshake)
        {
            if (s_starting)
            {
                s_stopWhenPromoted = true;
                return;
            }
        }

        context.StopService(new Intent(context, typeof(RecordingService)));
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();

        _session = Services()?.GetService<NoteRecordingSession>();
        if (_session is not null) _session.Changed += OnSessionChanged;
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var session = _session;

        switch (intent?.Action)
        {
            case PauseAction:
                if (session is not null) _ = Drive(session.PauseAsync().AsTask());
                break;

            case ResumeAction:
                if (session is not null) _ = Drive(session.ResumeAsync().AsTask());
                break;

            case StopAction:
                if (session is not null) _ = Drive(session.StopAsync());
                else StopSelf();
                break;

            default:
                Promote(session);
                break;
        }

        // A recording cannot be resumed by a restarted service: its recorder went with the process,
        // and the next start attaches what it wrote.
        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        // A level tick still on its way must not repost an ongoing notification nothing is behind.
        lock (_gate) _destroyed = true;

        if (_session is not null) _session.Changed -= OnSessionChanged;
        _session = null;

        base.OnDestroy();
    }

    /// <summary>Into the foreground within the five seconds Android gives a service started that
    /// way, with the microphone type from Android 11, where the type exists.</summary>
    private void Promote(NoteRecordingSession? session)
    {
        try
        {
            EnsureChannel();
            var notification = Build(session);

            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                StartForeground(NotificationId, notification, ForegroundService.TypeMicrophone);
            }
            else
            {
                StartForeground(NotificationId, notification);
            }
        }
        catch (Java.Lang.Exception ex)
        {
            // The recorder has not opened the microphone yet: it waits on this answer, and says
            // the recording could not start.
            Warn("The recording service could not enter the foreground: {Error}", ex.GetType().Name);
            Promoted(false);
            StopSelf();
            return;
        }

        if (Promoted(true)) StopSelf();
    }

    /// <summary>Ends the start handshake; answers whether a stop was asked for meanwhile.</summary>
    private static bool Promoted(bool promoted)
    {
        TaskCompletionSource<bool>? waiting;
        bool stop;

        lock (Handshake)
        {
            waiting = s_promoted;
            s_promoted = null;
            stop = s_stopWhenPromoted;
            s_starting = false;
            s_stopWhenPromoted = false;
        }

        waiting?.TrySetResult(promoted);
        return stop;
    }

    private void OnSessionChanged()
    {
        var session = _session;
        if (session is null) return;

        lock (_gate)
        {
            // The notice too, or a call's reason never reaches the notification.
            var now = (session.State, session.NoteId, session.Notice);
            if (_shown == now) return;
            _shown = now;
        }

        // Nothing to show once the recording ended; the recorder stops the service.
        if (session.State == MeetingRecorderState.Idle) return;

        try
        {
            var notification = Build(session);

            lock (_gate)
            {
                if (_destroyed) return;
                NotificationManagerCompat.From(this)?.Notify(NotificationId, notification);
            }
        }
        catch (Java.Lang.Exception)
        {
            // A notification that did not redraw keeps its last state; the recording goes on.
        }
    }

    private Notification Build(NoteRecordingSession? session)
    {
        var state = session?.State ?? MeetingRecorderState.Recording;

        // Each setter returns the builder, nullable in the binding; called as statements instead.
        var builder = new NotificationCompat.Builder(this, ChannelId);
        builder.SetSmallIcon(Resource.Drawable.notification_icon);
        builder.SetContentTitle(Title(session));
        builder.SetOngoing(true);
        builder.SetOnlyAlertOnce(true);
        builder.SetSilent(true);
        builder.SetCategory(NotificationCompat.CategoryService);
        builder.SetPriority(NotificationCompat.PriorityLow);
        builder.SetForegroundServiceBehavior(NotificationCompat.ForegroundServiceImmediate);
        builder.SetContentIntent(OpenIntent());

        switch (state)
        {
            case MeetingRecorderState.Paused:
                builder.SetContentText(session?.Notice ?? "Paused");
                builder.SetUsesChronometer(false);
                builder.SetShowWhen(false);
                builder.AddAction(0, "Resume", ActionIntent(ResumeAction));
                builder.AddAction(0, "Stop", ActionIntent(StopAction));
                break;

            case MeetingRecorderState.Stopping:
                builder.SetContentText("Saving the recording");
                builder.SetUsesChronometer(false);
                builder.SetShowWhen(false);
                break;

            default:
                // The chronometer counts from "when": now less what was recorded, so a resume
                // carries on from the time it paused at.
                var elapsed = session?.Elapsed ?? TimeSpan.Zero;
                builder.SetWhen(DateTimeOffset.UtcNow.Subtract(elapsed).ToUnixTimeMilliseconds());
                builder.SetUsesChronometer(true);
                builder.SetShowWhen(true);

                // Another app has the microphone: the recording runs, and hears nothing.
                if (session?.Notice is { } notice) builder.SetContentText(notice);

                builder.AddAction(0, "Pause", ActionIntent(PauseAction));
                builder.AddAction(0, "Stop", ActionIntent(StopAction));
                break;
        }

        return builder.Build() ?? throw new InvalidOperationException("Android built no notification.");
    }

    /// <summary>"Recording · &lt;note title&gt;" when the note has a title, else "Recording".</summary>
    private static string Title(NoteRecordingSession? session)
    {
        if (session?.NoteId is { } noteId
            && Services()?.GetService<NoteViewProjection>()?.Find(noteId)?.Title is { } title
            && !string.IsNullOrWhiteSpace(title))
        {
            return $"Recording · {title}";
        }

        return "Recording";
    }

    private PendingIntent OpenIntent()
    {
        var intent = new Intent(this, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop);
        return PendingIntent.GetActivity(this, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    private PendingIntent ActionIntent(string action)
    {
        // A request code per action: Android matches pending intents by their action too, but a
        // distinct code keeps each button's intent its own even if a flag ever drops the action.
        var intent = new Intent(this, typeof(RecordingService)).SetAction(action);
        return PendingIntent.GetService(this, RequestCode(action), intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    private static int RequestCode(string action) => action switch
    {
        PauseAction => 1,
        ResumeAction => 2,
        _ => 3,
    };

    private void EnsureChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;

        // Low: an ongoing timer, never a sound or a heads-up. Creating a channel that exists
        // changes nothing the person set.
        var channel = new NotificationChannel(ChannelId, "Recording", NotificationImportance.Low)
        {
            Description = "Shows while Backlog records a meeting into a note."
        };

        (GetSystemService(NotificationService) as NotificationManager)?.CreateNotificationChannel(channel);
    }

    private static async Task Drive(Task action)
    {
        try
        {
            await action;
        }
        catch (Exception ex)
        {
            // The session puts ordinary failures into words itself; this is the rest.
            Warn("A recording action from the notification failed: {Error}", ex.GetType().Name);
        }
    }

    private static void Warn(string message, string error) =>
        Services()?.GetService<ILogger<RecordingService>>()?.LogWarning(message, error);

    private static IServiceProvider? Services() => IPlatformApplication.Current?.Services;
}
