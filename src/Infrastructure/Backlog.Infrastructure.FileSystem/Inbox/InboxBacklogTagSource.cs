using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Inbox;

/// <summary>
/// Answers the Inbox's <see cref="IBacklogTagSource"/> port over Tasks'
/// published <see cref="ITaskItems"/>: the words the backlog already files
/// entries under, so the Inbox's picker can offer them.
/// <para>
/// Here beside <see cref="InboxBacklogTarget"/> and for its reason: the Inbox
/// may not see Tasks, and this adapter may see both. The one translation it
/// makes is which stored tags are words: a backlog entry's tag list carries a
/// sigil for a person or a roadmap item and none for a general tag, and only
/// the general ones are tags an inbox item can wear and routing will write —
/// <see cref="InboxBacklogTarget.Compose"/> drops any tag that does not open
/// with a letter, so offering one would offer a pick that vanishes on the way
/// to the backlog.
/// </para>
/// </summary>
internal sealed class InboxBacklogTagSource(ITaskItems tasks) : IBacklogTagSource
{
    public async Task<IReadOnlyList<string>> TagsInUseAsync(CancellationToken cancellationToken = default)
    {
        var entries = await tasks.ListAsync(cancellationToken).ConfigureAwait(false);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tags = new List<string>();

        // First appearance wins, the way the roadmap's source keeps its order:
        // the backlog lists its entries in rank order and the picker should not
        // shuffle a vocabulary the reader already knows.
        foreach (var tag in entries.SelectMany(entry => entry.Tags))
        {
            if (string.IsNullOrWhiteSpace(tag) || !char.IsAsciiLetter(tag[0])) continue;
            if (seen.Add(tag)) tags.Add(tag);
        }

        return tags;
    }
}
