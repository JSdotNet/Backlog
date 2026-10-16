using System.Collections.Concurrent;

using Android.App;
using Android.Content;
using Android.Content.PM;

using Backlog.Mobile.UI.AppUpdate;

namespace Backlog.Mobile.Services;

/// <summary>
/// Where Android's package installer reports on a session
/// <see cref="AndroidApkInstaller"/> committed.
/// </summary>
/// <remarks>
/// <para>
/// Android creates a fresh receiver for every broadcast, so the waiting install is
/// found by session id in a static table rather than through an instance.
/// </para>
/// <para>
/// The first report is usually <c>STATUS_PENDING_USER_ACTION</c>: Android wants the
/// person to confirm, and hands over the activity that asks them. With Backlog on
/// screen it is started at once, over the app. With Backlog in the background - the
/// person switched away while the APK downloaded - it is held until
/// <see cref="MainActivity"/> resumes, because Android 10 and later block an activity
/// started from the background, and the prompt would silently never appear. The final
/// report - a refusal or a cancel - completes the install; a success never arrives,
/// because Android ends this process to replace the app.
/// </para>
/// <para>
/// Not exported: only the installer, through the explicit intent this app gave it,
/// can reach it.
/// </para>
/// </remarks>
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class ApkInstallStatusReceiver : BroadcastReceiver
{
    private static readonly ConcurrentDictionary<int, TaskCompletionSource<(int Status, string? Message)>> Waiting = new();
    private static readonly Lock Gate = new();

    // Both only touched under Gate. The activity is held only while it is resumed.
    private static Activity? _resumed;
    private static (int SessionId, Intent Confirm)? _deferred;

    /// <summary>The final status of session <paramref name="sessionId"/>, once Android sends it.</summary>
    internal static Task<(int Status, string? Message)> Expect(int sessionId)
    {
        var outcome = new TaskCompletionSource<(int Status, string? Message)>(TaskCreationOptions.RunContinuationsAsynchronously);
        Waiting[sessionId] = outcome;
        return outcome.Task;
    }

    /// <summary>Stops waiting for <paramref name="sessionId"/>, and drops its held confirmation.</summary>
    internal static void Forget(int sessionId)
    {
        Waiting.TryRemove(sessionId, out _);

        lock (Gate)
        {
            if (_deferred?.SessionId == sessionId) _deferred = null;
        }
    }

    /// <summary>Called by <see cref="MainActivity.OnResume"/>: the app is on screen, so a
    /// confirmation held while it was not is shown now.</summary>
    internal static void ActivityResumed(Activity activity)
    {
        Intent? confirm;
        lock (Gate)
        {
            _resumed = activity;
            confirm = _deferred?.Confirm;
            _deferred = null;
        }

        if (confirm is not null) activity.StartActivity(confirm);
    }

    /// <summary>Called by <see cref="MainActivity.OnPause"/>.</summary>
    internal static void ActivityPaused(Activity activity)
    {
        lock (Gate)
        {
            if (ReferenceEquals(_resumed, activity)) _resumed = null;
        }
    }

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null) return;

        var sessionId = intent.GetIntExtra(PackageInstaller.ExtraSessionId, -1);
        var status = intent.GetIntExtra(PackageInstaller.ExtraStatus, ApkInstallStatus.Failure);
        var message = intent.GetStringExtra(PackageInstaller.ExtraStatusMessage);

        if (status == ApkInstallStatus.PendingUserAction)
        {
            if (ConfirmationOf(intent) is { } confirm)
            {
                AskToConfirm(sessionId, confirm);
                return;
            }

            status = ApkInstallStatus.Failure;
            message ??= "Android asked for a confirmation it did not provide.";
        }

        if (Waiting.TryRemove(sessionId, out var outcome))
        {
            outcome.TrySetResult((status, message));
        }
    }

    private static void AskToConfirm(int sessionId, Intent confirm)
    {
        Activity? resumed;
        lock (Gate)
        {
            resumed = _resumed;
            if (resumed is null) _deferred = (sessionId, confirm);
        }

        resumed?.StartActivity(confirm);
    }

    private static Intent? ConfirmationOf(Intent intent)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return intent.GetParcelableExtra(Intent.ExtraIntent, Java.Lang.Class.FromType(typeof(Intent))) as Intent;
        }

#pragma warning disable CA1422 // The typed overload above is API 33+; this is the only one before it.
        return intent.GetParcelableExtra(Intent.ExtraIntent) as Intent;
#pragma warning restore CA1422
    }
}
