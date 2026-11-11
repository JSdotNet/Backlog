using System.ComponentModel;

using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The goal operations an agent performs through the server: read the goals and the
/// iterations they are read over, set one, edit it, attach and detach its work, and delete it
/// (<c>.devbook/domain/progress/features.md#setting-a-goal</c>,
/// <c>#attaching-work-to-a-goal</c>, local ADR 0035).
/// <para>
/// <b>A class of its own</b>, for the reason <see cref="ImpedimentTools"/> is one: goals are
/// Progress's, so the tools are constructed from Progress's port in a group of their own,
/// gated on the same always-on Tasks key (<see cref="BacklogMcpTools.Goals"/>). Every write
/// goes through <see cref="IGoals"/>, so no tool here changes a task, a plan or a pull
/// request: a goal names its work by identity (local ADR 0035 §6). The server runs inside
/// the desktop app (local ADR 0012), so setting a goal through it is still a desktop action.
/// </para>
/// <para>
/// <b>An instance per call</b>, for the reason <see cref="WorkTools"/> states:
/// <see cref="IGoals"/> is scoped.
/// </para>
/// </summary>
/// <param name="goals">Progress's goal port. Every call here goes through it.</param>
/// <param name="repositories">The repository registry a pull request's repository is
/// resolved through, never registered in (see <see cref="RepositoryScope"/>).</param>
/// <param name="standUp">Progress's iteration reading, which <c>read_iterations</c> answers
/// from. A port of this class rather than a class of its own, because a group is constructed
/// from one class and the reading is the goals' (<see cref="BacklogMcpTools.Goals"/>).</param>
/// <param name="clock">Where "today" comes from when a call names its own length. Optional, as
/// on <see cref="RoadmapTools"/>; without a length the port reads the host's today itself.</param>
[McpServerToolType]
public sealed class GoalTools(IGoals goals, IRepositoryDirectory repositories, IStandUp standUp, TimeProvider? clock = null)
{
    internal const string ListGoals = "list_goals";
    internal const string ReadIterations = "read_iterations";
    internal const string CreateGoal = "create_goal";
    internal const string UpdateGoal = "update_goal";
    internal const string AttachToGoal = "attach_to_goal";
    internal const string DetachFromGoal = "detach_from_goal";
    internal const string DeleteGoal = "delete_goal";

    private const string PlanTagDescription =
        "Selector. A plan's tag, with or without its + sigil, e.g. +goals. Attaches every entry filed under it.";

    private const string TaskIdDescription = "Selector. A backlog entry's id, as a GUID.";

    private const string RepositoryDescription =
        "Selector, with number. A pull request's repository in owner/name form, e.g. JSdotNet/Backlog.";

    private const string NumberDescription = "Selector, with repository. The pull request's number.";

