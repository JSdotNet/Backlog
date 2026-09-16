using Backlog.Modules.Inbox.Abstractions.Services;

namespace Backlog.Desktop.UI.Inbox;

/// <summary>
/// The backlog tag source a host that has wired none stands in with: it offers
/// nothing.
/// <para>
/// A null object rather than a nullable field, so the picker's union never
/// branches on whether the backlog is reachable — it asks and gets an empty
/// list. The application hosts register the real adapter with the other inbox
/// cross-context adapters; a test that does not care about the backlog's words
/// gets this without composing one. The same shape Tasks.UI's
/// <c>EmptyRoadmapTagSource</c> takes.
/// </para>
/// </summary>
internal sealed class EmptyBacklogTagSource : IBacklogTagSource
{
    public static EmptyBacklogTagSource Instance { get; } = new();

    public Task<IReadOnlyList<string>> TagsInUseAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}
