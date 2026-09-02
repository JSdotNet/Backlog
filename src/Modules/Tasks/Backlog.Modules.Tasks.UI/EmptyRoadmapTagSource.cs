using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// The roadmap tag source a host that has wired none stands in with: it offers
/// nothing.
/// <para>
/// A null object rather than a nullable field, so the picker's union code never
/// branches on whether a plan is reachable — it asks and gets an empty list. A host
/// with a roadmap registers the real adapter; a test that does not care about
/// planned tags gets this without having to compose one.
/// </para>
/// </summary>
internal sealed class EmptyRoadmapTagSource : IRoadmapTagSource
{
    public static EmptyRoadmapTagSource Instance { get; } = new();

    public Task<IReadOnlyList<string>> TagsInUseAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}
