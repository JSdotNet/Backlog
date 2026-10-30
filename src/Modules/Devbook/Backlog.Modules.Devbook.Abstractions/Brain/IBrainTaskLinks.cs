namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// The links between Brain notes and tasks, both ways (local ADR 0026 §3,
/// <c>.devbook/domain/brain/features.md#bi-directional-linking</c>): the tasks a
/// note's <c>tasks</c> field names, and the notes that name one task. Read from the
/// Brain's own database in the owner's app storage, like <see cref="IBrainBacklinks"/>,
/// and never written anywhere — a task's record carries no note, so nothing
/// personal reaches the backlog or another machine's view of it.
/// <para>
/// With no Brain set, or no database built for it yet, every answer is empty — the
/// same answer as a Brain that links to no task.
/// </para>
/// </summary>
public interface IBrainTaskLinks
{
    /// <summary>
    /// The tasks the note at <paramref name="notePath"/> names, as
    /// <see cref="BrainTaskLink"/>s in the order the note writes them, each once.
    /// The path is the note's path from the Brain's root.
    /// </summary>
    Task<IReadOnlyList<BrainTaskLink>> TasksOfAsync(string notePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every task some note names, by its id in <see cref="BrainTaskLink.Normalize"/>'s
    /// spelling, with the notes naming it in title order. A value that is no task id
    /// links no task and is left out.
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<BrainTaskNote>>> NotesByTaskAsync(CancellationToken cancellationToken = default);
}

/// <summary>One Brain note that names a task in its <c>tasks</c> field.</summary>
/// <param name="NotePath">The note's path in the Brain — <c>projects/plan.md</c>.</param>
/// <param name="Title">The note's title.</param>
public sealed record BrainTaskNote(string NotePath, string Title);

/// <summary>
/// One value of a Brain note's <c>tasks</c> field (a
/// <c>Task Link</c>, <c>.devbook/domain/brain/domain.md#task-link</c>). The value is
/// the task's stored id, a GUID; <see cref="TaskId"/> is it in one spelling, or null
/// when the value is no GUID — still listed, so the reader sees what they wrote.
/// </summary>
/// <param name="Raw">The value as the note writes it.</param>
/// <param name="TaskId">The task id, lower-case and hyphenated, or null.</param>
public sealed record BrainTaskLink(string Raw, string? TaskId)
{
    /// <summary>A value as a link: trimmed of the quotes and blanks a YAML list
    /// may leave, and its id normalized.</summary>
    public static BrainTaskLink? Parse(string? value)
    {
        var raw = value?.Trim().Trim('"', '\'').Trim();
        return string.IsNullOrEmpty(raw) ? null : new BrainTaskLink(raw, Normalize(raw));
    }

    /// <summary>
    /// A task id in the one spelling both sides compare: the GUID's <c>D</c> form,
    /// lower-case. Braces and upper case are the same id; anything that is no GUID
    /// is null.
    /// </summary>
    public static string? Normalize(string? value) =>
        Guid.TryParse(value?.Trim().Trim('"', '\''), out var id) && id != Guid.Empty ? id.ToString("D") : null;
}
