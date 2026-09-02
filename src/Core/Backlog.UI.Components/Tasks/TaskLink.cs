namespace Backlog.UI.Components.Tasks;

/// <summary>
/// A row was dragged by its link handle and dropped on another row. Two ids and
/// nothing else, exactly as <see cref="TaskMove"/> is: the list knows what the
/// reader did, and what a dependency means and where it is written down is the
/// host's.
/// </summary>
/// <param name="Id">The row that was picked up — the one that now waits.</param>
/// <param name="DependsOnId">The row it was dropped on, which is what it now
/// waits for. Named for the direction of the fact rather than "target", because
/// "target" is ambiguous the moment a list has two drags in it: a reorder drag
/// targets a position and a link drag targets a predecessor.</param>
/// <remarks>
/// Deliberately no <c>ApplyTo</c>. <see cref="TaskMove"/> offers one because a
/// host with no opinion about ordering still has a list to reorder; there is no
/// equivalent here. A chain is data the host owns — the ids live in the entry's
/// own text, and the library ships no dependency editor — so a helper that
/// rewrote <see cref="TaskRow.DependsOn"/> would hand back rows the host has to
/// throw away before saving what it actually keeps.
/// </remarks>
public sealed record TaskLink(string Id, string DependsOnId);

/// <summary>
/// What part a row is playing in the link drag currently in flight.
/// <para>
/// Every row in the list gets one of these while a link drag is on, because the
/// whole row is the drop target: there is no sub-region to aim at, so the row
/// itself has to say whether releasing on it will do anything. Nothing at all
/// during a reorder drag, and nothing at rest.
/// </para>
/// <para>
/// Which of these a row is depends only on the two rows and never on where the
/// pointer is — that is <c>TaskItem.LinkArmed</c>, a second and deliberately
/// separate axis. A row can be refusing and be the row under the pointer, and it
/// then has to say the same "no" more loudly rather than a different one.
/// </para>
/// </summary>
public enum TaskLinkRole
{
    /// <summary>No link drag in flight, or nobody listening for one. The row is
    /// the row it was before any of this existed.</summary>
    None,

    /// <summary>The row that was picked up: the one that will end up waiting.
    /// It is the payload rather than a place to drop anything, and it is the one
    /// row a link drag cannot land on.</summary>
    Source,

    /// <summary>Release here and the source row waits for this one.</summary>
    Target,

    /// <summary>Present, and it will not take the drop — it is finished, or the
    /// source row already waits for it. Refusing rather than falling back to a
    /// reorder: a reader who aimed a link at a row must not get a move out of
    /// it.</summary>
    Refused
}
