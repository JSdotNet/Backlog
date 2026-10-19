using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Progress;

/// <summary>
/// Progress's <see cref="IBlockedTaskSource"/>, answered from Tasks' published
/// <see cref="ITaskItems"/> alone: every task carrying the stored blocked mark — its
/// <c>blocked:</c> token — that is not Done or Archived. It reads and never writes.
/// Resolved per call, for the reason <see cref="ProgressTaskSource"/> gives.
/// </summary>
public sealed class BlockedTaskSource(Func<ITaskItems> tasks) : IBlockedTaskSource
{
    public async Task<IReadOnlyList<BlockedTaskDto>> ReadBlockedAsync(CancellationToken cancellationToken = default)
    {
        var backlog = await tasks().ListAsync(cancellationToken).ConfigureAwait(false);
        return Select(backlog);
    }

    /// <summary>The blocked, unfinished tasks among <paramref name="backlog"/>.</summary>
    internal static IReadOnlyList<BlockedTaskDto> Select(IReadOnlyList<TaskItemDto> backlog) =>
        [
            .. backlog
                .Where(entry => entry.Status is not (EntryStatus.Done or EntryStatus.Archived))
                .Where(entry => entry.BlockedSince is not null)
                .Select(entry => new BlockedTaskDto(entry.Id, entry.Title, entry.BlockedSince!.Value))
        ];
}
