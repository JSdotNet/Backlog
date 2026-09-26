using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;

namespace Backlog.Infrastructure.Capture.Inbox;

/// <summary>The Inbox's lists, as the delivery needs them to file a capture by
/// name. Its own seam rather than <see cref="IInboxItems"/> itself, so the
/// delivery's tests say which lists exist without standing up the pane's whole
/// port.</summary>
internal interface IInboxListLookup
{
    Task<IReadOnlyList<InboxListDto>> ListsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Answers <see cref="IInboxListLookup"/> from the Inbox's published
/// snapshot — the lists the side menu shows.</summary>
internal sealed class InboxListLookup(IInboxItems items) : IInboxListLookup
{
    public async Task<IReadOnlyList<InboxListDto>> ListsAsync(CancellationToken cancellationToken = default) =>
        (await items.GetSnapshotAsync(cancellationToken).ConfigureAwait(false)).Lists;
}
