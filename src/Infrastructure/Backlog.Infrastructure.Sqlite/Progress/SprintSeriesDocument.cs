using System.Text.Json;
using System.Text.Json.Serialization;

using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.DomainModels;

namespace Backlog.Infrastructure.Sqlite.Progress;

/// <summary>
/// The stored shape of one <see cref="SprintSeries"/>: one JSON document per row of
/// <c>progress_series</c>. A document per series rather than a row per sprint or item,
/// because the series is the consistency boundary — closing a sprint changes two sprints
/// at once — and because a document per series is what travels between devices once
/// sprints sync. Dates are written as <c>yyyy-MM-dd</c>, and the enums by name, so the
/// stored text reads the same on every machine.
/// </summary>
internal sealed record SprintSeriesDocument
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>The only shape written so far.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public List<string> Repositories { get; init; } = [];

    public SprintLength Length { get; init; } = SprintLength.TwoWeeks;

    public DayOfWeek StartWeekday { get; init; } = DayOfWeek.Monday;

    public string NamePattern { get; init; } = Modules.Progress.DomainModels.NamePattern.Default.Value;

    public bool ShowInDailyBrief { get; init; }

    public bool ShowInWeeklyBrief { get; init; }

    public List<GoalRuleDocument> GoalRules { get; init; } = [];

    public int GoalShare { get; init; } = 100;

    public List<SprintDocument> Sprints { get; init; } = [];

    /// <summary>Whether the series is switched on. A document written before the
    /// switch existed has no such field and reads as on, which is what every series
    /// was then — so the shape is still <see cref="CurrentVersion"/>.</summary>
    public bool IsOn { get; init; } = true;

    public static SprintSeriesDocument From(SprintSeries series) =>
        new()
        {
            Id = series.Id,
            Name = series.Name,
            Repositories = [.. series.Repositories],
            Length = series.Length,
            StartWeekday = series.StartWeekday,
            NamePattern = series.NamePattern.Value,
            ShowInDailyBrief = series.Briefs.Daily,
            ShowInWeeklyBrief = series.Briefs.Weekly,
            GoalRules = [.. series.GoalRules.Select(rule => new GoalRuleDocument(rule.Kind, [.. rule.Choices]))],
            GoalShare = series.Threshold.Share,
            Sprints = [.. series.Sprints.Select(SprintDocument.From)],
            IsOn = series.IsOn
        };

    public SprintSeries ToSeries() =>
        SprintSeries.Restore(
            Id,
            Name,
            Repositories ?? [],
            Length,
            StartWeekday,
            NamePattern,
            new BriefVisibility(ShowInDailyBrief, ShowInWeeklyBrief),
            (GoalRules ?? []).Select(rule => (rule.Kind, (IReadOnlyList<string>)(rule.Choices ?? []))),
            GoalShare,
            (Sprints ?? []).Select(sprint => sprint.ToSprint()),
            IsOn);
}

internal sealed record GoalRuleDocument(GoalRuleKind Kind, List<string>? Choices);

internal sealed record SprintDocument
{
    public int Number { get; init; }

    public DateOnly Start { get; init; }

    public DateOnly End { get; init; }

    public string Goal { get; init; } = string.Empty;

    public bool IsOpen { get; init; }

    public List<PlanLinkDocument> PlanLinks { get; init; } = [];

    public List<SprintItemDocument> Items { get; init; } = [];

    public static SprintDocument From(Sprint sprint) =>
        new()
        {
            Number = sprint.Number,
            Start = sprint.Window.Start,
            End = sprint.Window.End,
            Goal = sprint.Goal,
            IsOpen = sprint.IsOpen,
            PlanLinks = [.. sprint.PlanLinks.Select(link => new PlanLinkDocument(link.Tag, link.LinkedOn))],
            Items = [.. sprint.Items.Select(SprintItemDocument.From)]
        };

    public Sprint ToSprint() =>
        Sprint.Restore(
            Number,
            Start,
            End,
            Goal,
            IsOpen,
            (PlanLinks ?? []).Select(link => PlanLink.Restore(link.Tag, link.LinkedOn)),
            (Items ?? []).Select(item => item.ToItem()));
}

internal sealed record PlanLinkDocument(string Tag, DateOnly LinkedOn);

internal sealed record SprintItemDocument
{
    public Guid TaskId { get; init; }

    public bool AddedDirectly { get; init; }

    public DateOnly? AddedOn { get; init; }

    public bool IsGoal { get; init; }

    public string? Note { get; init; }

    public bool OpenQuestion { get; init; }

    public bool KeptAtClose { get; init; }

    public CloseOutcome? Outcome { get; init; }

    public static SprintItemDocument From(SprintItem item) =>
        new()
        {
            TaskId = item.TaskId,
            AddedDirectly = item.AddedDirectly,
            AddedOn = item.AddedOn,
            IsGoal = item.IsGoal,
            Note = item.Note,
            OpenQuestion = item.OpenQuestion,
            KeptAtClose = item.KeptAtClose,
            Outcome = item.Outcome
        };

    public SprintItem ToItem() =>
        SprintItem.Restore(TaskId, AddedDirectly, AddedOn, IsGoal, Note, OpenQuestion, KeptAtClose, Outcome);
}
