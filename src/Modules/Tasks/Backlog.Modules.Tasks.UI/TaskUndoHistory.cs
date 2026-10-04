namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// One change the reader can take back with Ctrl+Z, as the list saw it happen.
/// <para>
/// Snapshots rather than inverse operations, because every edit on this pane is
/// already a whole-entry text save (ADR 0002): the text before and the text after
/// are the whole of what a field change, a sub-item tick or a typing session did.
/// </para>
/// </summary>
internal abstract record TaskUndoStep(string Label);

/// <summary>An existing entry's text went from <paramref name="Before"/> to
/// <paramref name="After"/>. <paramref name="Open"/> while a typing session is
/// still saving into it on the debounce, so the next keystroke's save joins this
/// step rather than becoming one of its own.</summary>
internal sealed record TaskTextUndoStep(string Label, Guid Id, string Before, string After, bool Open)
    : TaskUndoStep(Label);

/// <summary>An entry came into being with <paramref name="Text"/>. Undone by
/// deleting it; redone by saving that text again at <paramref name="Index"/>, which
/// is a new entry. <paramref name="Open"/> as for <see cref="TaskTextUndoStep"/>:
/// a draft's first debounced save creates it, and the typing that follows is part
/// of the same creation.</summary>
internal sealed record TaskCreatedUndoStep(string Label, Guid Id, string Text, bool Open, int Index = -1)
    : TaskUndoStep(Label);

/// <summary>An entry holding <paramref name="Text"/> left the list from
/// <paramref name="Index"/>. Undone by saving that text as a new entry in the same
/// place — the module deliberately has no undelete (<c>TaskItem.MarkDeleted</c>).</summary>
internal sealed record TaskDeletedUndoStep(string Label, Guid Id, string Text, int Index) : TaskUndoStep(Label);

/// <summary>The same entries were put in a different order.</summary>
internal sealed record TaskOrderUndoStep(string Label, IReadOnlyList<Guid> Before, IReadOnlyList<Guid> After)
    : TaskUndoStep(Label);

/// <summary>Several changes one gesture made — a bulk edit, a drop, a text split
/// into two entries — taken back together.</summary>
internal sealed record TaskGroupUndoStep(string Label, IReadOnlyList<TaskUndoStep> Steps) : TaskUndoStep(Label);

/// <summary>
/// The session's undo and redo stacks for the Tasks pane
/// (<c>.devbook/design/interaction-guidelines.md#undo-and-history</c>).
/// <para>
/// Memory only and bounded: session-level undo is what the guideline asks as a
/// minimum, and a history that outlived the session would be undoing against a
/// store other devices have written since.
/// </para>
/// <para>
/// A recreated entry gets a new id, so the steps recorded against the old one
/// are reached through <see cref="Resolve"/> rather than rewritten.
/// </para>
/// </summary>
internal sealed class TaskUndoHistory
{
    public const int Capacity = 100;

    private readonly LinkedList<TaskUndoStep> _undo = new();
    private readonly Stack<TaskUndoStep> _redo = new();
    private readonly Dictionary<Guid, Guid> _aliases = [];
    private List<TaskUndoStep>? _group;
    private string? _groupLabel;
    private int _groupDepth;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Collects every step recorded until the returned scope ends into one step.
    /// Nested scopes join the outermost, so a bulk delete that runs the single-row
    /// delete N times is still one step.
    /// </summary>
    public IDisposable Group(string label)
    {
        if (_groupDepth++ == 0)
        {
            _group = [];
            _groupLabel = label;
        }

        return new GroupScope(this);
    }

    public void Record(TaskUndoStep step)
    {
        if (_group is not null)
        {
            _group.Add(step);
            return;
        }

        Push(step);
    }

    /// <summary>
    /// Records an existing entry's text change, joining the open typing step for
    /// the same entry when there is one on top. <paramref name="open"/> false
    /// closes it: the flush that ends a typing session is the last save it owns.
    /// </summary>
    public void RecordText(string label, Guid id, string before, string after, bool open)
    {
        if (_group is null && _undo.Last?.Value is TaskCreatedUndoStep { Open: true } created && created.Id == id)
        {
            _undo.Last.Value = created with { Text = after, Open = open };
            _redo.Clear();
            return;
        }

        if (string.Equals(before, after, StringComparison.Ordinal)) return;

        if (_group is null && _undo.Last?.Value is TaskTextUndoStep { Open: true } top && top.Id == id)
        {
            _undo.RemoveLast();
            var joined = top with { After = after, Open = open };
            if (!string.Equals(joined.Before, joined.After, StringComparison.Ordinal)) _undo.AddLast(joined);
            _redo.Clear();
            return;
        }

        Record(new TaskTextUndoStep(label, id, before, after, open));
    }

    /// <summary>Closes the open typing step, so the next edit to the same entry
    /// is a step of its own.</summary>
    public void Seal()
    {
        switch (_undo.Last?.Value)
        {
            case TaskTextUndoStep { Open: true } text:
                _undo.Last.Value = text with { Open = false };
                break;
            case TaskCreatedUndoStep { Open: true } created:
                _undo.Last.Value = created with { Open = false };
                break;
        }
    }

    public TaskUndoStep? TakeUndo()
    {
        Seal();
        if (_undo.Last?.Value is not { } step) return null;

        _undo.RemoveLast();
        return step;
    }

    public TaskUndoStep? TakeRedo() => _redo.TryPop(out var step) ? step : null;

    /// <summary>Puts a step that was just undone where redo finds it.</summary>
    public void Undone(TaskUndoStep step) => _redo.Push(step);

    /// <summary>Puts a step that was just redone back on the undo stack, without
    /// clearing what is still left to redo.</summary>
    public void Redone(TaskUndoStep step)
    {
        _undo.AddLast(step);
        Trim();
    }

    /// <summary>The id an entry recorded as <paramref name="id"/> goes by now.</summary>
    public Guid Resolve(Guid id)
    {
        while (_aliases.TryGetValue(id, out var next)) id = next;
        return id;
    }

    public void Alias(Guid from, Guid to)
    {
        if (from != to) _aliases[Resolve(from)] = to;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _aliases.Clear();
    }

    private void Push(TaskUndoStep step)
    {
        Seal();
        _undo.AddLast(step);
        _redo.Clear();
        Trim();
    }

    private void Trim()
    {
        while (_undo.Count > Capacity) _undo.RemoveFirst();
    }

    private void EndGroup()
    {
        if (--_groupDepth > 0) return;

        var steps = _group!;
        var label = _groupLabel!;
        _group = null;
        _groupLabel = null;

        switch (steps.Count)
        {
            case 0:
                return;
            case 1:
                Push(steps[0]);
                return;
            default:
                Push(new TaskGroupUndoStep(label, steps));
                return;
        }
    }

    private sealed class GroupScope(TaskUndoHistory history) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            history.EndGroup();
        }
    }
}
