using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;

namespace Backlog.Modules.Progress.Services;

/// <summary>From the aggregate to what a caller receives.</summary>
public static class SprintSeriesMapper
{
    public static SprintSeriesDto ToDto(this SprintSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);

        return new SprintSeriesDto(
            series.Id,
            series.Name,
            [.. series.Repositories],
            series.Length,
            series.StartWeekday,
            series.NamePattern.Value,
            series.Briefs.Daily,
            series.Briefs.Weekly,
            [.. series.GoalRules.Select(ToDto)],
            series.Threshold.Share,
            [.. series.Sprints.Select(sprint => sprint.ToDto(series.NamePattern))],
            series.IsOn,
            series.OffReason);
    }

    public static GoalRuleDto ToDto(GoalRule rule) => new(rule.Kind, [.. rule.Choices]);

    private static SprintDto ToDto(this Sprint sprint, NamePattern pattern) =>
        new(
            sprint.Number,
            pattern.NameOf(sprint.Number),
            sprint.Window.Start,
            sprint.Window.End,
            sprint.Goal,
            sprint.IsOpen,
            [.. sprint.PlanLinks.Select(link => new PlanLinkDto(link.Tag, link.LinkedOn))],
            [.. sprint.Items.Select(item => new SprintItemDto(
                item.TaskId,
                item.AddedDirectly,
                item.AddedOn,
                item.IsGoal,
                item.Note,
                item.OpenQuestion,
                item.Outcome))]);
}
