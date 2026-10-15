using Backlog.Modules.Progress.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.DomainModels;

/// <summary>
/// One time box of a series (<c>.devbook/domain/progress/domain.md#sprint</c>). An
/// entity the <see cref="SprintSeries"/> owns: every change goes through the series,
/// and a closed sprint refuses all of them (<c>progress.sprint_closed</c>).
/// </summary>
public sealed class Sprint
{
    private readonly List<PlanLink> _planLinks;
    private readonly List<SprintItem> _items;

    private Sprint(int number, SprintWindow window, string goal, bool isOpen, List<PlanLink> planLinks, List<SprintItem> items)
    {
        Number = number;
        Window = window;
        Goal = goal;
        IsOpen = isOpen;
        _planLinks = planLinks;
        _items = items;
    }

    public int Number { get; }

    public SprintWindow Window { get; }

    public string Goal { get; private set; }

    public bool IsOpen { get; private set; }

    public IReadOnlyList<PlanLink> PlanLinks => _planLinks;

    public IReadOnlyList<SprintItem> Items => _items;

    internal static Sprint Open(int number, SprintWindow window, string? goal, IEnumerable<SprintItem>? items = null) =>
        new(number, window, goal?.Trim() ?? string.Empty, isOpen: true, [], [.. items ?? []]);

    /// <summary>Rehydration from storage.</summary>
    public static Sprint Restore(
        int number,
        DateOnly start,
        DateOnly end,
        string? goal,
        bool isOpen,
        IEnumerable<PlanLink> planLinks,
        IEnumerable<SprintItem> items) =>
        new(number, SprintWindow.Restore(start, end), goal ?? string.Empty, isOpen, [.. planLinks], [.. items]);

    public SprintItem? ItemFor(Guid taskId) => _items.FirstOrDefault(item => item.TaskId == taskId);

    internal Result SetGoal(string? goal)
    {
        if (!IsOpen) return Closed();

        Goal = goal?.Trim() ?? string.Empty;
        return Result.Success();
    }

    internal Result AddTask(Guid taskId, DateOnly today)
    {
        if (!IsOpen) return Closed();

        var item = ItemFor(taskId);
        if (item is null) _items.Add(SprintItem.Direct(taskId, today));
        else item.MarkAddedDirectly(today);

        return Result.Success();
    }

    /// <summary>Takes a directly added task out. While a linked plan still brings it in
    /// (<paramref name="broughtByPlan"/>) it stays a member and keeps what is recorded on
    /// it; otherwise its membership ends and its item goes whole — goal mark, note and
    /// open question with it — so adding it again starts clean.</summary>
    internal Result RemoveTask(Guid taskId, bool broughtByPlan = false)
    {
        if (!IsOpen) return Closed();

        var item = ItemFor(taskId);
        if (item is null || !item.AddedDirectly) return Result.Failure(ProgressErrors.NotAnItem(taskId));

        item.ClearAddedDirectly();
        if (!broughtByPlan || item.IsEmpty) _items.Remove(item);

        return Result.Success();
    }

    internal Result LinkPlan(string? planTag, DateOnly today)
    {
        if (!IsOpen) return Closed();

        var link = PlanLink.Create(planTag, today);
        if (link.IsFailure) return Result.Failure(link.Error);

        if (!_planLinks.Any(existing => PlanTags.Same(existing.Tag, link.Value.Tag))) _planLinks.Add(link.Value);

        return Result.Success();
    }

    internal Result UnlinkPlan(string? planTag)
    {
        if (!IsOpen) return Closed();

        _planLinks.RemoveAll(existing => PlanTags.Same(existing.Tag, planTag));
        return Result.Success();
    }

    /// <summary>Marks, notes and questions are recorded on an item, which a task only a
    /// plan brought in gets the first time one is set.</summary>
    internal Result Record(Guid taskId, Func<SprintItem, Result> change)
    {
        if (!IsOpen) return Closed();

        var item = ItemFor(taskId);
        var added = item is null;
        item ??= SprintItem.Recorded(taskId);

        var changed = change(item);
        if (changed.IsFailure) return changed;

        if (added && !item.IsEmpty) _items.Add(item);
        else if (!added && item.IsEmpty) _items.Remove(item);

        return Result.Success();
    }

    /// <summary>
    /// Closes the sprint. <paramref name="members"/> are the task ids that were members
    /// at closing; each gets an item, so what the sprint held stays its record after the
    /// plans move on. Every one in <paramref name="unfinished"/> must have an outcome.
    /// </summary>
    internal Result Close(
        IReadOnlyCollection<Guid> members,
        IReadOnlyCollection<Guid> unfinished,
        IReadOnlyDictionary<Guid, CloseOutcome> outcomes)
    {
        if (!IsOpen) return Closed();

        var unsettled = unfinished
            .Where(id => !outcomes.TryGetValue(id, out var outcome) || !Enum.IsDefined(outcome))
            .ToList();

        if (unsettled.Count > 0) return Result.Failure(ProgressErrors.UnsettledItems(unsettled));

        foreach (var taskId in members)
        {
            var item = ItemFor(taskId);
            if (item is null)
            {
                item = SprintItem.Recorded(taskId);
                _items.Add(item);
            }

            item.KeepAtClose(unfinished.Contains(taskId) ? outcomes[taskId] : null);
        }

        IsOpen = false;
        return Result.Success();
    }

    private Result Closed() => Result.Failure(ProgressErrors.SprintClosed(Number));
}

/// <summary>A whole plan linked to a sprint by its plan tag
/// (<c>.devbook/domain/progress/domain.md#plan-link</c>), stored without the sigil.
/// Equality is by the tag; the day it was linked says which members joined after the
/// start.</summary>
public sealed record PlanLink
{
    private PlanLink(string tag, DateOnly linkedOn)
    {
        Tag = tag;
        LinkedOn = linkedOn;
    }

    public string Tag { get; }

    public DateOnly LinkedOn { get; }

    public static Result<PlanLink> Create(string? tag, DateOnly linkedOn)
    {
        var bare = PlanTags.Bare(tag);
        return bare.Length == 0 ? ProgressErrors.PlanTagRequired() : new PlanLink(bare, linkedOn);
    }

    public static PlanLink Restore(string tag, DateOnly linkedOn) => new(tag, linkedOn);

    public bool Equals(PlanLink? other) => other is not null && PlanTags.Same(Tag, other.Tag);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Tag);
}
