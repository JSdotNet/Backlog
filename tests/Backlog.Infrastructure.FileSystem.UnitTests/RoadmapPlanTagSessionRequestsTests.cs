using System.Reflection;

using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Infrastructure.FileSystem.Tasks;
using Backlog.Infrastructure.Sqlite.Sessions;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The plan filter chip's queue port, answered through the roadmap's plan session queue
/// (local ADR 0038 §2): a plan request read from the filter bar carries what the
/// roadmap's own read does, when it was requested included.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class RoadmapPlanTagSessionRequestsTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-plan-tag-session-requests", Guid.NewGuid().ToString("n"));
    private readonly FakeTimeProvider _clock = new(Noon);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task A_plan_request_read_from_the_filter_bar_carries_when_it_was_requested()
    {
        var queue = new SessionRequestActions(new SqliteSessionRequestStore(_root, _clock), clock: _clock);
        IPlanTagSessionRequests port = new RoadmapPlanTagSessionRequests(Unused<IRoadmapPlanning>(), queue);

        var requested = await port.RequestAsync("session-dispatch", "JSdotNet/Backlog", @"D:\Repos\Backlog", Ct);
        _clock.Advance(TimeSpan.FromMinutes(7));

        Assert.Equal(Noon, requested.RequestedAt);
        Assert.Equal(Noon, (await port.OpenAsync(Ct))["session-dispatch"].RequestedAt);
    }

    /// <summary>A port this case never calls: any call fails it.</summary>
    private static T Unused<T>() where T : class => DispatchProxy.Create<T, Throwing>();

    /// <summary>Public and unsealed because <see cref="DispatchProxy"/> derives from it.</summary>
    public class Throwing : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"{targetMethod?.Name} was not expected to be called.");
    }
}
