using System.ComponentModel;

using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The impediment operations a session performs beside <c>set_blocked</c>: raise an
/// impediment, and move one along its states
/// (<c>.devbook/domain/progress/features.md#tracking-an-impediment</c>, local ADR 0035 §5).
/// <para>
/// <b>A class of its own rather than two more methods on <see cref="TrackerTools"/>.</b>
/// That group is gated on the Tasks key; impediments are Progress's and answer to the
/// <see cref="ProgressFeatures.Goals"/> switch, and local ADR 0012 §7 asks one feature
/// check per group. Marking a task blocked raises an impediment on its own — the
/// blocked-task policy hears the task write — so a session reaches for
/// <c>raise_impediment</c> only for one no task's mark describes.
/// </para>
/// <para>
/// <b>An instance per call</b>, for the reason <see cref="WorkTools"/> states:
/// <see cref="IImpediments"/> is scoped.
/// </para>
/// </summary>
/// <param name="impediments">Progress's impediment port. Every call here goes through it.</param>
[McpServerToolType]
public sealed class ImpedimentTools(IImpediments impediments)
{
    internal const string RaiseImpediment = "raise_impediment";
    internal const string SetImpedimentState = "set_impediment_state";

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
        + "Name the tasks it holds back by id. Marking a task blocked with set_blocked raises one on its own, so use "
        + "this for an impediment no blocked task describes. Calling it twice raises two.")]
    public async Task<ImpedimentPayload> RaiseAsync(
        [Description("What is in the way, e.g. \"Bank sample files missing\".")]
        string title,
        [Description("The ids of the backlog entries it holds back, as GUIDs. May be empty.")]
        Guid[]? holdsBack = null,
        [Description("An optional note.")]
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        var raised = await impediments
            .RaiseAsync(new RaiseImpedimentRequest(title, holdsBack ?? [], note), cancellationToken)
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
public sealed record ImpedimentPayload(
    Guid Id,
    string Title,
    string State,
    string? ReportedTo,
    string? HandledBy,
    DateOnly? CheckAgain,
    string? Note,
    IReadOnlyList<ImpedimentStepPayload> History,
    IReadOnlyList<Guid> HoldsBack)
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
            impediment.HoldsBack);
}

/// <summary>One step in an impediment's history.</summary>
/// <param name="State">The state it moved to, as a wire token.</param>
/// <param name="On">The day it moved.</param>
/// <param name="With">Who it was with at that step, or null.</param>
public sealed record ImpedimentStepPayload(string State, DateOnly On, string? With);
