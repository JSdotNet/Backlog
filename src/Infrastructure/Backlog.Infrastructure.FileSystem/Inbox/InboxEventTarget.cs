using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.FileSystem.Inbox;

/// <summary>
/// Answers the Inbox's <see cref="IInboxEventTarget"/> port over Events' published
/// <see cref="IKnowledgeEvents"/>: an item routed To Event becomes a Spotted knowledge
/// event (<c>.devbook/domain/inbox/features.md#route-to-event</c>).
/// <para>
/// Here rather than in either module, because neither context may see the other.
/// The route is offered while <see cref="EventsFeatures.Events"/> is on — Events'
/// key, which the Inbox cannot name. With no Events composed, or no feature settings
/// to ask, it is not offered, the flag's off answer.
/// </para>
/// <para>
/// The item's link is the event's link. The watched page picks the series and its
/// title names a new one; which series that is, is Events' to decide.
/// </para>
/// </summary>
internal sealed class InboxEventTarget(IKnowledgeEvents? events = null, IAppFeatureSettings? features = null) : IInboxEventTarget
{
    public bool IsOffered => events is not null && features is not null && features.IsEnabled(EventsFeatures.Events);

    public async Task<Result<Guid>> SpotEventAsync(InboxEventRequestDto request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsOffered) return Result.Failure<Guid>(InboxErrors.EventsUnavailable);

        var facts = request.Facts;
        Result<SpottedKnowledgeEventDto> spotted;
        try
        {
            spotted = await events!.SpotAsync(
                new SpotKnowledgeEventDto(
                    request.Title,
                    request.SourceUrl,
                    DateOf(facts?.Start),
                    DateOf(facts?.End),
                    facts?.Place,
                    facts?.PageUrl,
                    facts?.PageTitle),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // A store that cannot write throws rather than answering; the person is
            // told the event was not made, and the item stays open.
            return Result.Failure<Guid>(InboxErrors.EventNotMade(failure.Message));
        }

        return spotted.IsSuccess
            ? Result.Success(spotted.Value.EventId)
            : Result.Failure<Guid>(InboxErrors.EventNotMade(spotted.Error.Message));
    }

    private static EventDateDto? DateOf(InboxEventDateDto? date) =>
        date is null ? null : new EventDateDto(date.Date, date.Time, date.Offset);
}
