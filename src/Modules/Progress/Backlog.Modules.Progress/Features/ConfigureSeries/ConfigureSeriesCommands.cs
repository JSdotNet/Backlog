using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;
using Backlog.Modules.Progress.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Features.ConfigureSeries;

/// <summary>Gives a series another repository; refused when another series has it.</summary>
public sealed record AddRepositoryCommand(Guid SeriesId, string RepositoryAlias);

/// <summary>Takes a repository out of a series; refused for its last one.</summary>
public sealed record RemoveRepositoryCommand(Guid SeriesId, string RepositoryAlias);

/// <summary>Chooses the sprint length and start weekday, from the next sprint on.</summary>
public sealed record ChangeCadenceCommand(Guid SeriesId, SprintLength Length, DayOfWeek StartWeekday);

/// <summary>Renames the series and the pattern its sprints are named from.</summary>
public sealed record RenameSeriesCommand(Guid SeriesId, string Name, string NamePattern);

/// <summary>Chooses which briefs show the series' open sprint.</summary>
public sealed record ShowInBriefsCommand(Guid SeriesId, bool Daily, bool Weekly);

/// <summary>Chooses the goal rules and the share of goal tasks that meets the goal.</summary>
public sealed record SetGoalRulesCommand(Guid SeriesId, IReadOnlyList<GoalRuleDto> Rules, int GoalShare);

public sealed class ConfigureSeriesCommandHandler(SeriesChanges changes)
    : ICommandHandler<AddRepositoryCommand, Result<SprintSeriesDto>>,
      ICommandHandler<RemoveRepositoryCommand, Result<SprintSeriesDto>>,
      ICommandHandler<ChangeCadenceCommand, Result<SprintSeriesDto>>,
      ICommandHandler<RenameSeriesCommand, Result<SprintSeriesDto>>,
      ICommandHandler<ShowInBriefsCommand, Result<SprintSeriesDto>>,
      ICommandHandler<SetGoalRulesCommand, Result<SprintSeriesDto>>
{
    public Task<Result<SprintSeriesDto>> Handle(AddRepositoryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return changes.ChangeAcrossAsync(
            command.SeriesId,
            (all, series) => SeriesAssignment.Assign(all, series, command.RepositoryAlias),
            cancellationToken);
    }

    public Task<Result<SprintSeriesDto>> Handle(RemoveRepositoryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return changes.ChangeAsync(command.SeriesId, series => series.RemoveRepository(command.RepositoryAlias), cancellationToken);
    }

    public Task<Result<SprintSeriesDto>> Handle(ChangeCadenceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return changes.ChangeAsync(
            command.SeriesId,
            series => series.ChangeCadence(command.Length, command.StartWeekday),
            cancellationToken);
    }

    public Task<Result<SprintSeriesDto>> Handle(RenameSeriesCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return changes.ChangeAsync(command.SeriesId, series => series.Rename(command.Name, command.NamePattern), cancellationToken);
    }

    public Task<Result<SprintSeriesDto>> Handle(ShowInBriefsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return changes.ChangeAsync(
            command.SeriesId,
            series =>
            {
                series.ShowInBriefs(command.Daily, command.Weekly);
                return Result.Success();
            },
            cancellationToken);
    }

    public Task<Result<SprintSeriesDto>> Handle(SetGoalRulesCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var rules = new List<GoalRule>();
        foreach (var rule in command.Rules ?? [])
        {
            var made = GoalRule.Create(rule.Kind, rule.Choices);
            if (made.IsFailure) return Task.FromResult(Result.Failure<SprintSeriesDto>(made.Error));
            rules.Add(made.Value);
        }

        return changes.ChangeAsync(command.SeriesId, series => series.SetGoalRules(rules, command.GoalShare), cancellationToken);
    }
}
