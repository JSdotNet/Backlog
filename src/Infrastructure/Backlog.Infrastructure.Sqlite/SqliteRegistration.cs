using Backlog.Infrastructure.Sqlite.Inbox;
using Backlog.Infrastructure.Sqlite.Roadmap;
using Backlog.Infrastructure.Sqlite.Sessions;
using Backlog.Modules.Inbox;
using Backlog.Modules.Roadmap;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Sqlite;

/// <summary>
/// The stores in <c>backlog.db</c>, registered in one place both desktop
/// heads reach through their shared composition, so which adapter answers each
/// module's repository port cannot differ between them.
/// </summary>
public static class SqliteRegistration
{
    /// <summary>
    /// Registers the task repository, the roadmap plan, the inbox tables and the
    /// Claude Code api_request table, each a
    /// singleton rooted at <paramref name="rootDirectory"/>.
    /// <para>
    /// The root is asked per call rather than fixed here, because somebody can move
    /// their backlog while the app is open and the database follows the folder. It
    /// arrives as a delegate over the provider because this project may not see
    /// the workspace settings that know it.
    /// </para>
    /// <para>
    /// One instance behind every port a store answers — the roadmap row is both the
    /// plan the module loads and the document that travels to the person's other
    /// devices (local ADR 0018), and the inbox store answers both of the module's
    /// repository ports — so two ports can never follow two different roots.
    /// </para>
    /// <para>
    /// The task repository takes the Tasks module's change signal lazily, through
    /// <c>GetService</c>, so whether <c>AddTasksModule()</c> comes before or after
    /// this call does not matter. The signal is what lets the sync loop push an edit
    /// seconds after it is saved rather than on its five-minute tick.
    /// </para>
    /// </summary>
    public static IServiceCollection AddSqlite(this IServiceCollection services, Func<IServiceProvider, string> rootDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(rootDirectory);

        services.AddSingleton<ITaskRepository>(sp =>
            new RootedSqliteTaskRepository(
                () => rootDirectory(sp),
                sp.GetService<ITaskChangeSignal>()));

        services.AddSingleton(sp =>
            new RootedSqliteRoadmapPlanRepository(() => rootDirectory(sp)));
        services.AddSingleton<IRoadmapPlanRepository>(sp => sp.GetRequiredService<RootedSqliteRoadmapPlanRepository>());
        services.AddSingleton<IRoadmapReplicaStore>(sp => sp.GetRequiredService<RootedSqliteRoadmapPlanRepository>());

        services.AddSingleton<RootedSqliteInboxRepository>(sp =>
            new RootedSqliteInboxRepository(() => rootDirectory(sp)));
        services.AddSingleton<IInboxItemRepository>(sp => sp.GetRequiredService<RootedSqliteInboxRepository>());
        services.AddSingleton<IInboxOrganizerRepository>(sp => sp.GetRequiredService<RootedSqliteInboxRepository>());

        // The Claude Code api_request events the /v1/logs endpoint keeps (local ADR
        // 0024): the Sessions context's table, in the same file and following the
        // same root.
        services.AddSingleton<IClaudeApiRequestStore>(sp =>
            new RootedSqliteClaudeApiRequestStore(() => rootDirectory(sp)));

        return services;
    }
}
