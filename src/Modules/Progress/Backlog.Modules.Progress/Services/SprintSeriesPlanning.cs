using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.Features.CloseSprint;
using Backlog.Modules.Progress.Features.ConfigureSeries;
using Backlog.Modules.Progress.Features.CreateSeries;
using Backlog.Modules.Progress.Features.FillSprint;
using Backlog.Modules.Progress.Features.GetSeries;
using Backlog.Modules.Progress.Features.MarkSprintItem;
using Backlog.Modules.Progress.Features.ReadSprintProgress;
using Backlog.Modules.Progress.Features.StartSprint;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Services;

/// <summary>
/// The published ports over the module's handlers, behind the
/// <see cref="ProgressFeatures.Progress"/> switch: while it is off every call answers
/// <see cref="ProgressErrors.Off"/> and nothing is read or written. A host that composed
/// no feature settings has Progress off, as it ships.
/// </summary>
public sealed class SprintSeriesPlanning(
    GetSeriesQueryHandler series,
    CreateSeriesCommandHandler create,
    ConfigureSeriesCommandHandler configure,
    StartSprintCommandHandler start,
    FillSprintCommandHandler fill,
    MarkSprintItemCommandHandler mark,
    CloseSprintCommandHandler close,
    ReadSprintProgressQueryHandler progress,
    IAppFeatureSettings? features = null)
    : ISprintSeriesPlanning, ISprintProgress
{
    private bool IsOn => features?.IsEnabled(ProgressFeatures.Progress) ?? false;

    public async Task<Result<IReadOnlyList<SprintSeriesDto>>> ListSeriesAsync(CancellationToken cancellationToken = default) =>
        IsOn
            ? Result.Success(await series.Handle(new ListSeriesQuery(), cancellationToken).ConfigureAwait(false))
            : Result.Failure<IReadOnlyList<SprintSeriesDto>>(ProgressErrors.Off());

    public Task<Result<SprintSeriesDto>> GetSeriesAsync(Guid seriesId, CancellationToken cancellationToken = default) =>
        Gate(() => series.Handle(new GetSeriesQuery(seriesId), cancellationToken));

    public async Task<Result<SprintSeriesDto?>> FindSeriesOfAsync(string repositoryAlias, CancellationToken cancellationToken = default) =>
        IsOn
            ? Result.Success(await series.Handle(new FindSeriesOfQuery(repositoryAlias), cancellationToken).ConfigureAwait(false))
            : Result.Failure<SprintSeriesDto?>(ProgressErrors.Off());

    public Task<Result<SprintSeriesDto>> CreateSeriesAsync(CreateSprintSeries request, CancellationToken cancellationToken = default) =>
        Gate(() => create.Handle(new CreateSeriesCommand(request), cancellationToken));

    public Task<Result<SprintSeriesDto>> AddRepositoryAsync(Guid seriesId, string repositoryAlias, CancellationToken cancellationToken = default) =>
        Gate(() => configure.Handle(new AddRepositoryCommand(seriesId, repositoryAlias), cancellationToken));

    public Task<Result<SprintSeriesDto>> RemoveRepositoryAsync(Guid seriesId, string repositoryAlias, CancellationToken cancellationToken = default) =>
        Gate(() => configure.Handle(new RemoveRepositoryCommand(seriesId, repositoryAlias), cancellationToken));

    public Task<Result<SprintSeriesDto>> ChangeCadenceAsync(Guid seriesId, SprintLength length, DayOfWeek startWeekday, CancellationToken cancellationToken = default) =>
        Gate(() => configure.Handle(new ChangeCadenceCommand(seriesId, length, startWeekday), cancellationToken));

    public Task<Result<SprintSeriesDto>> RenameAsync(Guid seriesId, string name, string namePattern, CancellationToken cancellationToken = default) =>
        Gate(() => configure.Handle(new RenameSeriesCommand(seriesId, name, namePattern), cancellationToken));

    public Task<Result<SprintSeriesDto>> ShowInBriefsAsync(Guid seriesId, bool daily, bool weekly, CancellationToken cancellationToken = default) =>
        Gate(() => configure.Handle(new ShowInBriefsCommand(seriesId, daily, weekly), cancellationToken));

    public Task<Result<SprintSeriesDto>> SwitchAsync(Guid seriesId, bool on, CancellationToken cancellationToken = default) =>
        Gate(() => configure.Handle(new SwitchSeriesCommand(seriesId, on), cancellationToken));

    public Task<Result<SprintSeriesDto>> SetGoalRulesAsync(Guid seriesId, IReadOnlyList<GoalRuleDto> rules, int goalShare, CancellationToken cancellationToken = default) =>
        Gate(() => configure.Handle(new SetGoalRulesCommand(seriesId, rules, goalShare), cancellationToken));

    public Task<Result<SprintSeriesDto>> StartSprintAsync(Guid seriesId, DateOnly startOn, string goal, CancellationToken cancellationToken = default) =>
        Gate(() => start.Handle(new StartSprintCommand(seriesId, startOn, goal), cancellationToken));

    public Task<Result<SprintSeriesDto>> SetSprintGoalAsync(Guid seriesId, int number, string goal, CancellationToken cancellationToken = default) =>
        Gate(() => fill.Handle(new SetSprintGoalCommand(seriesId, number, goal), cancellationToken));

    public Task<Result<SprintSeriesDto>> AddTaskAsync(Guid seriesId, int number, Guid taskId, CancellationToken cancellationToken = default) =>
        Gate(() => fill.Handle(new AddTaskCommand(seriesId, number, taskId), cancellationToken));

    public Task<Result<SprintSeriesDto>> RemoveTaskAsync(Guid seriesId, int number, Guid taskId, CancellationToken cancellationToken = default) =>
        Gate(() => fill.Handle(new RemoveTaskCommand(seriesId, number, taskId), cancellationToken));

    public Task<Result<SprintSeriesDto>> LinkPlanAsync(Guid seriesId, int number, string planTag, CancellationToken cancellationToken = default) =>
        Gate(() => fill.Handle(new LinkPlanCommand(seriesId, number, planTag), cancellationToken));

    public Task<Result<SprintSeriesDto>> UnlinkPlanAsync(Guid seriesId, int number, string planTag, CancellationToken cancellationToken = default) =>
        Gate(() => fill.Handle(new UnlinkPlanCommand(seriesId, number, planTag), cancellationToken));

    public Task<Result<SprintSeriesDto>> MarkGoalAsync(Guid seriesId, int number, Guid taskId, bool isGoal, CancellationToken cancellationToken = default) =>
        Gate(() => mark.Handle(new MarkGoalCommand(seriesId, number, taskId, isGoal), cancellationToken));

    public Task<Result<SprintSeriesDto>> SetNoteAsync(Guid seriesId, int number, Guid taskId, string? note, CancellationToken cancellationToken = default) =>
        Gate(() => mark.Handle(new SetNoteCommand(seriesId, number, taskId, note), cancellationToken));

    public Task<Result<SprintSeriesDto>> SetOpenQuestionAsync(Guid seriesId, int number, Guid taskId, bool openQuestion, CancellationToken cancellationToken = default) =>
        Gate(() => mark.Handle(new SetOpenQuestionCommand(seriesId, number, taskId, openQuestion), cancellationToken));

    public Task<Result<SprintSeriesDto>> CloseSprintAsync(Guid seriesId, IReadOnlyDictionary<Guid, CloseOutcome> outcomes, string nextGoal, CancellationToken cancellationToken = default) =>
        Gate(() => close.Handle(new CloseSprintCommand(seriesId, outcomes, nextGoal), cancellationToken));

    public Task<Result<SprintProgressDto>> ReadAsync(Guid seriesId, int? number = null, CancellationToken cancellationToken = default) =>
        Gate(() => progress.Handle(new ReadSprintProgressQuery(seriesId, number), cancellationToken));

    private Task<Result<T>> Gate<T>(Func<Task<Result<T>>> call) =>
        IsOn ? call() : Task.FromResult(Result.Failure<T>(ProgressErrors.Off()));
}
