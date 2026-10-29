using Backlog.UI.Components.Brain;
using Backlog.UI.Components.Devbook;
using Microsoft.AspNetCore.Components;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// What the Tasks views draw beside the list that only the window's shell holds: the
/// Brain notes that name a task, the scenario screenshots an entry's body names, and a
/// task's sessions and pull requests (local ADR 0040 §3).
/// <para>
/// One per window, with the window's view state. The main window's shell fills it when
/// it opens, and the Tasks, Board and Calendar surfaces read it as they draw; a window
/// whose shell fills nothing draws the list without them, as a host that composes none
/// of them does.
/// </para>
/// </summary>
public sealed class TasksViewWiring
{
    /// <summary>The Brain notes that name a task, by its id; null draws no Brain badge.</summary>
    public Func<string, IReadOnlyList<BrainNoteLink>>? BrainNotesFor { get; set; }

    /// <summary>The scenario screenshots of an entry's repository; null draws them as
    /// plain links.</summary>
    public Func<string?, IDevbookImageSource?>? DevbookImagesFor { get; set; }

    /// <summary>A task's "Sessions and pull requests" section; null draws none.</summary>
    public RenderFragment<EntryRow>? LinkedWork { get; set; }
}
