// The namespace deliberately does not match the folder, for the reason
// FeedbackReporter.cs beside this file gives.
namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// What a caller wants the Report issue dialog to open with. Both halves are
/// starting text the reader can still edit before anything is filed.
/// </summary>
public sealed record FeedbackReportDraft(string Title, string Details);

/// <summary>
/// The one way to open the Report issue dialog from somewhere other than its
/// own button.
/// <para>
/// The dialog lives in <c>FeedbackReportButton</c>, which the footer mounts
/// once, and until now the only thing that could open it was that button. The
/// app's error screen needs to open it too — with the exception already written
/// into it — and it sits inside the routed body, two components away from the
/// footer. A channel rather than a cascade or a parameter drilled through the
/// layout, for the same reason <c>ToastChannel</c> is one: a publisher that
/// knows nothing about who is listening, and a listener that does not care who
/// asked.
/// </para>
/// <para>
/// One instance per window: a singleton in the MAUI head, where there is one
/// WebView, and scoped in the harnesses, where every circuit is its own window
/// and a request in one must not open the dialog in another.
/// </para>
/// </summary>
public sealed class FeedbackReportChannel
{
    /// <summary>Raised on the caller's thread with the draft as it was handed
    /// over. The dialog re-enters the renderer itself.</summary>
    public event Action<FeedbackReportDraft>? Requested;

    public void Request(FeedbackReportDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        Requested?.Invoke(draft);
    }
}
