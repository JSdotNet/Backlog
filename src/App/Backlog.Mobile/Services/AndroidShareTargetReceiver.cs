using Android.Content;

using Backlog.Mobile.UI.Services;

namespace Backlog.Mobile.Services;

/// <summary>
/// <see cref="ISharedContentReceiver"/> over Android's own share sheet.
/// </summary>
/// <remarks>
/// <para>
/// The platform API is used directly rather than through a wrapper package: the
/// head targets <c>net10.0-android</c> only, so there is no second platform to
/// abstract over, and a new central package version would be carrying a
/// dependency for two <c>GetStringExtra</c> calls.
/// </para>
/// <para>
/// It is registered as a singleton, not a scoped service. The activity is what
/// receives an intent, and it does so both before the <c>BlazorWebView</c> exists
/// and again — through <c>OnNewIntent</c> — while a component from an earlier
/// scope is on screen. A scoped receiver would give the activity nothing to hand
/// the payload to on the first share and the wrong instance on the next one, so
/// the buffer has to outlive the WebView.
/// </para>
/// </remarks>
public sealed class AndroidShareTargetReceiver : BufferedSharedContentReceiver
{
    /// <summary>
    /// Takes what an <c>ACTION_SEND</c> intent carries, if that is what this is.
    /// </summary>
    /// <remarks>
    /// Anything else — the launcher icon, a notification tap — is passed over
    /// rather than treated as an empty share, because the activity forwards every
    /// intent it is given and only some of them are shares.
    /// </remarks>
    public void Receive(Intent? intent)
    {
        if (intent?.Action != Intent.ActionSend) return;

        var content = SharedContent.From(
            intent.GetStringExtra(Intent.ExtraText),
            intent.GetStringExtra(Intent.ExtraSubject));

        if (content.IsEmpty) return;

        // The extras are cleared once read. Android hands the same launch intent
        // back to a recreated activity — a restored process, for instance — and
        // prefilling the field a second time would undo whatever the person had
        // since typed over it.
        intent.RemoveExtra(Intent.ExtraText);
        intent.RemoveExtra(Intent.ExtraSubject);

        Publish(content);
    }
}
