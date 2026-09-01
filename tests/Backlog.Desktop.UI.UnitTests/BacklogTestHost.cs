using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sqlite;
using Backlog.Desktop.UI.BacklogManagement;
using Backlog.Modules.Backlog;
using Backlog.Modules.Backlog.Abstractions.Services;
using Backlog.Modules.Backlog.Extensions;
using Backlog.Modules.Roadmap;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.Extensions;
using Backlog.Infrastructure.FileSystem.Roadmap;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Composes the Backlog module the way a host does, so these tests drive the
/// desktop through the real use cases rather than a stand-in for them.
/// <para>
/// The desktop project itself can no longer see the module implementation — it
/// only knows <see cref="ITaskItems"/> — but a test is a host, and wiring
/// the handlers to a real file store is exactly what makes these tests worth
/// having.
/// </para>
/// </summary>
internal static class BacklogTestHost
{
    public static ITaskRepository RepositoryFor(WorkspaceSettingsStore store) =>
        new RootedSqliteTaskRepository(() => store.RootDirectory);

    public static ITaskItems EntriesFor(WorkspaceSettingsStore store) =>
        EntriesFor(RepositoryFor(store));

    /// <summary>
    /// The module's use cases over one repository.
    /// <para>
    /// The provider composing them is deliberately not disposed and not held: the
    /// handlers it builds are the return value, they outlive this call by
    /// definition, and disposing the provider would take them with it. Nothing it
    /// creates owns a thread, a timer, or a handle — the repository it is handed
    /// was constructed outside and opens its SQLite connection per call — so a
    /// rooted provider here costs the test process a few objects until it exits
    /// and nothing else. Contrast <c>BacklogDesktopState</c>, which does arm
    /// timers and is disposed by every harness that builds one.
    /// </para>
    /// </summary>
    public static ITaskItems EntriesFor(ITaskRepository repository) =>
        new ServiceCollection()
            .AddSingleton(repository)
            .AddSingleton<IRepositoryDirectory, NoRepositoryDirectory>()
            .AddBacklogModule()
            .BuildServiceProvider()
            .GetRequiredService<ITaskItems>();

    /// <summary>
    /// Backlog Management's store port over a workspace settings file. The two are
    /// composed by a host, and a test is a host.
    /// </summary>
    public static IBacklogStore BacklogStoreFor(WorkspaceSettingsStore settings) =>
        new WorkspaceBacklogStore(settings);

    /// <summary>
    /// Roadmap Planning composed the way a host composes it: the module's own use
    /// cases over the JSON plan document in the same storage root the backlog uses.
    /// <para>
    /// A real plan on disk rather than a stub, for the same reason the backlog gets
    /// one — the band's whole job is to draw what was stored, and a stub that
    /// returns a fixture would make every test about the band pass whether the
    /// storage worked or not. A test that wants an empty plan simply does not write
    /// one.
    /// </para>
    /// <para>
    /// The provider is rooted rather than disposed, for the reason
    /// <see cref="EntriesFor(ITaskRepository)"/> gives.
    /// </para>
    /// </summary>
    public static IRoadmapPlanning PlanningFor(WorkspaceSettingsStore store) =>
        new ServiceCollection()
            .AddSingleton<IRoadmapPlanRepository>(
                new RootedJsonRoadmapPlanRepository(() => store.RootDirectory))
            .AddRoadmapModule()
            .BuildServiceProvider()
            .GetRequiredService<IRoadmapPlanning>();

    public static BacklogDesktopState StateFor(
        WorkspaceSettingsStore store,
        GitHubIntegration gitHub,
        BacklogCopilotCli? copilot = null,
        IRoadmapTagSource? roadmapTags = null,
        IBacklogRefreshSettings? refreshSettings = null) =>
        new(BacklogStoreFor(store), EntriesFor(store), gitHub, copilot, roadmapTags, refreshSettings);

    /// <summary>
    /// The repository directory a test host stands in with: it knows nothing and
    /// registers nothing.
    /// <para>
    /// A workspace with no configured repositories is the state these tests are
    /// actually in — none of them wires the Repositories screen — and under it an
    /// imported <c>repo:</c> name simply stays as written, which is what every
    /// existing assertion about entry text expects. The real settings-backed
    /// directory is asserted on directly in
    /// <c>BacklogRepositoryDirectoryTests</c>, and Import's use of it in
    /// <c>ImportPlanTests</c>; standing one up here would only put a temporary
    /// settings file behind every unrelated pane test.
    /// </para>
    /// </summary>
    private sealed class NoRepositoryDirectory : IRepositoryDirectory
    {
        public IReadOnlyList<BacklogRepositoryRef> Repositories => [];

        public BacklogRepositoryRef? Resolve(string name) => null;

        public BacklogRepositoryRef Register(string name) => new(name, name, name);
    }
}
