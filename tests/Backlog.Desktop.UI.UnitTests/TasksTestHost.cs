using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sqlite;
using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.Extensions;
using Backlog.Modules.Roadmap;
using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.Extensions;
using Backlog.Infrastructure.Sqlite.Roadmap;
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
    public static ITaskRepository RepositoryFor(WorkspaceSettingsStore store, ITaskChangeSignal? changes = null) =>
        new RootedSqliteTaskRepository(() => store.RootDirectory, changes);

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
    /// cases over the stored plan document, in the same <c>backlog.db</c> under the
    /// same storage root the backlog uses.
    /// <para>
    /// A real plan in a real store rather than a stub, for the same reason the backlog
    /// gets one — the band's whole job is to draw what was stored, and a stub that
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
        PlanningFor(store, new SevenPointsAWeek());

    /// <summary>The same planning port, placing by the pace
    /// <paramref name="pace"/> keeps, measured from <paramref name="finished"/> as of
    /// <paramref name="clock"/> — for a host whose pace control and plan must read one
    /// pace.</summary>
    public static IRoadmapPlanning PlanningFor(
        WorkspaceSettingsStore store,
        IPlanningVelocitySettings pace,
        IRoadmapCompletedWork? finished = null,
        TimeProvider? clock = null) =>
        new ServiceCollection()
            .AddSingleton<IRoadmapPlanRepository>(
                new RootedSqliteRoadmapPlanRepository(() => store.RootDirectory))
            // Import places a window by the reader's pace, read from settings and
            // finished work a host answers through the cross-context adapters; seven
            // points a week, typed, with nothing finished is the pace of nobody having
            // chosen one.
            .AddSingleton(pace)
            .AddSingleton(finished ?? new NothingFinished())
            .AddSingleton(clock ?? TimeProvider.System)
            .AddRoadmapModule()
            .BuildServiceProvider()
            .GetRequiredService<IRoadmapPlanning>();

    /// <summary>
    /// The reader's paces as the roadmap reads them: the module's own service over a
    /// real pace file, with the finished work and the clock the test chooses.
    /// </summary>
    public static IPlanningPace PaceFor(
        PlanningVelocitySettingsStore paceFile,
        IRoadmapCompletedWork finished,
        TimeProvider clock) =>
        new ServiceCollection()
            .AddSingleton<IPlanningVelocitySettings>(new Backlog.Infrastructure.FileSystem.Roadmap.PlanningVelocitySource(paceFile))
            .AddSingleton(finished)
            .AddSingleton(clock)
            .AddRoadmapModule()
            .BuildServiceProvider()
            .GetRequiredService<IPlanningPace>();

    /// <summary>The pace of nobody having chosen one — seven points a week, typed, with
    /// nothing finished — for a host that renders the roadmap but is not about its
    /// pace.</summary>
    public static IPlanningPace UntouchedPace() =>
        new ServiceCollection()
            .AddSingleton<IPlanningVelocitySettings>(new SevenPointsAWeek())
            .AddSingleton<IRoadmapCompletedWork>(new NothingFinished())
            .AddRoadmapModule()
            .BuildServiceProvider()
            .GetRequiredService<IPlanningPace>();

    /// <summary>
    /// The imported plans the roadmap shelf offers, read by the real adapter from the
    /// backlog under the same storage root, with no repositories configured.
    /// </summary>
    public static IImportedPlanSource ImportedPlansFor(WorkspaceSettingsStore store) =>
        new Backlog.Infrastructure.FileSystem.Roadmap.ImportedPlanSource(EntriesFor(store), new NoRepositoryDirectory());

    /// <summary>The roadmap's change signal with no task signal behind it: a band
    /// under test reloads only when the test raises it.</summary>
    public static Backlog.Infrastructure.FileSystem.Roadmap.RoadmapWorkChanges WorkChanges() => new(null);

    public static TasksDesktopState StateFor(
        WorkspaceSettingsStore store,
        GitHubIntegration gitHub,
        TasksCopilotCli? copilot = null,
        IRoadmapTagSource? roadmapTags = null,
        IToastChannel? toasts = null) =>
        new(TaskStoreFor(store), EntriesFor(store), gitHub, copilot, roadmapTags, toasts);

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
    private sealed class SevenPointsAWeek : IPlanningVelocitySettings
    {
        public event Action? Changed { add { } remove { } }

        public decimal Manual => 7;

        public PaceSource Source => PaceSource.Manual;

        public string? SetManual(string? typed) => null;

        public string? Choose(PaceSource source) => null;
    }

    private sealed class NothingFinished : IRoadmapCompletedWork
    {
        public Task<IReadOnlyList<CompletedEffortDto>> CompletedSinceAsync(
            DateOnly since,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CompletedEffortDto>>([]);
    }

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
