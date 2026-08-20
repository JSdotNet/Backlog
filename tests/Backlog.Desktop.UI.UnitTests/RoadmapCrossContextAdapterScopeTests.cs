using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Infrastructure.Sqlite;
using Backlog.Modules.Backlog;
using Backlog.Modules.Backlog.Abstractions.Services;
using Backlog.Modules.Backlog.Extensions;
using Backlog.Modules.Roadmap;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Both hosts compose the roadmap/backlog use cases and the two cross-context
/// adapters that join them. The adapters capture services the modules register as
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
        services.AddSingleton<IRoadmapPlanRepository>(_ => new RootedJsonRoadmapPlanRepository(() => store.RootDirectory));
        services.AddBacklogModule();
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

        Assert.IsType<RoadmapPlanTagSource>(tagSource);
        Assert.IsType<RoadmapItemRollupService>(rollup);
    }
}
