using System.ComponentModel;

using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The goal operations an agent performs through the server: read the goals, set one, edit
/// it, attach and detach its work, and delete it
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
[McpServerToolType]
public sealed class GoalTools(IGoals goals, IRepositoryDirectory repositories)
{
    internal const string ListGoals = "list_goals";
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

    /// <summary>
    /// The one item the selectors name, or the refusal saying why there is not exactly one —
    /// the rule and the codes <c>find_item</c> uses, so a session learns one.
    /// <para>
    /// A pull request's repository is resolved through the registry, and the registered
    /// <c>owner/name</c> is what the goal stores, so <c>jsdotnet/backlog</c> and
    /// <c>JSdotNet/Backlog</c> name one pull request. An attach refuses a repository the
    /// registry does not know, before anything is stored. A detach does not
    /// (<paramref name="registeredOnly"/> false): a goal may hold a pull request in a
    /// repository since removed from the registry, and refusing would leave it there for
    /// good. Such a name is passed on as given, and detaching what the goal does not hold
    /// changes nothing.
    /// </para>
    /// </summary>
    private GoalItemDto Item(string? planTag, Guid? taskId, string? repository, int? number, bool registeredOnly)
    {
        var byPlan = !string.IsNullOrWhiteSpace(planTag);
        var byTask = taskId is not null;
        var byPullRequest = !string.IsNullOrWhiteSpace(repository) || number is not null;

        if (new[] { byPlan, byTask, byPullRequest }.Count(given => given) != 1)
        {
            throw RepositoryScope.Failure(Error.Validation(
                "selector.required",
                "Name exactly one item: planTag, or taskId, or repository and number together."));
        }

        if (byPlan) return GoalItemDto.ForPlan(planTag!.Trim());

        if (byTask) return GoalItemDto.ForTask(taskId!.Value);

        if (string.IsNullOrWhiteSpace(repository) || number is null)
        {
            throw RepositoryScope.Failure(Error.Validation(
                "selector.incomplete",
                "repository and number are one selector and have to be given together."));
        }

        var resolved = RepositoryScope.Resolve(repositories, repository);

        var identity = resolved.IsSuccess ? resolved.Value.Id
            : registeredOnly ? throw RepositoryScope.Failure(resolved.Error)
            : repository.Trim();

        return GoalItemDto.ForPullRequest(identity, number.Value);
    }

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
