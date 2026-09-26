using Backlog.Desktop.UI.Tasks;
using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sqlite;
using Backlog.Infrastructure.Sqlite.Roadmap;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.Extensions;
using Backlog.Modules.Roadmap;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.Extensions;
using Backlog.Modules.Roadmap.Features.ImportPlanItems;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Both hosts compose the roadmap/backlog use cases and the adapters that answer
/// what Roadmap asks of the world outside it. The two cross-context ones capture
/// services the modules register as
/// <c>Scoped</c> (<see cref="IRoadmapPlanning"/> and <see cref="ITaskItems"/>), so a
/// captive dependency — a singleton over a scoped service — makes the whole app
/// return HTTP 500 on the first render, even though every use-case unit test still
/// passes.
/// <para>
/// This builds the service provider the way a host does — with scope validation on —
/// exercising the real <see cref="RoadmapCrossContextAdapterRegistration.AddRoadmapCrossContextAdapters"/>
/// and the real module registrations rather than a re-declared copy. If either
/// adapter is reintroduced as a singleton over a scoped dependency, either
/// <c>ValidateOnBuild</c> fails the build (type-based registration) or resolving the
/// adapter from a scope throws "Cannot resolve scoped service ... from root provider"
/// (factory registration) — the exact runtime fault QA hit.
/// </para>
/// </summary>
public sealed class RoadmapCrossContextAdapterScopeTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        "backlog-scope-validation",
        Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Composes the same graph both hosts do for the roadmap/backlog feature: the
    /// workspace root, the two rooted repositories the modules read through, both
    /// modules, and — the thing under guard — the shared cross-context adapter
    /// registration. Everything the hosts feed the adapters is real, so the test
    /// cannot pass against a registration the host would fail on.
    /// </summary>
    private ServiceProvider BuildHostLikeProvider()
    {
        var store = new WorkspaceSettingsStore(_tempDir, Path.Combine(_tempDir, "settings.json"));

        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton<ITaskRepository>(_ => new RootedSqliteTaskRepository(() => store.RootDirectory));
        services.AddSingleton<IRoadmapPlanRepository>(_ => new RootedSqliteRoadmapPlanRepository(() => store.RootDirectory));
        // Both hosts register the GitHub settings store and then the Backlog
        // adapter over it, so Import can resolve a plan's `repo:` names. It is
        // here for the same reason everything else is: a graph missing it is not
        // the graph a host builds, and ValidateOnBuild would say so.
        services.AddSingleton(new GitHubSettingsStore(Path.Combine(_tempDir, "github", "github.json")));
        // The reader's pace, which IPlanningVelocity is answered over. Here for the
        // same reason the GitHub store is: a graph missing it is not the graph a
        // host builds, and ValidateOnBuild would say so.
        services.AddSingleton(new PlanningVelocitySettingsStore(Path.Combine(_tempDir, "velocity", "planning-velocity.json")));
        services.AddTasksAdapters();
        services.AddTasksModule();
        services.AddRoadmapModule();
        services.AddRoadmapCrossContextAdapters();

        // ValidateOnBuild turns a captive dependency into a build-time throw for the
        // type-based adapter; ValidateScopes turns it into a resolve-time throw for
        // the factory-based one. A host builds its provider the same way.
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    [Fact]
    public void The_host_provider_builds_with_scope_validation()
    {
        using var provider = BuildHostLikeProvider();

        Assert.NotNull(provider);
    }

    [Fact]
    public void The_cross_context_adapters_resolve_from_a_request_scope()
    {
        using var provider = BuildHostLikeProvider();

        using var scope = provider.CreateScope();

        var tagSource = scope.ServiceProvider.GetRequiredService<IRoadmapTagSource>();
        var rollup = scope.ServiceProvider.GetRequiredService<IRoadmapItemRollup>();
        var velocity = scope.ServiceProvider.GetRequiredService<IPlanningVelocitySettings>();
        var finished = scope.ServiceProvider.GetRequiredService<IRoadmapCompletedWork>();
        var importedPlans = scope.ServiceProvider.GetRequiredService<IImportedPlanSource>();
        var intake = scope.ServiceProvider.GetRequiredService<IRoadmapPlanIntake>();

        Assert.IsType<RoadmapPlanTagSource>(tagSource);
        Assert.IsType<RoadmapPlanIntake>(intake);
        Assert.IsType<RoadmapItemRollupService>(rollup);
        Assert.IsType<PlanningVelocitySource>(velocity);
        Assert.IsType<RoadmapCompletedWork>(finished);
        Assert.IsType<ImportedPlanSource>(importedPlans);
    }

    [Fact]
    public void Roadmaps_own_import_command_resolves_in_the_host_graph()
    {
        // It asks for the reader's pace, which the module works out from what only
        // the cross-context adapters answer: the typed pace and the finished work.
        using var provider = BuildHostLikeProvider();
        using var scope = provider.CreateScope();

        var import = scope.ServiceProvider.GetRequiredService<
            ICommandHandler<ImportPlanItemsCommand, Result<PlanImportResultDto>>>();

        Assert.IsType<ImportPlanItemsCommandHandler>(import);
    }

    /// <summary>
    /// The backlog's plan import reaches the roadmap importer, which asks the pace,
    /// which counts finished work from the backlog. Taken eagerly that is a loop the
    /// container cannot see through its factories, and a scope resolving any of them
    /// deadlocks — so both ends are resolved here, in both orders, and the pace is
    /// read, under a deadline that turns a hang back into a failure.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_roadmaps_pace_and_the_backlog_resolve_together_in_the_host_graph(bool backlogFirst)
    {
        using var provider = BuildHostLikeProvider();

        var resolved = Task.Run(async () =>
        {
            using var scope = provider.CreateScope();

            if (backlogFirst) Assert.NotNull(scope.ServiceProvider.GetRequiredService<ITaskItems>());

            var pace = scope.ServiceProvider.GetRequiredService<IPlanningPace>();
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPlanningVelocity>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<ITaskItems>());

            return await pace.ReadAsync();
        });

        var paces = await resolved.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        Assert.Equal(7m, paces.InUse);
    }
}