    // Open world: each goal's progress reads the state of the pull requests it counts from
    // GitHub (IGoalPullRequestReader), so this read can reach the network.
    [McpServerTool(Name = ListGoals, Title = "List the goals", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description(
        "Every goal, in the order they were set: its id, title, outcome and due date, the plans, tasks and pull "
        + "requests attached to it, and its progress as done/total with how many open items are doing, blocked "
        + "and to do. Read-only.")]
    public async Task<GoalsPayload> ListAsync(CancellationToken cancellationToken = default)
    {
        var listed = await goals.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        return new GoalsPayload([.. listed.ValueOrThrow().Select(summary => GoalPayload.From(summary.Goal, summary.Progress))]);
    }

    /// <summary>
    /// The iteration reading the goals view and the stand-up draw from, over
    /// <see cref="IStandUp"/> (<c>.devbook/domain/progress/domain.md#stand-up-reading</c>), so a
    /// session reports the figures the person sees. It moves nothing: Ready to resolve is a
    /// reading, and a person resolves (local ADR 0035, 2026-10-29 amendment).
    /// </summary>
    // Open world for the reason list_goals is: the reading counts pull requests by their state
    // on GitHub, for the goals, what was finished and what an impediment holds back.
    [McpServerTool(Name = ReadIterations, Title = "Read the goals and iterations", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description(
        "The last and next iteration windows, and every goal read over them: its standing (on-track or at-risk), "
        + "the day it is expected to land, and its progress with how many items it finished in the last window. "
        + "Then the items finished in the last window and planned for the next, grouped by goal with No goal last; "
        + "the planned effort against the capacity; how many impediments were resolved in the last window; and every "
        + "open impediment with who it is with, its days open, the goal it holds back, each item it holds back and "
        + "whether that is settled, and whether it reads ready to resolve. Read-only: ready to resolve is a reading, "
        + "and resolving is set_impediment_state. Refused for a length that is not day, week, two-weeks or month "
        + "(progress.iteration_length_unknown).")]
    public async Task<IterationsPayload> ReadIterationsAsync(
        [Description("Optional. The iteration length to read with: day, week, two-weeks or month. Without it, the "
            + "person's own setting applies.")]
        string? length = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(length))
        {
            var own = await standUp.ReadAsync(cancellationToken).ConfigureAwait(false);

            return IterationsPayload.From(own.ValueOrThrow());
        }

        if (!Lengths.TryGetValue(length.Trim(), out var named))
        {
            throw RepositoryScope.Failure(SharedKernel.Results.Error.Validation(
                "progress.iteration_length_unknown",
                $"'{length}' is not an iteration length. Use day, week, two-weeks or month, or leave it out for the person's own."));
        }

        var read = await standUp.ReadAsync(Today(), named, cancellationToken).ConfigureAwait(false);

        return IterationsPayload.From(read.ValueOrThrow());
    }

    /// <summary>The host's today in local time, the day <see cref="IStandUp.ReadAsync(CancellationToken)"/>
    /// reads for when it chooses the length itself.</summary>
    private DateOnly Today() => DateOnly.FromDateTime((clock ?? TimeProvider.System).GetLocalNow().DateTime);

    /// <summary>The length tokens a call names, as the setting stores them
    /// (<c>.devbook/domain/progress/context.md#iteration-length</c>).</summary>
    internal static readonly IReadOnlyDictionary<string, IterationLength> Lengths =
        new Dictionary<string, IterationLength>(StringComparer.OrdinalIgnoreCase)
        {
            ["day"] = IterationLength.Day,
            ["week"] = IterationLength.Week,
            ["two-weeks"] = IterationLength.TwoWeeks,
            ["month"] = IterationLength.Month,
        };

    internal static string ToWire(IterationLength length) => length switch
    {
        IterationLength.Day => "day",
        IterationLength.Week => "week",
        IterationLength.TwoWeeks => "two-weeks",
        _ => "month",
    };

    [McpServerTool(Name = CreateGoal, Title = "Set a goal", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description(
        "Sets a new goal, holding nothing yet; attach its work with attach_to_goal. Refused without a title "
        + "(progress.goal_without_title) or with an outcome of more than one line (progress.outcome_not_one_line). "
        + "Calling it twice sets two goals.")]
    public async Task<GoalPayload> CreateAsync(
        [Description("What the goal is, e.g. \"Local development\".")]
        string title,
        [Description("Optional. The day it should be reached by, as yyyy-MM-dd; without one the goal is due on "
            + "the last day of the current iteration.")]
        DateOnly? dueDate = null,
        [Description("Optional. One line saying what is true once the goal is reached.")]
        string? outcome = null,
        CancellationToken cancellationToken = default)
    {
        var created = await goals.CreateAsync(new SetGoalRequest(title, outcome, dueDate), cancellationToken).ConfigureAwait(false);

        return GoalPayload.From(created.ValueOrThrow());
    }

    /// <summary>
    /// Changes the fields given and keeps the rest. <see cref="IGoals.EditAsync"/> replaces
    /// all three, so the stored goal is read first and an omitted field is sent back as it
    /// was; an empty outcome is a field given, and clears it.
    /// </summary>
    [McpServerTool(Name = UpdateGoal, Title = "Edit a goal", ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description(
        "Changes a goal's title, outcome or due date. Only the fields given change; an outcome given as an empty "
        + "string clears it. What the goal holds is unchanged. Refused for an unknown goal (progress.goal_not_found), "
        + "a blank title (progress.goal_without_title) or an outcome of more than one line "
        + "(progress.outcome_not_one_line).")]
    public async Task<GoalPayload> UpdateAsync(
        [Description("The goal's id, as a GUID.")]
        Guid id,
        [Description("Optional. The new title.")]
        string? title = null,
        [Description("Optional. The new one-line outcome; an empty string clears it.")]
        string? outcome = null,
        [Description("Optional. The new due date, as yyyy-MM-dd.")]
        DateOnly? dueDate = null,
        CancellationToken cancellationToken = default)
    {
        var stored = (await goals.GetAsync(id, cancellationToken).ConfigureAwait(false)).ValueOrThrow();

        var edited = await goals
            .EditAsync(id, new SetGoalRequest(title ?? stored.Title, outcome ?? stored.Outcome, dueDate ?? stored.DueDate), cancellationToken)
            .ConfigureAwait(false);

        return GoalPayload.From(edited.ValueOrThrow());
    }

    [McpServerTool(Name = AttachToGoal, Title = "Attach work to a goal", ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description(
        "Attaches one plan, task or pull request to a goal. Name exactly one: planTag, or taskId, or repository and "
        + "number together. Attaching what the goal already holds changes nothing. An item another goal holds is "
        + "refused, naming that goal (progress.item_in_other_goal), unless moveFromOtherGoal is true: then it leaves "
        + "that goal and joins this one. The task, plan or pull request itself is never changed.")]
    public async Task<GoalPayload> AttachAsync(
        [Description("The goal's id, as a GUID.")]
        Guid goalId,
        [Description(PlanTagDescription)]
        string? planTag = null,
        [Description(TaskIdDescription)]
        Guid? taskId = null,
        [Description(RepositoryDescription)]
        string? repository = null,
        [Description(NumberDescription)]
        int? number = null,
        [Description("Move the item from the goal that holds it now. Without it, an item in another goal is refused.")]
        bool moveFromOtherGoal = false,
        CancellationToken cancellationToken = default)
    {
        var item = Item(planTag, taskId, repository, number, registeredOnly: true);

        var attached = await goals.AttachAsync(goalId, item, moveFromOtherGoal, cancellationToken).ConfigureAwait(false);

        return GoalPayload.From(attached.ValueOrThrow());
    }

    [McpServerTool(Name = DetachFromGoal, Title = "Detach work from a goal", ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description(
        "Detaches one plan, task or pull request from a goal. Name exactly one: planTag, or taskId, or repository "
        + "and number together. Detaching what the goal does not hold changes nothing. The task, plan or pull "
        + "request itself is never changed.")]
    public async Task<GoalPayload> DetachAsync(
        [Description("The goal's id, as a GUID.")]
        Guid goalId,
        [Description(PlanTagDescription)]
        string? planTag = null,
        [Description(TaskIdDescription)]
        Guid? taskId = null,
        [Description(RepositoryDescription)]
        string? repository = null,
        [Description(NumberDescription)]
        int? number = null,
        CancellationToken cancellationToken = default)
    {
        var item = Item(planTag, taskId, repository, number, registeredOnly: false);

        var detached = await goals.DetachAsync(goalId, item, cancellationToken).ConfigureAwait(false);

        return GoalPayload.From(detached.ValueOrThrow());
    }

    [McpServerTool(Name = DeleteGoal, Title = "Delete a goal", ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = false)]
    [Description(
        "Deletes a goal and its attachments. The plans, tasks and pull requests it held are unchanged and belong to "
        + "no goal from then on. Refused for an unknown goal (progress.goal_not_found).")]
    public async Task<GoalDeletedPayload> DeleteAsync(
        [Description("The goal's id, as a GUID.")]
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var deleted = await goals.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

        if (deleted.IsFailure) throw RepositoryScope.Failure(deleted.Error);

        return new GoalDeletedPayload(id);
    }

    /// <summary>The one item the selectors name (<see cref="ItemSelector"/>): an attach
    /// refuses a repository the registry does not know, a detach
    /// (<paramref name="registeredOnly"/> false) passes it on as given.</summary>
    private GoalItemDto Item(string? planTag, Guid? taskId, string? repository, int? number, bool registeredOnly) =>
        ItemSelector.One(
            repositories,
            planTag,
            taskId,
            repository,
            number,
            registeredOnly,
            GoalItemDto.ForPlan,
            GoalItemDto.ForTask,
            GoalItemDto.ForPullRequest);

    internal static string ToWire(GoalAttachmentKind kind) => kind switch
    {
        GoalAttachmentKind.Plan => "plan",
        GoalAttachmentKind.Task => "task",
        _ => "pull-request",
    };
}

/// <summary>Every goal, as <c>list_goals</c> answers.</summary>
public sealed record GoalsPayload(IReadOnlyList<GoalPayload> Goals);

/// <summary>One goal as a session receives it.</summary>
/// <param name="Id">The goal's id.</param>
/// <param name="Title">What the goal is.</param>
/// <param name="Outcome">One line saying what is true once it is reached, or null.</param>
/// <param name="DueDate">The day it should be reached by, or null for a goal due at the end of
/// the current iteration.</param>
/// <param name="Items">The plans, tasks and pull requests attached to it, in the order they were attached.</param>
/// <param name="Progress">How far it has come; null on the answer to a write, which reads no progress.</param>
public sealed record GoalPayload(
    Guid Id,
    string Title,
    string? Outcome,
    DateOnly? DueDate,
    IReadOnlyList<GoalItemPayload> Items,
    GoalProgressPayload? Progress)
{
    internal static GoalPayload From(GoalDto goal, GoalProgressDto? progress = null) =>
        new(
            goal.Id,
            goal.Title,
            goal.Outcome,
            goal.DueDate,
            [.. goal.Attachments.Select(GoalItemPayload.From)],
            progress is null ? null : new GoalProgressPayload(progress.Done, progress.Total, progress.Doing, progress.Blocked, progress.ToDo));
}

/// <summary>One item a goal holds. Which identity fields are set follows <paramref name="Kind"/>.</summary>
/// <param name="Kind">plan, task or pull-request.</param>
/// <param name="PlanTag">A plan's tag.</param>
/// <param name="TaskId">A task's id.</param>
/// <param name="Repository">A pull request's repository, as owner/name.</param>
/// <param name="Number">A pull request's number.</param>
/// <param name="AttachedOn">The day it was attached.</param>
public sealed record GoalItemPayload(string Kind, string? PlanTag, Guid? TaskId, string? Repository, int? Number, DateOnly AttachedOn)
{
    internal static GoalItemPayload From(GoalAttachmentDto attachment) =>
        new(
            GoalTools.ToWire(attachment.Item.Kind),
            attachment.Item.PlanTag,
            attachment.Item.TaskId,
            attachment.Item.Repository,
            attachment.Item.Number,
            attachment.AttachedOn);
}

/// <summary>How far a goal has come: done of total items, each counted once, and how the
/// open ones stand.</summary>
public sealed record GoalProgressPayload(int Done, int Total, int Doing, int Blocked, int ToDo);

/// <summary>The goal <c>delete_goal</c> deleted.</summary>
public sealed record GoalDeletedPayload(Guid Id);
