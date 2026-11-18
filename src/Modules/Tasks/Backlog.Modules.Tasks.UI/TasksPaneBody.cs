namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// The little of the Tasks pane a body drawn under its filter bar needs (local ADR 0055 §3):
/// the title a row reads while the open panel is still drafting a rename, opening an entry
/// the way a row does, and a hook the pane's focus return asks to put the focus back on the
/// open entry. Everything else — the filtered rows, the selection and the writes — is the
/// window's <see cref="TasksDesktopState"/>, which a body injects as the pane does.
/// <para>
/// Built once per pane, and handed to <see cref="TasksPane.Body"/>. The Calendar's Tasks
/// layer is the one body so far.
/// </para>
/// </summary>
public sealed class TasksPaneBody
{
    private readonly Func<EntryRow, string> _displayTitle;
    private readonly Func<string, Task> _open;

    internal TasksPaneBody(Func<EntryRow, string> displayTitle, Func<string, Task> open)
    {
        _displayTitle = displayTitle;
        _open = open;
    }

    /// <summary>What the pane calls the focus hook the body gave, if any.</summary>
    internal Func<Task>? FocusSelected { get; private set; }

    /// <summary>What <paramref name="row"/> is called, live: the open panel's draft while
    /// that row is the one being retitled there, its saved title the rest of the time, and
    /// "Untitled" when that comes out blank.</summary>
    public string DisplayTitleOf(EntryRow row) => _displayTitle(row);

    /// <summary>Opens the entry <paramref name="taskId"/> names in the pane's detail
    /// panel, revealing it first when a filter hides it — what pressing its row does.</summary>
    public Task OpenAsync(string taskId) => _open(taskId);

    /// <summary>Gives the pane the way to put the focus back on the open entry — after
    /// Escape, Shift+Tab off the panel, or opening a hidden entry — in place of the list's
    /// or the Board's. Null takes it back, as a body does when it goes.</summary>
    public void FocusSelectedWith(Func<Task>? focus) => FocusSelected = focus;
}
