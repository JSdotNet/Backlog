using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Modules.Tasks.Services;

/// <summary>
/// A task as the text it is — what <see cref="EntryTextParser.ToRawText"/> composes,
/// metadata line and sub-items included. For a reader outside the module that holds
/// the aggregate and not its published shape: the change journal, which shows a
/// task's text before a change set beside its text now (local ADR 0061).
/// </summary>
public static class TaskEntryText
{
    public static string Of(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return EntryTextParser.ToRawText(task.ToDto());
    }
}
