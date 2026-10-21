using Backlog.Modules.Devbook.Abstractions;
using Backlog.UI.Components.Brain;
using Backlog.UI.Components.Devbook;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// One task's Devbook references, as the shell hands them to the Domain view: the
/// task's id and the chapters it points at, as stored on the task.
/// </summary>
/// <param name="TaskId">The task's id.</param>
/// <param name="References">Its Devbook references as written —
/// <c>.devbook/domain/tasks/features.md#planning</c>.</param>
public sealed record DevbookTaskReferences(string TaskId, IReadOnlyList<string> References);

/// <summary>
/// The Domain view's Brain and task links, read into the per-chapter maps the
/// library's <see cref="DomainChapterLinks"/> carries (local ADR 0026 §3, ADR 0027
/// §6). Pure, so the grouping is tested without a Brain or a backlog.
/// </summary>
public static class DomainViewLinks
{
    /// <summary>
    /// The Brain notes linking to each heading, by <see cref="DomainChapterLinks.Key"/>,
    /// each note once per heading in the order the links came. A link to a whole
    /// file names no unit, feature or term, and is left to the box under the page.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<BrainBacklink>> NotesByChapter(IEnumerable<BrainChapterBacklink> links)
    {
        var byChapter = new Dictionary<string, List<BrainBacklink>>(StringComparer.Ordinal);
        foreach (var link in links)
        {
            if (link.Slug is not { Length: > 0 } slug) continue;

            var key = DomainChapterLinks.Key($"{link.ChapterPath}#{slug}");
            if (!byChapter.TryGetValue(key, out var notes)) byChapter[key] = notes = [];

            var known = notes.FindIndex(note => string.Equals(note.NotePath, link.Note.NotePath, StringComparison.OrdinalIgnoreCase));
            if (known < 0) notes.Add(link.Note);
            else if (notes[known].Heading is not null && link.Note.Heading is null) notes[known] = link.Note;
        }

        return byChapter.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<BrainBacklink>)pair.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// The tasks naming each chapter, by <see cref="DomainChapterLinks.Key"/>, each
    /// task once per chapter, titled by <paramref name="titleFor"/> — the shell's
    /// answer for a task this backlog holds, null for one it does not.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<DomainTaskLink>> TasksByChapter(
        IEnumerable<DevbookTaskReferences> tasks,
        Func<string, string?>? titleFor)
    {
        var byChapter = new Dictionary<string, List<DomainTaskLink>>(StringComparer.Ordinal);
        foreach (var task in tasks)
        {
            DomainTaskLink? link = null;
            foreach (var reference in task.References)
            {
                if (string.IsNullOrWhiteSpace(reference) || !reference.Contains('#', StringComparison.Ordinal)) continue;

                var key = DomainChapterLinks.Key(reference);
                if (!byChapter.TryGetValue(key, out var named)) byChapter[key] = named = [];
                if (named.Exists(known => string.Equals(known.TaskId, task.TaskId, StringComparison.OrdinalIgnoreCase))) continue;

                link ??= new DomainTaskLink(task.TaskId, titleFor?.Invoke(task.TaskId));
                named.Add(link);
            }
        }

        return byChapter.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<DomainTaskLink>)pair.Value, StringComparer.Ordinal);
    }

    /// <summary>The notes as a badge lists them: path and title.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<BrainNoteLink>> NoteLinks(IReadOnlyDictionary<string, IReadOnlyList<BrainBacklink>> notes) =>
        notes.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<BrainNoteLink>)[.. pair.Value.Select(note => new BrainNoteLink(note.NotePath, note.Title))],
            StringComparer.Ordinal);

    /// <summary>The notes linking to any of <paramref name="references"/>, each once,
    /// in the order the chapters are given — what one From your Brain box lists.</summary>
    public static IReadOnlyList<BrainBacklink> NotesOn(IReadOnlyDictionary<string, IReadOnlyList<BrainBacklink>> notes, IEnumerable<string> references) =>
        [.. references
            .SelectMany(reference => notes.TryGetValue(DomainChapterLinks.Key(reference), out var linked) ? linked : [])
            .DistinctBy(note => note.NotePath, StringComparer.OrdinalIgnoreCase)];
}
