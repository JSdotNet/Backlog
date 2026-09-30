using Backlog.Infrastructure.Sqlite.Inbox;
using Backlog.Infrastructure.Sqlite.Roadmap;
using Backlog.Modules.Inbox;
using Backlog.Modules.Roadmap;
using Backlog.Modules.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// What a host gets for calling <c>AddSqlite()</c>: the three stores in
/// <c>backlog.db</c>, each a singleton rooted at the folder the host names, and
/// one instance behind every port a store answers so two ports can never follow
/// two different roots.
/// </summary>
public sealed class SqliteRegistrationTests
{
    [Fact]
    public void The_task_repository_is_a_singleton_rooted_where_the_host_says()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-sqlite-registration", Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddSqlite(_ => root);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ITaskRepository));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<RootedSqliteTaskRepository>(provider.GetRequiredService<ITaskRepository>());
    }

    [Fact]
    public void One_roadmap_store_answers_both_of_its_ports()
    {
        var services = new ServiceCollection();
        services.AddSqlite(_ => Path.GetTempPath());

        Assert.All(
            services.Where(d => d.ServiceType == typeof(IRoadmapPlanRepository)
                || d.ServiceType == typeof(IRoadmapReplicaStore)
                || d.ServiceType == typeof(RootedSqliteRoadmapPlanRepository)),
            d => Assert.Equal(ServiceLifetime.Singleton, d.Lifetime));

        using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<RootedSqliteRoadmapPlanRepository>();
        Assert.Same(store, provider.GetRequiredService<IRoadmapPlanRepository>());
        Assert.Same(store, provider.GetRequiredService<IRoadmapReplicaStore>());
    }

    [Fact]
    public void One_inbox_store_answers_both_of_its_ports()
    {
        var services = new ServiceCollection();
        services.AddSqlite(_ => Path.GetTempPath());

        Assert.All(
            services.Where(d => d.ServiceType == typeof(IInboxItemRepository)
                || d.ServiceType == typeof(IInboxOrganizerRepository)
                || d.ServiceType == typeof(RootedSqliteInboxRepository)),
            d => Assert.Equal(ServiceLifetime.Singleton, d.Lifetime));

        using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<RootedSqliteInboxRepository>();
        Assert.Same(store, provider.GetRequiredService<IInboxItemRepository>());
        Assert.Same(store, provider.GetRequiredService<IInboxOrganizerRepository>());
    }
}
