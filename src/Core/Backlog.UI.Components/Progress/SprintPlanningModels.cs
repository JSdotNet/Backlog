using Backlog.UI.Components.Menus;

namespace Backlog.UI.Components.Progress;

/// <summary>An act chosen from a sprint row's menu: the row it was chosen on and the
/// menu item, whose <see cref="MenuItem.Id"/> the host gave it.</summary>
public sealed record SprintRowMenuChoice(SprintBoardRow Row, MenuItem Item);

/// <summary>What a choice in the "Add to sprint" picker is: one task, or a whole plan.</summary>
public enum SprintAddKind
{
    Task,
    Plan
}

/// <summary>
/// One thing the "Add to sprint" picker offers
/// (<c>.devbook/domain/progress/features.md#filling-a-sprint</c>), in plain values: the
/// library carries no domain, so the host lists what can be added and hears back the
/// <see cref="Id"/> of what was chosen.
/// </summary>
/// <param name="Id">The host's key for it: a task's id, or a plan's tag without the sigil.</param>
/// <param name="Label">What it reads: the task's title, or <c>+tag</c>.</param>
/// <param name="Detail">One quiet line under it — its repository and status, or how many
/// tasks a plan holds.</param>
/// <param name="Repositories">The series' repositories it belongs to, by alias: what the
/// picker's repository filter narrows on.</param>
/// <param name="InSprint">Already in the sprint: listed, but not offered again.</param>
public sealed record SprintAddChoice(
    SprintAddKind Kind,
    string Id,
    string Label,
    string? Detail,
    IReadOnlyList<string> Repositories,
    bool InSprint = false);

/// <summary>What a person records on one sprint item: a one-line note, null for none,
/// and whether it is an open question.</summary>
public sealed record SprintItemNote(string? Note, bool OpenQuestion);

/// <summary>How a task is in the open sprint, as its side panel says it.</summary>
public enum SprintMembership
{
    /// <summary>Not a member.</summary>
    Outside,

    /// <summary>Added by a person.</summary>
    Added,

    /// <summary>Brought in by a linked plan alone.</summary>
    ThroughPlan,

    /// <summary>Added by a person, and its plan is linked too.</summary>
    AddedAndThroughPlan
}

/// <summary>
/// A task's place in the open sprint of its series, as the task's side panel draws it
/// (<c>.devbook/domain/progress/features.md#planning-from-a-task</c>).
/// </summary>
/// <param name="SeriesName">The series the task's repository works to.</param>
/// <param name="SprintName">The open sprint's name; null when the series has no open sprint.</param>
/// <param name="Dates">The open sprint's window in words.</param>
/// <param name="PlanTag">The plan the task is filed under, without the sigil, or null.</param>
/// <param name="PlanLinked">Whether <paramref name="PlanTag"/> is linked to the sprint.</param>
public sealed record SprintTaskPanelModel(
    string SeriesName,
    string? SprintName,
    string? Dates,
    SprintMembership Membership,
    bool IsGoal,
    string? Note,
    bool OpenQuestion,
    string? PlanTag,
    bool PlanLinked)
{
    public bool IsMember => Membership is not SprintMembership.Outside;

    public bool AddedDirectly => Membership is SprintMembership.Added or SprintMembership.AddedAndThroughPlan;

    /// <summary>One line on how the task stands in the sprint.</summary>
    public string Line => SprintName is null
        ? $"{SeriesName} has no open sprint."
        : Membership switch
        {
            SprintMembership.Added => $"In {SprintName}, added directly.",
            SprintMembership.ThroughPlan => $"In {SprintName} through +{PlanTag}.",
            SprintMembership.AddedAndThroughPlan => $"In {SprintName}, added directly and through +{PlanTag}.",
            _ => $"Not in {SprintName}."
        };
}

/// <summary>The picker's narrowing, kept apart from the dialog so it is tested on its
/// own: the repository filter, then the search over label and detail.</summary>
public static class SprintAddRules
{
    public static IReadOnlyList<SprintAddChoice> Filter(
        IEnumerable<SprintAddChoice> choices,
        SprintAddKind kind,
        string? repository,
        string? search)
    {
        ArgumentNullException.ThrowIfNull(choices);

        var words = (search ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return
        [
            .. choices.Where(choice => choice.Kind == kind
                && (repository is null || choice.Repositories.Contains(repository, StringComparer.OrdinalIgnoreCase))
                && words.All(word => choice.Label.Contains(word, StringComparison.OrdinalIgnoreCase)
                    || (choice.Detail?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false)))
        ];
    }
}
