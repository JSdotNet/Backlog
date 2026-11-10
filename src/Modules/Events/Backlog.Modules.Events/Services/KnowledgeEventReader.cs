using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Events.Abstractions.Services;
using Backlog.Modules.Events.DomainModels;

namespace Backlog.Modules.Events.Services;

/// <summary>
/// Answers the events read contract, <see cref="IKnowledgeEventReader"/>, from the
/// <see cref="IKnowledgeEventRepository"/>: the decided events, mapped to the published shape
/// so no aggregate leaves the module.
/// </summary>
internal sealed class KnowledgeEventReader(IKnowledgeEventRepository repository) : IKnowledgeEventReader
{
    public async Task<IReadOnlyList<KnowledgeEventDto>> ListDecidedAsync(CancellationToken cancellationToken = default)
    {
        var events = await repository.ListAsync(cancellationToken).ConfigureAwait(false);

        return [.. events.Where(knowledgeEvent => IsDecided(knowledgeEvent.State)).Select(ToDto)];
    }

    private static bool IsDecided(KnowledgeEventState state) =>
        state is KnowledgeEventState.Considering
            or KnowledgeEventState.Going
            or KnowledgeEventState.Attended
            or KnowledgeEventState.Harvested;

    private static KnowledgeEventDto ToDto(KnowledgeEvent knowledgeEvent) =>
        new(
            knowledgeEvent.Id,
            knowledgeEvent.Title,
            knowledgeEvent.State,
            knowledgeEvent.Start?.Date,
            knowledgeEvent.End?.Date,
            knowledgeEvent.Place?.Venue,
            knowledgeEvent.Place?.IsOnline ?? false,
            [.. knowledgeEvent.KeyDates.Select(keyDate => new KeyDateDto(keyDate.Date, keyDate.Label))]);
}
