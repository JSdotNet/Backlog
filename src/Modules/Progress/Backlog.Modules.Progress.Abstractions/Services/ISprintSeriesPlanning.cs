using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Abstractions.Services;

/// <summary>
/// Everything a host may do to sprint series, in one port: set a series up, fill
/// and mark its open sprint, and close it. A plain delegation to the module's
/// use-case handlers. Every call fails with <see cref="ProgressErrors.Off"/> while
/// Progress is switched off; nothing recorded is lost by that.
/// <para>
/// No call changes a task. Adding a task, linking a plan, marking, noting and
/// closing are all recorded in the sprint alone (local ADR 0028).
/// </para>
/// </summary>
public interface ISprintSeriesPlanning
{
    /// <summary>Every series, in the order they were created.</summary>
    Task<Result<IReadOnlyList<SprintSeriesDto>>> ListSeriesAsync(CancellationToken cancellationToken = default);

    Task<Result<SprintSeriesDto>> GetSeriesAsync(Guid seriesId, CancellationToken cancellationToken = default);

    /// <summary>The series a repository belongs to, or null when it belongs to none
    /// — which is the case the In progress view stays unchanged for.</summary>
    Task<Result<SprintSeriesDto?>> FindSeriesOfAsync(string repositoryAlias, CancellationToken cancellationToken = default);

    Task<Result<SprintSeriesDto>> CreateSeriesAsync(CreateSprintSeries request, CancellationToken cancellationToken = default);

    Task<Result<SprintSeriesDto>> AddRepositoryAsync(Guid seriesId, string repositoryAlias, CancellationToken cancellationToken = default);

    Task<Result<SprintSeriesDto>> RemoveRepositoryAsync(Guid seriesId, string repositoryAlias, CancellationToken cancellationToken = default);

    /// <summary>A changed length or weekday applies from the next sprint; the open one
    /// keeps its window.</summary>
    Task<Result<SprintSeriesDto>> ChangeCadenceAsync(
        Guid seriesId,
        SprintLength length,
        DayOfWeek startWeekday,
        CancellationToken cancellationToken = default);

    Task<Result<SprintSeriesDto>> RenameAsync(
        Guid seriesId,
        string name,
        string namePattern,
        CancellationToken cancellationToken = default);

    Task<Result<SprintSeriesDto>> ShowInBriefsAsync(
        Guid seriesId,
        bool daily,
        bool weekly,
        CancellationToken cancellationToken = default);

    Task<Result<SprintSeriesDto>> SetGoalRulesAsync(
        Guid seriesId,
        IReadOnlyList<GoalRuleDto> rules,
        int goalShare,
        CancellationToken cancellationToken = default);

    /// <summary>Starts a sprint on <paramref name="startOn"/>, which has to be the
    /// series' start weekday — and, after an earlier sprint, the first one after its
    /// last day.</summary>
    Task<Result<SprintSeriesDto>> StartSprintAsync(
        Guid seriesId,
        DateOnly startOn,
        string goal,
        CancellationToken cancellationToken = default);

    Task<Result<SprintSeriesDto>> SetSprintGoalAsync(Guid seriesId, int number, string goal, CancellationToken cancellationToken = default);

    Task<Result<SprintSeriesDto>> AddTaskAsync(Guid seriesId, int number, Guid taskId, CancellationToken cancellationToken = default);

    Task<Result<SprintSeriesDto>> RemoveTaskAsync(Guid seriesId, int number, Guid taskId, CancellationToken cancellationToken = default);

    Task<Result<SprintSeriesDto>> LinkPlanAsync(Guid seriesId, int number, string planTag, CancellationToken cancellationToken = default);

    Task<Result<SprintSeriesDto>> UnlinkPlanAsync(Guid seriesId, int number, string planTag, CancellationToken cancellationToken = default);

    Task<Result<SprintSeriesDto>> MarkGoalAsync(Guid seriesId, int number, Guid taskId, bool isGoal, CancellationToken cancellationToken = default);

    /// <summary>A one-line note, or null to clear it.</summary>
    Task<Result<SprintSeriesDto>> SetNoteAsync(Guid seriesId, int number, Guid taskId, string? note, CancellationToken cancellationToken = default);

    Task<Result<SprintSeriesDto>> SetOpenQuestionAsync(Guid seriesId, int number, Guid taskId, bool openQuestion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the open sprint and starts the next one in the same change, numbered one
    /// higher, from the first start weekday after the closed sprint's last day, with
    /// <paramref name="nextGoal"/> and every carried task as a directly added member.
    /// Refused until every unfinished member has an outcome in
    /// <paramref name="outcomes"/>.
    /// </summary>
    Task<Result<SprintSeriesDto>> CloseSprintAsync(
        Guid seriesId,
        IReadOnlyDictionary<Guid, CloseOutcome> outcomes,
        string nextGoal,
        CancellationToken cancellationToken = default);
}

/// <summary>What a new series starts with. Everything but the repositories has the
/// devbook's default: two-week sprints from a Monday, named <c>Sprint {n}</c>, shown in
/// neither brief, with every task in the sprint counting toward the goal and the goal
/// met when every goal task is done.</summary>
public sealed record CreateSprintSeries(
    IReadOnlyList<string> Repositories,
    string? Name = null,
    SprintLength Length = SprintLength.TwoWeeks,
    DayOfWeek StartWeekday = DayOfWeek.Monday,
    string NamePattern = "Sprint {n}",
    bool ShowInDailyBrief = false,
    bool ShowInWeeklyBrief = false,
    IReadOnlyList<GoalRuleDto>? GoalRules = null,
    int GoalShare = 100);
