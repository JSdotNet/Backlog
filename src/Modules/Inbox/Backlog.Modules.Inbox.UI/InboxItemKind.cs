namespace Backlog.Desktop.UI.Inbox;

/// <summary>
/// What kind of content an inbox item is.
/// <para>
/// This is a different question from where it came from. A YouTube link arrives
/// from the YouTube monitor, from the phone's share sheet, or pasted by hand,
/// and it is a video in every case — so the kind is a fact about the content
/// and <see cref="InboxSource"/> is a fact about the delivery. The values are
/// the content shapes the capture sources in <c>.domain/capture/domain.md</c>
/// actually produce, named the way a reader sorting a queue would name them.
/// </para>
/// </summary>
public enum InboxItemKind
{
    /// <summary>A plain note: the kind every source can produce and the one an
    /// item is until somebody says otherwise.</summary>
    Text,

    /// <summary>A web page worth reading: a clipped or linked article.</summary>
    Article,

    /// <summary>A bare URL that is not (yet) known to be an article.</summary>
    Link,

    /// <summary>A video, from the YouTube monitor or a shared link.</summary>
    YouTube,

    /// <summary>A picture or screenshot.</summary>
    Image,

    /// <summary>A file: a PDF, an office document, an archive.</summary>
    Document,

    /// <summary>A newsletter or mail ingested from an IMAP inbox.</summary>
    Email,

    /// <summary>A code selection from an IDE-class host, or a fenced
    /// snippet.</summary>
    Code,

    /// <summary>A dictated memo from the mobile app.</summary>
    Voice,

    /// <summary>An artifact a Claude session produced and shared.</summary>
    ClaudeArtifact
}

/// <summary>
/// The kind as the shared library's <c>CaptureKindMarker</c> spells it, and as a
/// reader reads it. Kept beside the enum so the two never drift: the marker
/// draws slugs, the pane prints labels, and this is the one place both are
/// written down.
/// </summary>
public static class InboxItemKinds
{
    /// <summary>The library's slug for the kind — the value
    /// <c>CaptureKindMarker</c> draws.</summary>
    public static string Slug(InboxItemKind kind) => kind switch
    {
        InboxItemKind.Text => "text",
        InboxItemKind.Article => "article",
        InboxItemKind.Link => "link",
        InboxItemKind.YouTube => "youtube",
        InboxItemKind.Image => "image",
        InboxItemKind.Document => "document",
        InboxItemKind.Email => "email",
        InboxItemKind.Code => "code",
        InboxItemKind.Voice => "voice",
        InboxItemKind.ClaudeArtifact => "claude-artifact",
        _ => "text"
    };

    /// <summary>The kind as a reader reads it beside the mark. Colour and shape
    /// are never the only carriers, so the word is always printed.</summary>
    public static string Label(InboxItemKind kind) => kind switch
    {
        InboxItemKind.Text => "Text",
        InboxItemKind.Article => "Article",
        InboxItemKind.Link => "Link",
        InboxItemKind.YouTube => "YouTube",
        InboxItemKind.Image => "Image",
        InboxItemKind.Document => "Document",
        InboxItemKind.Email => "Email",
        InboxItemKind.Code => "Code",
        InboxItemKind.Voice => "Voice memo",
        InboxItemKind.ClaudeArtifact => "Claude artifact",
        _ => "Text"
    };

    /// <summary>Whether the kind is reference material rather than a thought of
    /// the reader's own — the distinction PARA files under Resources.</summary>
    public static bool IsReference(InboxItemKind kind) => kind is
        InboxItemKind.Article
        or InboxItemKind.Link
        or InboxItemKind.YouTube
        or InboxItemKind.Image
        or InboxItemKind.Document
        or InboxItemKind.Email
        or InboxItemKind.ClaudeArtifact;
}
