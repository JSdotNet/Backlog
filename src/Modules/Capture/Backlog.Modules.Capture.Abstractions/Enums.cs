namespace Backlog.Modules.Capture.Abstractions;

/// <summary>
/// Where a capture came from — the <c>Capture Source</c> enum of
/// <c>.domain/capture/domain.md#capture-source</c>, in full.
/// <para>
/// All seven are here even though only three can be monitored, because the
/// vocabulary is the domain's and a subset would be a second vocabulary. Which
/// ones a person can point at something and wait on is
/// <see cref="CaptureSourceKinds.Monitorable"/>'s answer, not this type's.
/// </para>
/// </summary>
public enum CaptureSourceKind
{
    /// <summary>Quick entry from the mobile app.</summary>
    Mobile,

    /// <summary>A new video on a channel the reader watches.</summary>
    YouTube,

    /// <summary>A change on a page the reader watches.</summary>
    Website,

    /// <summary>A newsletter or summary from a mailbox.</summary>
    Email,

    /// <summary>Content clipped from the browser.</summary>
    WebClipper,

    /// <summary>Content captured from an IDE-class host.</summary>
    Ide,

    /// <summary>Typed straight into the Inbox.</summary>
    Manual
}

/// <summary>
/// The spellings the vocabulary is written in, and the subset a person can
/// switch on.
/// <para>
/// The slugs are the domain's lower-case spelling and they are also what the
/// Inbox pane's <c>InboxSource.Channel</c> carries. That pane holds a raw string
/// by design — Inbox may not reference this project — so the two are kept the
/// same by a test rather than by a type, and the badge on an Inbox row is only
/// right while they agree.
/// </para>
/// </summary>
public static class CaptureSourceKinds
{
    /// <summary>The sources that poll: something the reader points at and
    /// waits on. The rest arrive on their own — a share sheet, a clipper, an
    /// IDE — and have nothing to watch.</summary>
    public static IReadOnlyList<CaptureSourceKind> Monitorable { get; } =
        [CaptureSourceKind.YouTube, CaptureSourceKind.Website, CaptureSourceKind.Email];

    /// <summary>The kind in the domain's own lower-case spelling — what the
    /// settings file and the Inbox badge both carry.</summary>
    public static string Slug(CaptureSourceKind kind) => kind switch
    {
        CaptureSourceKind.Mobile => "mobile",
        CaptureSourceKind.YouTube => "youtube",
        CaptureSourceKind.Website => "website",
        CaptureSourceKind.Email => "email",
        CaptureSourceKind.WebClipper => "web_clipper",
        CaptureSourceKind.Ide => "ide",
        CaptureSourceKind.Manual => "manual",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a capture source.")
    };

    /// <summary>The kind as a reader reads it — the same words the Inbox prints
    /// beside its source badge.</summary>
    public static string Label(CaptureSourceKind kind) => kind switch
    {
        CaptureSourceKind.Mobile => "Mobile",
        CaptureSourceKind.YouTube => "YouTube",
        CaptureSourceKind.Website => "Website",
        CaptureSourceKind.Email => "Email",
        CaptureSourceKind.WebClipper => "Web clipper",
        CaptureSourceKind.Ide => "IDE",
        CaptureSourceKind.Manual => "Manual",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a capture source.")
    };

    /// <summary>Reads a slug back. False for anything that is not one, so a
    /// hand-edited file can be dropped rather than guessed at.</summary>
    public static bool TryParse(string? slug, out CaptureSourceKind kind)
    {
        foreach (var candidate in Enum.GetValues<CaptureSourceKind>())
        {
            if (string.Equals(Slug(candidate), slug, StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;
                return true;
            }
        }

        kind = default;
        return false;
    }
}
