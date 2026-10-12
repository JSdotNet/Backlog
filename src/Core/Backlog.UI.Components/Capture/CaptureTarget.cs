namespace Backlog.UI.Components.Capture;

/// <summary>
/// Where a capture made on the phone's capture sheet goes: the Inbox to be
/// sorted later, a note kept as it is, or a task picked for today.
/// </summary>
public enum CaptureTarget
{
    Inbox,
    Note,
    Today
}

/// <summary>
/// The words the capture sheet says for each <see cref="CaptureTarget"/>, kept
/// in one place so the choice, the hint, the button and the status line can
/// never disagree about what a save does.
/// </summary>
public static class CaptureTargets
{
    /// <summary>The three, in the order the sheet offers them. Inbox first: it
    /// is the default, and the one that asks nothing of the person.</summary>
    public static IReadOnlyList<CaptureTarget> All { get; } = [CaptureTarget.Inbox, CaptureTarget.Note, CaptureTarget.Today];

    /// <summary>The choice's own name: "Inbox", "Note", "Today".</summary>
    public static string Label(CaptureTarget target) => target switch
    {
        CaptureTarget.Note => "Note",
        CaptureTarget.Today => "Today",
        _ => "Inbox"
    };

    /// <summary>What the save button says it will do.</summary>
    public static string SaveLabel(CaptureTarget target) => target switch
    {
        CaptureTarget.Note => "Save note",
        CaptureTarget.Today => "Add to today",
        _ => "Add to inbox"
    };

    /// <summary>What the status line says once a save has landed.</summary>
    public static string SavedLabel(CaptureTarget target) => target switch
    {
        CaptureTarget.Note => "Note saved",
        CaptureTarget.Today => "Added to today",
        _ => "Added to inbox"
    };

    /// <summary>The line over the text field: where this capture will end up.</summary>
    public static string Hint(CaptureTarget target) => target switch
    {
        CaptureTarget.Note => "A note keeps thoughts, not to-dos",
        CaptureTarget.Today => "Lands in Anytime today",
        _ => "Sort it out later — it waits in the inbox"
    };

    /// <summary>The empty field's prompt.</summary>
    public static string Placeholder(CaptureTarget target) => target switch
    {
        CaptureTarget.Note => "Start writing…",
        CaptureTarget.Today => "What needs doing today?",
        _ => "What is on your mind?"
    };

    /// <summary>The status line after a save: "Added to inbox — capture another".</summary>
    public static string Status(CaptureTarget saved) => $"{SavedLabel(saved)} — capture another";

    /// <summary>The lower-case token a test id or a CSS modifier carries.</summary>
    public static string Slug(CaptureTarget target) => target.ToString().ToLowerInvariant();
}
