using System.Text;

using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.CreatePlan;

/// <summary>Asks the plan drafter for an import plan about an item, hands the
/// plan to Tasks' import, and records the entries it made as the item's routing.
/// The same terminal outcome as <c>RouteToBacklog</c> reached through a
/// different door.</summary>
public sealed record CreatePlanCommand(Guid Id);

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
/// </summary>
public sealed class CreatePlanCommandHandler(
    IInboxItemRepository items,
    IInboxBacklogTarget target,
    TimeProvider clock,
    IInboxPlanDrafter? drafter = null)
    : ICommandHandler<CreatePlanCommand, Result<InboxRoutedDto>>
{
    /// <summary>The longest tag the slug becomes, suffix included. Long enough
    /// to keep a title recognisable on the metadata line, short enough that the
    /// line stays one.</summary>
    private const int MaxPlanTagLength = 40;

    /// <summary>How many hex digits of the item's id go on the end of the tag.
    /// Thirty-two random bits: enough that two items in one inbox cannot share
    /// a tag by accident, short enough that the title still reads.</summary>
    private const int PlanTagSuffixLength = 8;

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

        var request = new InboxPlanDraftRequestDto(
            item.Id,
            item.Title,
            item.BodyMd,
            item.SourceUrl,
            item.KindSlug,
            [.. item.Tags.Select(tag => tag.Name)],
            [.. item.RepoIds],
            PlanTag(item.Title, item.Id));

        var draft = await drafter.DraftAsync(request, cancellationToken).ConfigureAwait(false);
        if (draft.IsFailure) return draft.Error;

        if (!LooksLikeAPlan(draft.Value.PlanMarkdown)) return InboxErrors.PlanEmpty;

        // The item's repositories go along as the only ones the plan may name:
        // the drafter was told the same list, and a model that writes another
        // is refused by the adapter rather than allowed to register it.
        var imported = await target
            .ImportPlanAsync(draft.Value.PlanMarkdown, item.Id, [.. item.RepoIds], cancellationToken)
            .ConfigureAwait(false);
        if (imported.IsFailure) return imported.Error;

        item.RouteToBacklog(imported.Value, [.. item.RepoIds], clock.GetUtcNow());

        await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);

        return new InboxRoutedDto(item.Id, imported.Value);
    }

    /// <summary>At least one line that opens an entry. The grammar's entry
    /// delimiter is a top-level heading, so a draft without one parses to
    /// nothing whatever else it says.</summary>
    private static bool LooksLikeAPlan(string markdown) =>
        markdown.Split('\n').Any(line => line.TrimStart('\r', ' ').StartsWith("# ", StringComparison.Ordinal));

    /// <summary>
    /// The title as a tag, with the item's id on the end: lower-case letters,
    /// digits and hyphens, at most <see cref="MaxPlanTagLength"/> long, and
    /// opening with a letter so it is a legal <c>#tag</c> on the metadata line
    /// (the grammar reads a tag as <c>[A-Za-z][\w-]*</c>).
    /// <para>
    /// The suffix is what makes the tag the <em>item's</em> and not the
    /// title's. On the Tasks side the tag is the plan's id (ADR 0007's
    /// <c>import_plan_id</c>), and an import clears every not-started entry
    /// under it before writing its own — so two items with one title, or two
    /// whose titles both slug to nothing, would each tombstone the other's
    /// plan. The last eight hex digits rather than the first: a version-7 guid
    /// opens with a timestamp whose leading digits are shared by every id
    /// minted in the same minute, and the random bits are at the tail.
    /// </para>
    /// <para>Internal so the shape can be pinned on its own.</para>
    /// </summary>
    internal static string PlanTag(string title, Guid itemId)
    {
        var suffix = itemId.ToString("N")[^PlanTagSuffixLength..];
        var stemLength = MaxPlanTagLength - PlanTagSuffixLength - 1;

        var slug = new StringBuilder(stemLength);
        var pendingHyphen = false;

        foreach (var character in (title ?? string.Empty).ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                if (pendingHyphen && slug.Length > 0) slug.Append('-');
                pendingHyphen = false;
                slug.Append(character);
                if (slug.Length >= stemLength) break;
            }
            else
            {
                pendingHyphen = true;
            }
        }

        var value = slug.ToString().TrimEnd('-');

        if (value.Length == 0) return $"inbox-plan-{suffix}";
        if (char.IsAsciiLetter(value[0])) return $"{value}-{suffix}";

        const string prefix = "plan-";
        return prefix + value[..Math.Min(value.Length, stemLength - prefix.Length)].TrimEnd('-') + "-" + suffix;
    }
}
