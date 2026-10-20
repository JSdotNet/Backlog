using Backlog.UI.Components.Brain;

using Microsoft.AspNetCore.Components;

namespace Backlog.UI.Components.Devbook;

/// <summary>
/// What the Domain view knows about each chapter beyond the chapter itself: the
/// owner's Brain notes that link to it, and the tasks whose Devbook references name
/// it (local ADR 0026 §3, ADR 0027 §6). The host reads both once and cascades this
/// to every page, which looks a chapter up by its reference.
/// <para>
/// Pure data plus the host's ways to draw a From your Brain box and to open a note
/// or a task, so the library's pages never read a Brain or a backlog. Unset — the
/// storybook, a test, a host with no Brain — every page draws as it did without it.
/// </para>
/// </summary>
public sealed class DomainChapterLinks
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<BrainNoteLink>> NoNotes =
        new Dictionary<string, IReadOnlyList<BrainNoteLink>>();

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<DomainTaskLink>> NoTasks =
        new Dictionary<string, IReadOnlyList<DomainTaskLink>>();

    /// <summary>No notes, no tasks, no box.</summary>
    public static DomainChapterLinks None { get; } = new();

    /// <summary>The Brain notes linking to each chapter, by <see cref="Key"/>, each
    /// note once per chapter.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<BrainNoteLink>> Notes { get; init; } = NoNotes;

    /// <summary>The tasks naming each chapter, by <see cref="Key"/>, each once per
    /// chapter.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<DomainTaskLink>> Tasks { get; init; } = NoTasks;

    /// <summary>The host's From your Brain box for the chapters a unit, feature or
    /// term covers, by their references. Drawn only where one of them has a
    /// note.</summary>
    public RenderFragment<IReadOnlyList<string>>? NotesBox { get; init; }

    /// <summary>Raised with a note a badge opens.</summary>
    public EventCallback<BrainNoteLink> OnOpenNote { get; init; }

    /// <summary>Raised with the id of a task a chip opens.</summary>
    public EventCallback<string> OnOpenTask { get; init; }

    /// <summary>
    /// A chapter's reference in the one spelling both sides compare: beneath the
    /// domain folder, lower case — <c>tasks/domain.md#task</c> for
    /// <c>.devbook/domain/tasks/domain.md#task</c> and <c>.domain/tasks/domain.md#Task</c>.
    /// </summary>
    public static string Key(string reference) => DomainUnits.Normalize(reference);

    /// <summary>The notes linking to the one chapter <paramref name="reference"/> names.</summary>
    public IReadOnlyList<BrainNoteLink> NotesOn(string? reference) =>
        reference is { Length: > 0 } && Notes.TryGetValue(Key(reference), out var notes) ? notes : [];

    /// <summary>The notes linking to any of <paramref name="references"/>, each once,
    /// in the order the chapters are given.</summary>
    public IReadOnlyList<BrainNoteLink> NotesOn(IEnumerable<string> references) =>
        [.. references.SelectMany(NotesOn).DistinctBy(note => note.Path, StringComparer.OrdinalIgnoreCase)];

    /// <summary>The tasks naming any of <paramref name="references"/>, each once.</summary>
    public IReadOnlyList<DomainTaskLink> TasksOn(IEnumerable<string> references) =>
        [.. references
            .SelectMany(reference => Tasks.TryGetValue(Key(reference), out var tasks) ? tasks : [])
            .DistinctBy(task => task.TaskId, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// The chapter <paramref name="reference"/> names and every chapter under it,
    /// as references — what a unit or a feature covers: a note linking to an
    /// aggregate's entity, or a task naming a feature's sub-feature, belongs to
    /// the unit or feature it sits in.
    /// </summary>
    public static IReadOnlyList<string> Covering(string reference, DomainChapter chapter)
    {
        var hash = reference.IndexOf('#');
        var path = hash < 0 ? reference : reference[..hash];
        return [reference, .. chapter.Descendants().Where(child => child.Anchor.Length > 0).Select(child => $"{path}#{child.Anchor}")];
    }
}

/// <summary>A task whose Devbook references name a chapter.</summary>
/// <param name="TaskId">The task's id.</param>
/// <param name="Title">Its title in this backlog, or null when this backlog does
/// not hold it — drawn as a dashed chip reading <em>not in this backlog</em>.</param>
public sealed record DomainTaskLink(string TaskId, string? Title);
