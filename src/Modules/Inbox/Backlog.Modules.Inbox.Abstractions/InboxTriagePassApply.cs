using System.Globalization;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Abstractions;

/// <summary>
/// One decision the reader accepted on the AI triage pass, ready to apply.
/// <paramref name="Name"/> is what a failure sentence calls it: the item's
/// title, or for a plan the plan's title.
/// <para>
/// A decision is one of the acts the reader could have taken by hand, and is
/// applied through that act on <see cref="IInboxItems"/>: the pass adds no
/// write path of its own (local ADR 0023 §5), so every rule the hand-made act
/// keeps, the applied one keeps too.
/// </para>
/// </summary>
public abstract record InboxTriageDecision(string Name);

/// <summary>A plan: the items routed together as one batch under a plan tag
/// built from <paramref name="Title"/>, each to <paramref name="Repositories"/>
/// — or, when that is empty, to the repositories it already carries.</summary>
public sealed record InboxTriagePlanDecision(
    string Title,
    IReadOnlyList<Guid> ItemIds,
    IReadOnlyList<string> Repositories)
    : InboxTriageDecision(Title);

/// <summary>What the reader chose for a duplicate pair. "Keep both" is no
/// decision at all, so it is not here: a pair left as both is simply not applied.</summary>
public enum InboxTriageDuplicateChoice
{
    /// <summary>Into the task as a comment, or archived as the other item's
    /// duplicate when the target is an inbox item.</summary>
    Merge,

    /// <summary>Archive the capture and leave the target as it is.</summary>
    Archive,
}

/// <summary>A duplicate pair: <paramref name="ItemId"/> repeats the task or
/// inbox item <paramref name="TargetId"/>.</summary>
public sealed record InboxTriageDuplicateDecision(
    string Name,
    Guid ItemId,
    InboxTriageTargetKind TargetKind,
    Guid TargetId,
    InboxTriageDuplicateChoice Choice)
    : InboxTriageDecision(Name);

/// <summary>One item moved to the backlog on its own, written to
/// <paramref name="Repositories"/> first when there are any, as triage's own
/// Move to backlog does.</summary>
public sealed record InboxTriageRouteDecision(
    string Name,
    Guid ItemId,
    IReadOnlyList<string> Repositories)
    : InboxTriageDecision(Name);

/// <summary>One item filed in one of the reader's lists.</summary>
public sealed record InboxTriageFilingDecision(string Name, Guid ItemId, Guid ListId)
    : InboxTriageDecision(Name);

/// <summary>One item archived.</summary>
public sealed record InboxTriageArchiveDecision(string Name, Guid ItemId)
    : InboxTriageDecision(Name);

/// <summary>A decision that landed. <paramref name="Batch"/> is a plan's
/// batch — partly routed ones included, so their routed half can still be
/// taken back — and <paramref name="Routed"/> a single route's entries.</summary>
public sealed record InboxTriageApplied(
    InboxTriageDecision Decision,
    InboxBatchRoutedDto? Batch = null,
    InboxRoutedDto? Routed = null);

/// <summary>The decision that stopped Apply: which item it was refused for,
/// what that item is called, and why.</summary>
public sealed record InboxTriagePassFailure(
    InboxTriageDecision Decision,
    Guid ItemId,
    string ItemName,
    Error Error)
{
    /// <summary>Whether the plan was refused as a whole — its batch never went,
    /// so no one item is to blame and the sentence names the plan.</summary>
    public bool WholePlan { get; init; }

    /// <summary>The one sentence the reader is told: what could not be done
    /// to which item, why, and that the rest is as it was — the decisions
    /// before it applied, the ones after it never tried.</summary>
    public string Sentence =>
        $"Couldn't {Act()}: {Terminated(Error.Message)} The decisions before it stay applied; the rest were not tried.";

    private string Act() => Decision switch
    {
        InboxTriagePlanDecision plan when WholePlan => $"make the plan \"{plan.Title}\"",
        InboxTriagePlanDecision plan => $"move \"{ItemName}\" to the backlog as part of \"{plan.Title}\"",
        InboxTriageDuplicateDecision { Choice: InboxTriageDuplicateChoice.Merge, TargetKind: InboxTriageTargetKind.Task } =>
            $"merge \"{ItemName}\" into the backlog entry",
        InboxTriageDuplicateDecision { Choice: InboxTriageDuplicateChoice.Merge } => $"archive \"{ItemName}\" as a duplicate",
        InboxTriageRouteDecision => $"move \"{ItemName}\" to the backlog",
        InboxTriageFilingDecision => $"file \"{ItemName}\" in its list",
        _ => $"archive \"{ItemName}\"",
    };

    /// <summary>The module's messages mostly end with a full stop; one that
    /// does not still closes its sentence before the next one starts.</summary>
    private static string Terminated(string message)
    {
        var trimmed = (message ?? string.Empty).Trim();
        if (trimmed.Length == 0) return "No reason was given.";

        return trimmed[^1] is '.' or '!' or '?' ? trimmed : trimmed + ".";
    }
}

