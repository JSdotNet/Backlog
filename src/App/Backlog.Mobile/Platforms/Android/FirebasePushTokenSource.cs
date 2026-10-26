using IOnCompleteListener = Android.Gms.Tasks.IOnCompleteListener;

using Backlog.Mobile.UI.Notifications;

using Firebase.Messaging;

namespace Backlog.Mobile;

/// <summary>
/// <see cref="IPushTokenSource"/> over Firebase Messaging (local ADR 0039 §4): the token this
/// install receives pushes under. Firebase starts itself from the resources the binding builds
/// out of <c>google-services.json</c>; a build where it could not start throws here, which
/// <see cref="PushRegistration"/> takes as "no token yet".
/// </summary>
public sealed class FirebasePushTokenSource : IPushTokenSource
{
    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        var outcome = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var registration = cancellationToken.Register(() => outcome.TrySetCanceled(cancellationToken));

        // The binding marks getToken "deprecated" and names no replacement; it is Firebase's
        // only way to read the current token.
#pragma warning disable CS0618
        FirebaseMessaging.Instance.GetToken().AddOnCompleteListener(new Completion(outcome));
#pragma warning restore CS0618

        return await outcome.Task;
    }

    /// <summary>Hands the Play Services task's result to the .NET task.</summary>
    private sealed class Completion(TaskCompletionSource<string?> outcome) : Java.Lang.Object, IOnCompleteListener
    {
        public void OnComplete(Android.Gms.Tasks.Task task)
        {
            if (task.IsSuccessful)
            {
                outcome.TrySetResult(task.Result?.ToString());
            }
            else
            {
                outcome.TrySetException(task.Exception is { } failure
                    ? new InvalidOperationException("Firebase Messaging gave no token.", failure)
                    : new InvalidOperationException("Firebase Messaging gave no token."));
            }
        }
    }
}
