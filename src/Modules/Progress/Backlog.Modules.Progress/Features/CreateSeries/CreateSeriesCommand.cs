using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.DomainModels;
using Backlog.Modules.Progress.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Features.CreateSeries;

/// <summary>Sets up a sprint series over a set of repositories
/// (<c>.devbook/domain/progress/features.md#setting-up-a-sprint-series</c>).</summary>
public sealed record CreateSeriesCommand(CreateSprintSeries Request);

public sealed class CreateSeriesCommandHandler(ISprintSeriesRepository repository, ProgressGate gate, SeriesSaved saved, TimeProvider clock)
    : ICommandHandler<CreateSeriesCommand, Result<SprintSeriesDto>>
{
    public async Task<Result<SprintSeriesDto>> Handle(CreateSeriesCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;

        var rules = new List<GoalRule>();
        foreach (var rule in request.GoalRules ?? [])
        {
            var made = GoalRule.Create(rule.Kind, rule.Choices);
            if (made.IsFailure) return made.Error;
            rules.Add(made.Value);
        }

        using var held = await gate.EnterAsync(cancellationToken).ConfigureAwait(false);

        var all = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var free = SeriesAssignment.CanCreate(all, request.Repositories ?? []);
        if (free.IsFailure) return free.Error;

        var created = SprintSeries.Create(
            Guid.NewGuid(),
            request.Repositories ?? [],
            request.Name,
            request.Length,
            request.StartWeekday,
            request.NamePattern,
            new BriefVisibility(request.ShowInDailyBrief, request.ShowInWeeklyBrief),
            rules,
            request.GoalShare,
            clock.GetUtcNow());

        if (created.IsFailure) return created.Error;

        await repository.SaveAsync(created.Value, cancellationToken).ConfigureAwait(false);
        held.Dispose(); // Let go first, so nothing that hears the notice waits on this write.
        saved.Raise();
        return created.Value.ToDto();
    }
}