/// <summary>What Apply did: the decisions that landed, in order; the one that
/// stopped it, if any; and how many after it were never tried.</summary>
public sealed record InboxTriagePassOutcome(
    IReadOnlyList<InboxTriageApplied> Applied,
    InboxTriagePassFailure? Failure,
    int NotTried)
{
    public bool Succeeded => Failure is null;
}

/// <summary>
/// Apply on the AI triage pass (local ADR 0023 §5): runs the accepted decisions
/// through the port, in the order given, and stops at the first one refused.
/// <para>
/// Static and over the published port, beside <see cref="InboxBatchOrder"/> and
/// <see cref="InboxPlanTag"/>, because it is orchestration and not a rule: each
/// act is the module's own command, refused or allowed by the module's own
/// rules, and the pane is the one caller. Stopping rather than carrying on is
/// the point — the reader accepted a set of decisions together, and a later one
/// may lean on an earlier one; once one fails, what is left is the reader's to
/// look at again, not the pass's to push through.
/// </para>
/// <para>
/// A plan goes as one batch under a fresh plan tag of its own, minted from its
/// title, so two plans never share an import id and one never clears the
/// other's entries. A batch that refused any member counts as this decision's
/// failure; the members it did route stay routed and are reported, so they can
/// be taken back.
/// </para>
/// </summary>
public static class InboxTriagePassApply
{
    /// <summary>The code a thrown exception is reported under.</summary>
    public const string ApplyFailedCode = "inbox.triage.apply_failed";

    /// <summary>Applies <paramref name="decisions"/> in order. <paramref name="titles"/>,
    /// by item id, names the item a plan's refusal is about; without one the
    /// item is named by its id.</summary>
    public static async Task<InboxTriagePassOutcome> ApplyAsync(
        IInboxItems inbox,
        IReadOnlyList<InboxTriageDecision> decisions,
        IReadOnlyDictionary<Guid, string>? titles = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(decisions);

        var applied = new List<InboxTriageApplied>();

        for (var index = 0; index < decisions.Count; index++)
        {
            var decision = decisions[index];
            ArgumentNullException.ThrowIfNull(decision);

            Step step;
            try
            {
                step = await ApplyOneAsync(inbox, decision, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A store that cannot write is a refusal like any other: the
                // reader is told which item, and nothing after it is tried.
                step = new Step(null, new Refusal(FirstItem(decision), Error.Unexpected(ApplyFailedCode, ex.Message)));
            }

            if (step.Applied is { } landed) applied.Add(landed);

            if (step.Refusal is { } refusal)
            {
                var failure = new InboxTriagePassFailure(
                    decision,
                    refusal.ItemId,
                    refusal.WholePlan ? decision.Name : NameOf(decision, refusal.ItemId, titles),
                    refusal.Error)
                {
                    WholePlan = refusal.WholePlan,
                };
                return new InboxTriagePassOutcome(applied, failure, decisions.Count - index - 1);
            }
        }

        return new InboxTriagePassOutcome(applied, null, 0);
    }

    private static Task<Step> ApplyOneAsync(IInboxItems inbox, InboxTriageDecision decision, CancellationToken cancellationToken) => decision switch
    {
        InboxTriagePlanDecision plan => PlanAsync(inbox, plan, cancellationToken),
        InboxTriageDuplicateDecision duplicate => DuplicateAsync(inbox, duplicate, cancellationToken),
        InboxTriageRouteDecision route => RouteAsync(inbox, route, cancellationToken),
        InboxTriageFilingDecision filing => SimpleAsync(decision, filing.ItemId, inbox.MoveToListAsync(filing.ItemId, filing.ListId, cancellationToken)),
        InboxTriageArchiveDecision archive => SimpleAsync(decision, archive.ItemId, inbox.ArchiveAsync(archive.ItemId, cancellationToken)),
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision.GetType().Name, "Not a triage decision."),
    };

