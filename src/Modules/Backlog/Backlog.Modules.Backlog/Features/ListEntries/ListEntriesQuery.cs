using Backlog.Modules.Backlog.Abstractions.DataTransferObjects;
using Backlog.Modules.Backlog.Services;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Backlog.Features.ListEntries;

/// <summary>Everything in the backlog, in rank order.</summary>
public sealed record ListEntriesQuery;

public sealed class ListEntriesQueryHandler(IBacklogRepository entries)
    : IQueryHandler<ListEntriesQuery, IReadOnlyList<BacklogEntryDto>>
{
    public async Task<IReadOnlyList<BacklogEntryDto>> Handle(
        ListEntriesQuery query,
        CancellationToken cancellationToken = default)
    {
        var summaries = await entries.ListAsync(cancellationToken);
        var results = new List<BacklogEntryDto>(summaries.Count);

        foreach (var summary in summaries)
        {
            // The index is a derived summary; the markdown is the truth. An entry
            // the index still lists but the file no longer holds is simply gone,
            // not an error to raise at somebody opening their backlog.
            var entry = await entries.GetAsync(summary.Id, cancellationToken);
            if (entry is null) continue;

            results.Add(entry.ToDto());
        }

        return results;
    }
}
