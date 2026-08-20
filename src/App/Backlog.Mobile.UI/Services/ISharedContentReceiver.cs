namespace Backlog.Mobile.UI.Services;

/// <summary>
/// Something another app handed to Backlog: a link, a note, a video.
/// </summary>
/// <remarks>
/// Android sends a share as two loose extras — the text and an optional subject
/// — and neither is a draft on its own. YouTube is the case that makes this
/// worth a type: it puts the video title in the subject and the link in the
/// text, so a screen that read only one of them would show a bare URL or a title
/// with nothing to open. <see cref="Draft"/> is the one line of text a share is
/// worth, decided here rather than in each screen that shows one.
/// </remarks>
/// <param name="Text">The shared text, trimmed. Usually a link.</param>
/// <param name="Subject">The sending app's title for it, or <c>null</c> when it sent none.</param>
public sealed record SharedContent(string Text, string? Subject)
{
    /// <summary>A share as it arrives: either half may be absent, blank, or padded.</summary>
    public static SharedContent From(string? text, string? subject) =>
        new((text ?? string.Empty).Trim(), string.IsNullOrWhiteSpace(subject) ? null : subject.Trim());

    /// <summary>
    /// What the quick-capture field should read.
    /// </summary>
    /// <remarks>
    /// The two halves are joined with a space rather than a newline because the
    /// field is a single-line <c>input</c>: a newline in its value is dropped by
    /// the browser, which would silently lose the title or the link.
    /// </remarks>
    public string Draft
    {
        get
        {
            if (Subject is null) return Text;
            if (Text.Length == 0) return Subject;

            // Some apps put the same words in both extras. Repeating them would
            // read as a mistake the person then has to edit out.
            return string.Equals(Subject, Text, StringComparison.Ordinal) ? Text : $"{Subject} {Text}";
        }
    }

    /// <summary>Whether this share carries nothing a screen could show.</summary>
    public bool IsEmpty => Draft.Length == 0;
}

/// <summary>
/// Content shared into the app from elsewhere, as the screens see it.
/// </summary>
/// <remarks>
/// <para>
/// There are two implementations behind this and they are not interchangeable at
/// runtime: an Android <c>ACTION_SEND</c> intent cannot be delivered to a
/// browser, and the browser harness has no intents to receive. Each host
/// registers the one it can honour, so the Inbox screen is the same code in the
/// emulator, on a device, and under Playwright.
/// </para>
/// <para>
/// Delivery is push-based <em>and</em> buffered, and the buffer is the reason
/// this is not a plain event. A share <em>launches</em> the Android activity: the
/// intent is read in <c>OnCreate</c>, long before the <c>BlazorWebView</c> has
/// built the scoped component that wants it. An event with no subscriber yet
/// would drop precisely the payload the person just shared, so
/// <see cref="Subscribe"/> replays the last unconsumed one instead.
/// </para>
/// </remarks>
public interface ISharedContentReceiver
{
    /// <summary>
    /// Asks to be told about shares, starting with one that is already waiting.
    /// </summary>
    /// <remarks>
    /// The callback runs on whichever thread published — the Android main thread,
    /// or the caller's own thread during the replay — so a component marshals to
    /// its renderer before touching state.
    /// </remarks>
    /// <param name="onShared">Called once per share, and once immediately when a
    /// payload was buffered before this subscription existed.</param>
    /// <returns>Disposed to stop listening.</returns>
    IDisposable Subscribe(Action<SharedContent> onShared);
}