    private static async Task<Step> PlanAsync(IInboxItems inbox, InboxTriagePlanDecision plan, CancellationToken cancellationToken)
    {
        var tag = "+" + InboxPlanTag.For(plan.Title, Guid.CreateVersion7());
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? repositories = plan.Repositories.Count == 0
            ? null
            : plan.ItemIds.Distinct().ToDictionary(id => id, _ => plan.Repositories);

        var result = await inbox.RouteToBacklogAsync(
            plan.ItemIds,
            listId: null,
            new InboxBatchRouteChoicesDto(PlanTag: tag, Repositories: repositories),
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure) return new Step(null, new Refusal(FirstItem(plan), result.Error, WholePlan: true));

        var batch = result.Value;

        // Tasks took the document whole or not at all: refused, nothing went,
        // and every member carries the same refusal — the plan is what failed.
        if (batch.Routed.Count == 0
            && batch.Archived.Count == 0
            && batch.Failed.Count > 0
            && batch.Failed.All(failure => failure.Error.Code == InboxErrors.BatchRefusedCode))
        {
            return new Step(null, new Refusal(FirstItem(plan), batch.Failed[0].Error, WholePlan: true));
        }

        var landed = new InboxTriageApplied(plan, Batch: batch);

        // The members that did route stay routed and are reported; the first
        // refused one, in the plan's order, is the one the reader is told about.
        var refusedFirst = plan.ItemIds
            .Select(id => batch.Failed.FirstOrDefault(failure => failure.Id == id))
            .FirstOrDefault(failure => failure is not null)
            ?? batch.Failed.FirstOrDefault();

        return refusedFirst is null
            ? new Step(landed, null)
            : new Step(batch.Routed.Count > 0 || batch.Archived.Count > 0 ? landed : null, new Refusal(refusedFirst.Id, refusedFirst.Error));
    }

    private static Task<Step> DuplicateAsync(IInboxItems inbox, InboxTriageDuplicateDecision duplicate, CancellationToken cancellationToken)
    {
        var act = duplicate switch
        {
            { Choice: InboxTriageDuplicateChoice.Merge, TargetKind: InboxTriageTargetKind.Task } =>
                inbox.MergeIntoTaskAsync(duplicate.ItemId, duplicate.TargetId, cancellationToken),
            { Choice: InboxTriageDuplicateChoice.Merge } =>
                inbox.ArchiveAsDuplicateAsync(duplicate.ItemId, duplicate.TargetId, cancellationToken),
            _ => inbox.ArchiveAsync(duplicate.ItemId, cancellationToken),
        };

        return SimpleAsync(duplicate, duplicate.ItemId, act);
    }

    private static async Task<Step> RouteAsync(IInboxItems inbox, InboxTriageRouteDecision route, CancellationToken cancellationToken)
    {
        if (route.Repositories.Count > 0)
        {
            var assigned = await inbox.AssignRepositoriesAsync(route.ItemId, route.Repositories, cancellationToken).ConfigureAwait(false);
            if (assigned.IsFailure) return new Step(null, new Refusal(route.ItemId, assigned.Error));
        }

        var routed = await inbox.RouteToBacklogAsync(route.ItemId, cancellationToken).ConfigureAwait(false);

        return routed.IsSuccess
            ? new Step(new InboxTriageApplied(route, Routed: routed.Value), null)
            : new Step(null, new Refusal(route.ItemId, routed.Error));
    }

    private static async Task<Step> SimpleAsync(InboxTriageDecision decision, Guid itemId, Task<Result> act)
    {
        var result = await act.ConfigureAwait(false);

        return result.IsSuccess
            ? new Step(new InboxTriageApplied(decision), null)
            : new Step(null, new Refusal(itemId, result.Error));
    }

    /// <summary>The item a decision is about — a plan's first member.</summary>
    private static Guid FirstItem(InboxTriageDecision decision) => decision switch
    {
        InboxTriagePlanDecision plan => plan.ItemIds.Count > 0 ? plan.ItemIds[0] : Guid.Empty,
        InboxTriageDuplicateDecision duplicate => duplicate.ItemId,
        InboxTriageRouteDecision route => route.ItemId,
        InboxTriageFilingDecision filing => filing.ItemId,
        InboxTriageArchiveDecision archive => archive.ItemId,
        _ => Guid.Empty,
    };

    /// <summary>The item's title when the caller gave one; otherwise the
    /// decision's own name for a single-item decision, and the id for a plan's
    /// member, whose title the decision does not carry.</summary>
    private static string NameOf(InboxTriageDecision decision, Guid itemId, IReadOnlyDictionary<Guid, string>? titles)
    {
        if (titles is not null && titles.TryGetValue(itemId, out var title) && !string.IsNullOrWhiteSpace(title)) return title;

        return decision is InboxTriagePlanDecision ? itemId.ToString("D", CultureInfo.InvariantCulture) : decision.Name;
    }

    private sealed record Step(InboxTriageApplied? Applied, Refusal? Refusal);

    private sealed record Refusal(Guid ItemId, Error Error, bool WholePlan = false);
}
