using System.ComponentModel;

using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The impediment operations a session performs beside <c>set_blocked</c>: raise an
/// impediment holding back tasks, pull requests and plans, move one along its states, and
/// link or unlink what it holds back
/// (<c>.devbook/domain/progress/features.md#impediments-for-agents</c>, local ADR 0035 §5).
/// <para>
/// <b>A class of its own rather than more methods on <see cref="TrackerTools"/>.</b>
/// Impediments are Progress's, not Tasks', so the tools are constructed from Progress's
/// port in a group of their own, gated on the same always-on Tasks key
/// (<see cref="BacklogMcpTools.Impediments"/>). A task's blocked mark is a Tasks-only mark that
/// raises no impediment (<c>.devbook/domain/progress/requirements.md#requirement-a-tasks-blocked-mark-changes-no-impediment</c>),
/// so a session that finds something outside the backlog in the way raises the impediment itself.
/// </para>
/// <para>
/// <b>An instance per call</b>, for the reason <see cref="WorkTools"/> states:
/// <see cref="IImpediments"/> is scoped.
/// </para>
/// </summary>
/// <param name="impediments">Progress's impediment port. Every call here goes through it.</param>
/// <param name="repositories">The repository registry a pull request's repository is
/// resolved through, never registered in (see <see cref="ItemSelector"/>).</param>
[McpServerToolType]
public sealed class ImpedimentTools(IImpediments impediments, IRepositoryDirectory repositories)
{
    internal const string RaiseImpediment = "raise_impediment";
    internal const string SetImpedimentState = "set_impediment_state";
    internal const string LinkToImpediment = "link_to_impediment";
    internal const string UnlinkFromImpediment = "unlink_from_impediment";

    private const string PlanTagDescription = "Selector. A plan's tag, with or without its + sigil, e.g. +bank-integration.";

    private const string TaskIdDescription = "Selector. A backlog entry's id, as a GUID.";

    private const string RepositoryDescription =
        "Selector, with number. A pull request's repository in owner/name form, e.g. acme/invoicing.";

    private const string NumberDescription = "Selector, with repository. The pull request's number.";

    /// <summary>The state tokens a call names, as the wire carries them.</summary>
    internal static readonly IReadOnlyDictionary<string, ImpedimentState> States =
        new Dictionary<string, ImpedimentState>(StringComparer.OrdinalIgnoreCase)
        {
            ["new"] = ImpedimentState.New,
            ["reported"] = ImpedimentState.Reported,
            ["escalated"] = ImpedimentState.Escalated,
            ["being-handled"] = ImpedimentState.BeingHandled,
            ["resolved"] = ImpedimentState.Resolved,
        };

