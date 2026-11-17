namespace Backlog.Modules.Brain.Abstractions;

/// <summary>
/// Picks work up from a note in the Brain
/// (<c>.devbook/domain/brain/features.md#brain-mode-of-the-devbook-pane</c>): a
/// draft task made from the note or from one of its lines, or a plan drafted from a
/// project note. Either way the work is made first and the link second, through
/// <see cref="IBrainNoteLinkWriter"/> (local ADR 0052): a note whose top
/// <c>meta</c> block does not parse keeps its task or plan, and the answer says the
/// link was not written.
/// <para>
/// Availability is a property, as the Inbox's plan drafter's is, because the pane
/// reads it on every render: each action stays on screen, disabled, with its reason
/// as its title. A host that composes no pickup offers neither action.
/// </para>
/// </summary>
public interface IBrainPickup
{
    /// <summary>Why <see cref="CreateTaskAsync"/> cannot run here, in words a person
    /// can act on — no Brain is set, or no backlog is composed; null when it can.</summary>
    string? CreateTaskUnavailableReason { get; }

    /// <summary>Why <see cref="PlanProjectAsync"/> cannot run here — no Brain, no
    /// backlog, or no plan drafter configured; null when it can.</summary>
    string? PlanUnavailableReason { get; }

    /// <summary>
    /// Makes a draft task from the note at <paramref name="notePath"/> (relative to
    /// the Brain's root), titled <paramref name="line"/> when one is given and the
    /// note's title otherwise. The task is filed in the Brain scope — no repository —
    /// or once in each repository the note's <c>repos</c> field names. Each task
    /// carries <c>#brain:&lt;path&gt;</c> in its Devbook references, and its id is
    /// appended to the note's <c>tasks</c>.
    /// </summary>
    Task<BrainPickupResult> CreateTaskAsync(string notePath, string? line = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the plan drafter Create plan uses for a plan about the project note at
    /// <paramref name="notePath"/>, with the note and the Brain notes its
    /// <c>related</c> field names as input, imports it under a fresh Roadmap Tag with a
    /// <c>plan</c> entry whose Knowledge Ref is the note, and appends the tag to the
    /// note's <c>roadmap</c>.
    /// </summary>
    Task<BrainPickupResult> PlanProjectAsync(string notePath, CancellationToken cancellationToken = default);
}

/// <summary>How a pickup ended.</summary>
public enum BrainPickupOutcome
{
    /// <summary>The work was made and the note links to it.</summary>
    Linked,

    /// <summary>The work was made, but the link was not written into the note —
    /// its <c>meta</c> block did not parse, say. The work stays.</summary>
    NotLinked,

    /// <summary>Nothing was made.</summary>
    Failed,
}

/// <summary>
/// What a pickup did: the tasks it made, the Roadmap Tag of the plan it imported, and
/// one sentence for the person.
/// </summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="TaskIds">The tasks made, in order; empty when it failed.</param>
/// <param name="RoadmapTag">The plan's Roadmap Tag, bare; null for a task.</param>
/// <param name="Message">What happened, in a person's words — for a link not
/// written, why.</param>
public sealed record BrainPickupResult(
    BrainPickupOutcome Outcome,
    IReadOnlyList<Guid> TaskIds,
    string? RoadmapTag,
    string Message)
{
    /// <summary>Nothing was made, for <paramref name="message"/>.</summary>
    public static BrainPickupResult Failed(string message) => new(BrainPickupOutcome.Failed, [], null, message);
}

/// <summary>The rules a pickup reads a note by, shared by the pane and the adapter.</summary>
public static class BrainPickupText
{
    /// <summary>
    /// A selected line as a task title: its first line with text, without the list
    /// marker, checkbox, heading marks or quote marker in front, and runs of
    /// whitespace made one space. Null when nothing is left.
    /// </summary>
    public static string? TitleFromLine(string? selection)
    {
        if (string.IsNullOrWhiteSpace(selection)) return null;

        var line = selection
            .Split('\n')
            .Select(part => part.Trim())
            .FirstOrDefault(part => part.Length > 0);
        if (line is null) return null;

        var changed = true;
        while (changed && line.Length > 0)
        {
            changed = false;
            foreach (var prefix in LinePrefixes)
            {
                if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                line = line[prefix.Length..].TrimStart();
                changed = true;
                break;
            }

            if (!changed && line.Length > 1 && char.IsAsciiDigit(line[0]))
            {
                var digits = 0;
                while (digits < line.Length && char.IsAsciiDigit(line[digits])) digits++;
                if (digits < line.Length - 1 && line[digits] is ('.' or ')') && char.IsWhiteSpace(line[digits + 1]))
                {
                    line = line[(digits + 1)..].TrimStart();
                    changed = true;
                }
            }
        }

        var title = string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return title.Length == 0 ? null : title;
    }

    /// <summary>What can stand in front of a line's words, longest first so a
    /// checkbox is taken whole before its list marker is.</summary>
    private static readonly string[] LinePrefixes =
    [
        "- [ ]", "- [x]", "* [ ]", "* [x]", "+ [ ]", "+ [x]",
        "[ ]", "[x]",
        "###### ", "##### ", "#### ", "### ", "## ", "# ",
        "- ", "* ", "+ ", "> ",
    ];

    /// <summary>
    /// Whether <paramref name="notePath"/> is a note under the Brain's projects area:
    /// <paramref name="projectsFolder"/>, or <c>projects</c> when the Brain declares
    /// none — the one note Plan this project is offered on.
    /// </summary>
    public static bool IsProjectNote(string? notePath, string? projectsFolder = null) =>
        notePath is not null
        && BrainNoteDestinations.ProjectFolderFor(
            string.IsNullOrWhiteSpace(projectsFolder) ? BrainNoteDestinations.ProjectsFolder : projectsFolder,
            notePath) is not null;
}
