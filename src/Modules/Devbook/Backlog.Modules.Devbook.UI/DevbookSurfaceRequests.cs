namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The open requests the Devbook surface has followed in one window.
/// <para>
/// A request is a press and is followed once (see
/// <see cref="DevbookPane.OnOpenReferenceFollowed"/>). The surface is mounted
/// afresh every time it comes on screen, and the query it is handed still carries
/// the last press, so what has been followed has to outlive the surface: scoped to
/// the window, as the window's view state is, so hiding the Devbook and showing it
/// again does not jump back to the task's chapter.
/// </para>
/// </summary>
public sealed class DevbookSurfaceRequests
{
    private string? _current;
    private string? _followed;
    private long _sequence;

    /// <summary>The pane's request for <paramref name="chapter"/> asked by the
    /// press <paramref name="request"/>, or null once that press was followed.</summary>
    public DevbookOpenRequest? For(string? chapter, string? request)
    {
        if (string.IsNullOrEmpty(chapter) || string.IsNullOrEmpty(request)) return null;
        if (string.Equals(request, _followed, StringComparison.Ordinal)) return null;

        if (!string.Equals(request, _current, StringComparison.Ordinal))
        {
            _current = request;
            _sequence++;
        }

        return new DevbookOpenRequest(chapter, _sequence);
    }

    /// <summary>The pane followed the request with <paramref name="sequence"/>.</summary>
    public void Followed(long sequence)
    {
        if (sequence == _sequence) _followed = _current;
    }
}