    [McpServerTool(Name = RaiseImpediment, Title = "Raise an impediment", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description(
        "Raises a New impediment: something outside the backlog that holds work back, tracked until it is resolved. "
        + "Name what it holds back: tasks by id, pull requests by repository and number, plans by tag. Marking a task "
        + "blocked with set_blocked raises no impediment, so raise one here whenever something outside the backlog is "
        + "in the way. Calling it twice raises two.")]
    public async Task<ImpedimentPayload> RaiseAsync(
        [Description("What is in the way, e.g. \"Bank sample files missing\".")]
        string title,
        [Description("The ids of the backlog entries it holds back, as GUIDs. May be empty.")]
        Guid[]? holdsBack = null,
        [Description("The pull requests it holds back, each by its repository in owner/name form and its number. May be empty.")]
        ImpedimentPullRequestArgument[]? pullRequests = null,
        [Description("The plans it holds back, each by its tag, with or without its + sigil. May be empty.")]
        string[]? planTags = null,
        [Description("An optional note.")]
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        // Resolved before the port is asked, so a repository nobody registered raises nothing.
        List<HeldBackItemDto> items =
        [
            .. (holdsBack ?? []).Select(HeldBackItemDto.ForTask),
            .. (pullRequests ?? []).Select(pull => HeldBackItemDto.ForPullRequest(
                ItemSelector.Repository(repositories, pull.Repository ?? string.Empty, registeredOnly: true),
                pull.Number)),
            .. (planTags ?? []).Select(tag => HeldBackItemDto.ForPlan(tag?.Trim() ?? string.Empty)),
        ];

        var raised = await impediments
            .RaiseAsync(new RaiseImpedimentRequest(title, items, note), cancellationToken)
            .ConfigureAwait(false);

        return ImpedimentPayload.From(raised.ValueOrThrow());
    }

    [McpServerTool(Name = SetImpedimentState, Title = "Move an impediment to another state", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description(
        "Moves an impediment to reported, escalated, being-handled or resolved, as of today, and records who it is "
        + "with at that step. It moves forward only: new to reported, reported to escalated or being-handled, "
        + "escalated to being-handled; any open state may move to resolved. Every other move is refused, and a "
        + "resolved impediment stays resolved.")]
    public async Task<ImpedimentPayload> SetStateAsync(
        [Description("The impediment's id, as a GUID.")]
        Guid id,
        [Description("The state to move to: reported, escalated, being-handled or resolved.")]
        string state,
        [Description("Who it is with at this step, in words, e.g. \"bank support\". Optional.")]
        string? with = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(state) || !States.TryGetValue(state.Trim(), out var target))
        {
            throw RepositoryScope.Failure(SharedKernel.Results.Error.Validation(
                "progress.impediment_state_unknown",
                $"'{state}' is not an impediment state. Use reported, escalated, being-handled or resolved."));
        }

        var moved = await impediments.MoveAsync(id, target, with, cancellationToken).ConfigureAwait(false);

        return ImpedimentPayload.From(moved.ValueOrThrow());
    }

    [McpServerTool(Name = LinkToImpediment, Title = "Link work an impediment holds back", ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description(
        "Links one task, pull request or plan an impediment holds back. Name exactly one: taskId, or planTag, or "
        + "repository and number together; a number without its repository is refused (selector.incomplete). "
        + "Linking what it already holds back changes nothing, and neither the state nor the history moves. The "
        + "task, plan or pull request itself is never changed.")]
    public async Task<ImpedimentPayload> LinkAsync(
        [Description("The impediment's id, as a GUID.")]
        Guid id,
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
        var item = Item(planTag, taskId, repository, number, registeredOnly: true);

        var linked = await impediments.HoldBackAsync(id, item, cancellationToken).ConfigureAwait(false);

        return ImpedimentPayload.From(linked.ValueOrThrow());
    }

    [McpServerTool(Name = UnlinkFromImpediment, Title = "Unlink work from an impediment", ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description(
        "Unlinks one task, pull request or plan from an impediment. Name exactly one: taskId, or planTag, or "
        + "repository and number together. Unlinking what it does not hold back changes nothing, and neither the "
        + "state nor the history moves. The task, plan or pull request itself is never changed.")]
    public async Task<ImpedimentPayload> UnlinkAsync(
        [Description("The impediment's id, as a GUID.")]
        Guid id,
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

        var unlinked = await impediments.ReleaseAsync(id, item, cancellationToken).ConfigureAwait(false);

        return ImpedimentPayload.From(unlinked.ValueOrThrow());
    }

    /// <summary>The one item the selectors name (<see cref="ItemSelector"/>): a link refuses
    /// a repository the registry does not know, an unlink (<paramref name="registeredOnly"/>
    /// false) passes it on as given.</summary>
    private HeldBackItemDto Item(string? planTag, Guid? taskId, string? repository, int? number, bool registeredOnly) =>
        ItemSelector.One(
            repositories,
            planTag,
            taskId,
            repository,
            number,
            registeredOnly,
            HeldBackItemDto.ForPlan,
            HeldBackItemDto.ForTask,
            HeldBackItemDto.ForPullRequest);

    internal static string ToWire(ImpedimentState state) => state switch
    {
        ImpedimentState.New => "new",
        ImpedimentState.Reported => "reported",
        ImpedimentState.Escalated => "escalated",
        ImpedimentState.BeingHandled => "being-handled",
        _ => "resolved",
    };
}

/// <summary>One impediment as a session receives it.</summary>
/// <param name="Id">The impediment's id.</param>
/// <param name="Title">What is in the way.</param>
/// <param name="State">Its state as a wire token: new, reported, escalated, being-handled or resolved.</param>
/// <param name="ReportedTo">Who it was last reported or escalated to, or null.</param>
/// <param name="HandledBy">Who handles it, or null.</param>
/// <param name="CheckAgain">The day to look at it again, or null.</param>
/// <param name="Note">Its note, or null.</param>
/// <param name="History">Every move, oldest first.</param>
/// <param name="HoldsBack">The ids of the entries it holds back.</param>
/// <param name="PullRequests">The pull requests it holds back, in the order linked.</param>
/// <param name="Plans">The tags of the plans it holds back, without their sigil, in the order linked.</param>
public sealed record ImpedimentPayload(
    Guid Id,
    string Title,
    string State,
    string? ReportedTo,
    string? HandledBy,
    DateOnly? CheckAgain,
    string? Note,
    IReadOnlyList<ImpedimentStepPayload> History,
    IReadOnlyList<Guid> HoldsBack,
    IReadOnlyList<ImpedimentPullRequestPayload> PullRequests,
    IReadOnlyList<string> Plans)
{
    internal static ImpedimentPayload From(ImpedimentDto impediment) =>
        new(
            impediment.Id,
            impediment.Title,
            ImpedimentTools.ToWire(impediment.State),
            impediment.ReportedTo,
            impediment.HandledBy,
            impediment.CheckAgain,
            impediment.Note,
            [.. impediment.History.Select(step => new ImpedimentStepPayload(ImpedimentTools.ToWire(step.State), step.On, step.With))],
            impediment.HeldBackTasks,
            [
                .. impediment.HoldsBack
                    .Where(item => item.Kind == HeldBackKind.PullRequest && item.Repository is not null && item.Number is not null)
                    .Select(item => new ImpedimentPullRequestPayload(item.Repository!, item.Number!.Value))
            ],
            [.. impediment.HoldsBack.Where(item => item.Kind == HeldBackKind.Plan && item.PlanTag is not null).Select(item => item.PlanTag!)]);
}

/// <summary>A pull request an impediment holds back, as a session receives it.</summary>
/// <param name="Repository">Its repository, in <c>owner/name</c> form.</param>
/// <param name="Number">Its number.</param>
public sealed record ImpedimentPullRequestPayload(string Repository, int Number);

/// <summary>A pull request a session names for <c>raise_impediment</c> to hold back.</summary>
/// <param name="Repository">Its repository, in <c>owner/name</c> form, e.g. <c>acme/invoicing</c>.</param>
/// <param name="Number">Its number.</param>
public sealed record ImpedimentPullRequestArgument(
    [property: Description("The pull request's repository, in owner/name form.")] string? Repository,
    [property: Description("The pull request's number.")] int Number);

/// <summary>One step in an impediment's history.</summary>
/// <param name="State">The state it moved to, as a wire token.</param>
/// <param name="On">The day it moved.</param>
/// <param name="With">Who it was with at that step, or null.</param>
public sealed record ImpedimentStepPayload(string State, DateOnly On, string? With);
