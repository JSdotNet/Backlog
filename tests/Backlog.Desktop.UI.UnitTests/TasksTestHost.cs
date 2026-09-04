using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sqlite;
using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.Extensions;
using Backlog.Modules.Roadmap;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.Extensions;
using Backlog.Infrastructure.FileSystem.Roadmap;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Composes the Tasks module the way a host does, so these tests drive the
/// desktop through the real use cases rather than a stand-in for them.
/// <para>
/// The desktop project itself can no longer see the module implementation — it
/// only knows <see cref="ITaskItems"/> — but a test is a host, and wiring
/// the handlers to a real file store is exactly what makes these tests worth
/// having.
/// </para>
/// </summary>
internal static class TasksTestHost
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
    /// and nothing else. Contrast <c>TasksDesktopState</c>, which does arm
    /// timers and is disposed by every harness that builds one.
    /// </para>
    /// </summary>
    public static ITaskItems EntriesFor(ITaskRepository repository) =>
        new ServiceCollection()
            .AddSingleton(repository)
            .AddSingleton<IRepositoryDirectory, NoRepositoryDirectory>()
            .AddTasksModule()
            .BuildServiceProvider()
            .GetRequiredService<ITaskItems>();

    /// <summary>
    /// Tasks' store port over a workspace settings file. The two are
    /// composed by a host, and a test is a host.
    /// </summary>
    public static ITaskStore TaskStoreFor(WorkspaceSettingsStore settings) =>
        new WorkspaceTaskStore(settings);

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

    public static TasksDesktopState StateFor(
        WorkspaceSettingsStore store,
        GitHubIntegration gitHub,
        TasksCopilotCli? copilot = null,
        IRoadmapTagSource? roadmapTags = null,
        ITasksRefreshSettings? refreshSettings = null,
        IToastChannel? toasts = null) =>
        new(TaskStoreFor(store), EntriesFor(store), gitHub, copilot, roadmapTags, refreshSettings, toasts);

    /// <summary>
    /// The notification channel a screen publishes on and MainLayout's tray reads
    /// back, registered the way an application host registers it.
    /// <para>
    /// Any test that renders <c>TasksPane</c> or <c>Home</c> needs it: both take a
    /// hard <c>@inject IToastChannel</c>, because a screen that silently lost the
    /// only feedback a reader gets is worse than one that fails to construct.
    /// The concrete type is registered as well as the interface so a test can read
    /// <c>Visible</c> back without casting.
    /// </para>
    /// </summary>
    public static void AddToastChannel(IServiceCollection services)
    {
        services.AddSingleton<ToastChannel>();
        services.AddSingleton<IToastChannel>(sp => sp.GetRequiredService<ToastChannel>());
    }

    /// <summary>
    /// The repository directory a test host stands in with: it knows nothing and
    /// registers nothing.
    /// <para>
    /// A workspace with no configured repositories is the state these tests are
    /// actually in — none of them wires the Repositories screen — and under it an
    /// imported <c>repo:</c> name simply stays as written, which is what every
    /// existing assertion about entry text expects. The real settings-backed
    /// directory is asserted on directly in
    /// <c>TasksRepositoryDirectoryTests</c>, and Import's use of it in
    /// <c>ImportPlanTests</c>; standing one up here would only put a temporary
    /// settings file behind every unrelated pane test.
    /// </para>
    /// </summary>
    /// <summary>
    /// A registry with nothing configured, which is the first-run state and the
    /// state every test here wants: none of them is about repository resolution,
    /// and a name that resolves to nothing is stored exactly as it was typed.
    /// </summary>
    private sealed class NoRepositoryDirectory : IRepositoryDirectory
    {
        public IReadOnlyList<TasksRepositoryRef> Repositories => [];

        public TasksRepositoryRef? Resolve(string name) => null;

        /// <summary>Answers the way the real adapter answers a bare name — owner
        /// and name standing in as the alias — so the <c>Id</c> it hands back is
        /// the same <c>name/name</c> placeholder Settings would show. It forgets
        /// immediately, which is the one thing it is for.</summary>
        public TasksRepositoryRef Register(string name) => new(name, name, name);
    }
}
