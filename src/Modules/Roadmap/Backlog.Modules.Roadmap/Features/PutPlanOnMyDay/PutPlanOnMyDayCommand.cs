using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.DomainModels;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Features.PutPlanOnMyDay;

/// <summary>
/// Puts a plan on My Day, or takes it off, by a My Day date on its Roadmap Items (local
/// ADR 0062 §8). The plan is named by its tag, with or without the <c>+</c> the entries
/// wear. The day is the caller's — today, as the person sees it — so the command stays a
/// plain record of what was said; whether the plan is in a given day's My Day is read
/// later, by the task rule.
/// </summary>
/// <param name="Tag">The plan tag, <c>+</c> optional.</param>
/// <param name="On">The day picked for, or null to take the plan off My Day.</param>
public sealed record PutPlanOnMyDayCommand(string Tag, DateOnly? On);

public sealed class PutPlanOnMyDayCommandHandler(IRoadmapPlanRepository plans, RoadmapPlanGate gate)
    : ICommandHandler<PutPlanOnMyDayCommand, Result<IReadOnlyList<RoadmapItemDto>>>
{
    public async Task<Result<IReadOnlyList<RoadmapItemDto>>> Handle(
        PutPlanOnMyDayCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tag = PlanningTag.Of(command.Tag.TrimStart('+'));

        using var held = await gate.EnterAsync(cancellationToken);
        var plan = await plans.LoadAsync(cancellationToken);
        var before = plan.ItemsTagged(tag).Select(item => item.MyDayOn).ToList();
        var put = plan.PutPlanOnMyDay(tag, command.On);
        if (put is not Ok<IReadOnlyList<RoadmapItem>>(var items)) return (Error)put.Value!;

        // Nothing moved — the plan was already on that day, or taken off one it was on
        // none of — so nothing is written: the whole document syncs on every save.
        var changed = items.Count != before.Count || items.Where((item, index) => item.MyDayOn != before[index]).Any();
        if (changed) await plans.SaveAsync(plan, cancellationToken);

        return Result.Success<IReadOnlyList<RoadmapItemDto>>([.. items.Select(item => item.ToDto())]);
    }
}
