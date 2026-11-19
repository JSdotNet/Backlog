using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.RouteToBacklog;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.CreatePlan;

/// <summary>Asks the plan drafter for an import plan about an item, hands the
/// plan to Tasks' import, and records the entries it made as the item's routing.
/// The same terminal outcome as <c>RouteToBacklog</c> reached through a
/// different door. <paramref name="StartProjectNote"/> also starts the plan's
/// project note in the Brain (<c>.devbook/domain/inbox/features.md#create-plan-from-an-item</c>).</summary>
public sealed record CreatePlanCommand(Guid Id, bool StartProjectNote = false);

/// <summary>
/// The drafter is optional because a host may have none, and the answer to
/// "no drafter" is a validation error the pane already shows as a disabled
/// control — not a missing registration that takes the host down at startup.
/// A drafter that is present but not configured says so itself through
/// <see cref="IInboxPlanDrafter.IsAvailable"/>, with its own reason.
/// <para>
/// The draft is checked for exactly one thing before Tasks sees it: that it
/// holds at least one entry heading. Anything less is not a plan, however
/// fluent, and reporting that here is cheaper and clearer than letting the
/// import report "nothing parsed". Everything else about whether it imports —
/// duplicate ids, empty entries — is Tasks' verdict and is passed back as is.
/// </para>
/// <para>
/// A project note is asked of the Brain only once the draft is a plan, so a
/// failed draft leaves no note behind; a Brain that cannot take one refuses
/// before the drafter is asked at all. The note goes through the same
/// <see cref="IInboxBrainTarget"/> as To Brain — into the Brain's projects area,
/// its <c>roadmap</c> list naming the plan's tag — and the plan gains a
/// <c>plan</c> entry whose <c>devbook:</c> token is the note's address, so the
/// Roadmap Item the import lays out carries it as a Knowledge Ref (local ADR
/// 0053). Should the import then refuse, the note stays: it is the person's
/// words in their own Brain, and the refusal is answered as it came.
/// </para>
/// </summary>
public sealed class CreatePlanCommandHandler(
    IInboxItemRepository items,
    IInboxBacklogTarget target,
    TimeProvider clock,
    IInboxPlanDrafter? drafter = null,
    IInboxAttachmentFiles? attachmentFiles = null,
    IInboxBrainTarget? brain = null)
    : ICommandHandler<CreatePlanCommand, Result<InboxRoutedDto>>
{
    public async Task<Result<InboxRoutedDto>> Handle(
        CreatePlanCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (drafter is null || !drafter.IsAvailable)
            return InboxErrors.PlanNotConfigured(drafter?.UnavailableReason);

        var item = await items.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return InboxErrors.ItemNotFound;

        if (item.IsRouted) return InboxErrors.InvalidTransition(
            new InvalidInboxTransitionException(item.Status, "routed again").Message);
        if (item.Status is InboxStatus.Archived) return InboxErrors.InvalidTransition(
            new InvalidInboxTransitionException(item.Status, "routed").Message);

        if (command.StartProjectNote)
        {
            if (brain is null) return InboxErrors.BrainUnavailable(InboxErrors.NoBrainHere);
            if (brain.UnavailableReason is { } reason) return InboxErrors.BrainUnavailable(reason);
        }

        var planTag = PlanTag(item.Title, item.Id);
        var request = new InboxPlanDraftRequestDto(
            item.Id,
            item.Title,
            item.BodyMd,
            item.SourceUrl,
            item.KindSlug,
            [.. item.Tags.Select(tag => tag.Name)],
            [.. item.RepoIds],
            planTag);

        var draft = await drafter.DraftAsync(request, cancellationToken).ConfigureAwait(false);
        if (draft.IsFailure) return draft.Error;

        if (!LooksLikeAPlan(draft.Value.PlanMarkdown)) return InboxErrors.PlanEmpty;

        var planMarkdown = draft.Value.PlanMarkdown;
        string? notePath = null;
        if (command.StartProjectNote)
        {
            var note = await brain!
                .WriteNoteAsync(
                    new InboxBrainNoteRequestDto(item.Id, item.Title, item.BodyMd, item.SourceUrl, IntoProjects: true, RoadmapTag: planTag),
                    cancellationToken)
                .ConfigureAwait(false);
            if (note.IsFailure) return note.Error;

            notePath = note.Value.NotePath;
            planMarkdown = WithPlanEntry(planMarkdown, item.Title, planTag, [.. item.RepoIds], notePath);
        }

        // The item's repositories go along as the only ones the plan may name:
        // the drafter was told the same list, and a model that writes another
        // is refused by the adapter rather than allowed to register it. The
        // item's attachment folder goes along too, as Route to backlog hands it
        // on — to the import, not the drafter: it is a place on this machine,
        // and the text the model writes has no use for it.
        var imported = await target
            .ImportPlanAsync(
                planMarkdown,
                item.Id,
                [.. item.RepoIds],
                RouteToBacklogCommandHandler.AttachmentFolder(item, attachmentFiles),
                cancellationToken)
            .ConfigureAwait(false);
        if (imported.IsFailure) return imported.Error;

        item.RouteToBacklog(imported.Value, [.. item.RepoIds], clock.GetUtcNow());

        await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);

        return new InboxRoutedDto(item.Id, imported.Value, notePath);
    }

    /// <summary>
    /// The drafted plan with a <c>plan</c> entry before it: titled as the item,
    /// carrying the plan's tag — the Roadmap Tag the import gives the item it lays
    /// out — the item's repositories as its scope, and the project note's Brain
    /// address as its <c>devbook:</c> token, which the import sets as the item's
    /// Knowledge Ref (ADR 0013, local ADR 0053). Internal so the shape can be
    /// pinned on its own.
    /// </summary>
    internal static string WithPlanEntry(string planMarkdown, string title, string planTag, IReadOnlyList<string> repoIds, string notePath)
    {
        var heading = string.Join(' ', (title ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (heading.Length == 0) heading = planTag;

        var tokens = new List<string> { "`plan`", $"`+{planTag}`" };
        tokens.AddRange(repoIds.Where(repo => !string.IsNullOrWhiteSpace(repo)).Select(repo => $"`repo:{repo.Trim()}`"));
        tokens.Add($"`devbook:{BrainNoteAddress.Format(notePath)}`");

        return $"# {heading}\n{string.Join(' ', tokens)}\n\n{planMarkdown.TrimStart('\r', '\n')}";
    }

    /// <summary>At least one line that opens an entry. The grammar's entry
    /// delimiter is a top-level heading, so a draft without one parses to
    /// nothing whatever else it says.</summary>
    private static bool LooksLikeAPlan(string markdown) =>
        markdown.Split('\n').Any(line => line.TrimStart('\r', ' ').StartsWith("# ", StringComparison.Ordinal));

    /// <summary>The item's plan tag: its title as a slug with the item's id on
    /// the end — see <see cref="InboxPlanTag.For"/> for the shape and why the
    /// id is there. Internal so the shape can be pinned on its own.</summary>
    internal static string PlanTag(string title, Guid itemId) => InboxPlanTag.For(title, itemId);
}
