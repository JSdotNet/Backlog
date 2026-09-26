namespace Backlog.Mobile.UI.TalkNotes;

/// <summary>
/// The talk note being written, held outside the page.
/// <para>
/// The Router remounts a page on every navigation, so a note kept in the Note
/// page's own fields would be gone after a glance at the Inbox — and a talk does
/// not pause while someone looks something up. Scoped, like the Inbox's
/// <see cref="Services.CaptureDraft"/>: it lives as long as the app's circuit or
/// web view, which on a phone includes time spent in the background. The picked
/// files themselves are staged on disk by <see cref="TalkNoteFiles"/>; this holds
/// where they are.
/// </para>
/// </summary>
public sealed class TalkNoteDraft
{
    public string Title { get; set; } = string.Empty;

    /// <summary>Markdown. Dictation appends here.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>The speaker as typed — <c>@name</c>, the @ optional.</summary>
    public string Speaker { get; set; } = string.Empty;

    /// <summary>Tags as typed, separated by commas or spaces, # optional.</summary>
    public string Tags { get; set; } = string.Empty;

    /// <summary>Keep this note's pictures as taken rather than downscaled.</summary>
    public bool SendOriginals { get; set; }

    public List<DraftAttachment> Attachments { get; } = [];

    /// <summary>The outbox entry of the note saved last, which the page keeps
    /// reporting on — waiting, uploading, synced — after the fields clear.</summary>
    public Guid? LastSaved { get; set; }

    /// <summary>The speaker as the capture carries it: one name without its @,
    /// or null.</summary>
    public string? Person => Speaker.Trim().TrimStart('@') is { Length: > 0 } name ? name : null;

    /// <summary>Why the speaker cannot be sent, or null. The desktop reads a
    /// person back as one <c>@name</c> token, so it is one word.</summary>
    public string? SpeakerProblem => Person is { } name && name.Any(char.IsWhiteSpace)
        ? "A speaker is one name with no spaces, like @ada."
        : null;

    /// <summary>The tags, bare and each once, in the order typed.</summary>
    public IReadOnlyList<string> TagList =>
        [.. Tags.Split([',', ' ', ';', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tag => tag.TrimStart('#'))
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The files that will go, in the order they were picked.</summary>
    public IReadOnlyList<DraftAttachment> Accepted => [.. Attachments.Where(attachment => attachment.Refusal is null)];

    public bool HasRefused => Attachments.Any(attachment => attachment.Refusal is not null);

    /// <summary>Anything worth saving.</summary>
    public bool HasContent =>
        Title.Trim().Length > 0 || Body.Trim().Length > 0 || Accepted.Count > 0;

    /// <summary>Saving is withheld while a refused file is still on the strip:
    /// the person thinks it is attached, and it would not go.</summary>
    public bool CanSave => HasContent && !HasRefused && SpeakerProblem is null;

    /// <summary>Empties the fields for the next note. <see cref="LastSaved"/>
    /// stays; so does the originals switch, which is a habit rather than content.</summary>
    public void Clear()
    {
        Title = string.Empty;
        Body = string.Empty;
        Speaker = string.Empty;
        Tags = string.Empty;
        Attachments.Clear();
    }
}

/// <summary>
/// One picked file on the strip.
/// </summary>
/// <param name="Id">The attachment's id — minted at the pick, sent as the
/// upload's id and named by the capture.</param>
/// <param name="StagedPath">Where its bytes wait on the device, or empty for a
/// refused file, which is never copied.</param>
/// <param name="Refusal">Why it will not be sent, or null.</param>
public sealed record DraftAttachment(
    Guid Id,
    string Name,
    string ContentType,
    long SizeBytes,
    string StagedPath,
    string? Refusal = null)
{
    public bool IsPicture => AttachmentRules.IsPicture(ContentType);
}
