using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.ResurfaceDueItems;

/// <summary>
/// The resurface sweep: every deferred item whose review date is today or
/// earlier returns to the queue as unprocessed. Answers how many moved.
/// <para>
/// A sweep and not a scheduler. The pane runs it each time it opens, which is
/// every moment a single person on one desktop could see the difference — an
/// item due while the pane is closed is back before anyone looks. A background
/// job would add a timer to wake a queue nobody is reading.
/// </para>
/// <para>
/// "Today" is the local date of the injected clock: a review date is a day on
/// the reader's calendar, not a UTC instant, so an item deferred until Friday
/// comes back on Friday morning wherever the desktop is.
/// </para>
/// </summary>
public sealed record ResurfaceDueItemsCommand;

public sealed class ResurfaceDueItemsCommandHandler(IInboxItemRepository items, TimeProvider clock)
    : ICommandHandler<ResurfaceDueItemsCommand, Result<int>>
{
    public async Task<Result<int>> Handle(ResurfaceDueItemsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var resurfaced = 0;

        foreach (var item in await items.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!item.ResurfaceIfDue(today, now)) continue;

            await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);
            resurfaced++;
        }

        return Result.Success(resurfaced);
    }
}
