using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The rollup adapter's third read: the AI sessions linked to a gathered entry, asked
/// for only when one of them is needed to date an entry Tasks left undated.
/// </summary>
public sealed class RoadmapItemRollupServiceSessionTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "rollup-sessions-" + Guid.NewGuid().ToString("N"));

    public RoadmapItemRollupServiceSessionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public async Task A_finished_entry_with_no_dates_is_dated_by_its_linked_sessions()
    {
        var id = Guid.NewGuid();
        var sessions = new StubSessions(
            Session("worked", Now.AddDays(-10), Now.AddDays(-8)),
            Session("elsewhere", Now.AddDays(-40), Now.AddDays(-39)));
        var service = Service(sessions, Entry(id, EntryStatus.Done, "worked"));

        var rollups = await service.GatherPlanAsync(Plan(Item(id)), TestContext.Current.CancellationToken);

        var link = Assert.Single(Assert.Single(rollups.Values).BacklogEntries);
        Assert.Equal(DateOnly.FromDateTime(Now.AddDays(-10).LocalDateTime), link.StartedOn);
        Assert.Equal(DateOnly.FromDateTime(Now.AddDays(-8).LocalDateTime), link.CompletedOn);

        // A horizon reading, never the capped inventory, reaching back as far as the
        // Sessions context keeps records.
        var query = Assert.Single(sessions.Queries);
        Assert.Equal(Now - AgentSessionLimits.History, query.Horizon);
    }

    [Fact]
    public async Task Sessions_are_not_read_when_every_gathered_entry_is_already_dated()
    {
        var id = Guid.NewGuid();
        var sessions = new StubSessions(Session("worked", Now.AddDays(-10), Now.AddDays(-8)));
        var service = Service(
            sessions,
            Entry(id, EntryStatus.Done, "worked") with
            {
                StartedOn = new DateOnly(2026, 9, 1),
                CompletedOn = new DateOnly(2026, 9, 2)
            },
            // Undated and linked, but not gathered by the plan.
            Entry(Guid.NewGuid(), EntryStatus.Done, "worked"));

        await service.GatherPlanAsync(Plan(Item(id)), TestContext.Current.CancellationToken);

        Assert.Empty(sessions.Queries);
    }

    [Fact]
    public async Task An_entry_filed_before_the_horizon_widens_it()
    {
        var id = Guid.NewGuid();
        var created = Now.AddDays(-200);
        var sessions = new StubSessions();
        var service = Service(sessions, Entry(id, EntryStatus.Done, "old") with { CreatedAt = created });

        await service.GatherAsync(Item(id), TestContext.Current.CancellationToken);

        Assert.Equal(created, Assert.Single(sessions.Queries).Horizon);
    }

    [Fact]
    public async Task One_session_arriving_twice_spans_both_copies()
    {
        var id = Guid.NewGuid();
        var service = Service(
            new StubSessions(
                Session("twice", Now.AddDays(-6), Now.AddDays(-5)),
                Session("twice", null, Now.AddDays(-2))),
            Entry(id, EntryStatus.Done, "twice"));

        var rollup = await service.GatherAsync(Item(id), TestContext.Current.CancellationToken);

        var link = Assert.Single(rollup.BacklogEntries);
        Assert.Equal(DateOnly.FromDateTime(Now.AddDays(-6).LocalDateTime), link.StartedOn);
        Assert.Equal(DateOnly.FromDateTime(Now.AddDays(-2).LocalDateTime), link.CompletedOn);
    }

    private RoadmapItemRollupService Service(StubSessions sessions, params TaskItemDto[] backlog) =>
        new(new ListingTaskItems(backlog), () => _root, sessions, new FakeTimeProvider(Now));

    private static RoadmapItemDto Item(Guid taskId) =>
        new(Guid.NewGuid(), "Ship it", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30),
            PlanningPriority.Medium, [], Lane: null, TaskId: taskId, DependsOn: []);

    private static RoadmapPlanDto Plan(params RoadmapItemDto[] items) => new(items, [], []);

    private static TaskItemDto Entry(Guid id, EntryStatus status, params string[] sessionIds) =>
        new(id, "Worked", string.Empty, EntryType.Task, Priority.Medium, status, null, [], 0, 0, 0,
            [.. sessionIds.Select(session => new EntryProjectionDto("owner/repo", session, EntryProjectionDto.SessionTargetType))]);

    private static AgentSession Session(string id, DateTimeOffset? startedAt, DateTimeOffset lastActivityAt) =>
        new(id, AgentSessionKind.Claude, "tower", "DEV-TOWER", id, @"D:\Repos\Backlog", null, null,
            startedAt, lastActivityAt, AgentSessionState.Finished, null, AgentSessionOrigin.Local);

    private sealed class StubSessions(params AgentSession[] sessions) : IAgentSessionSource
    {
        public List<AgentSessionQuery> Queries { get; } = [];

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            GetSessionsAsync(AgentSessionQuery.Newest, cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(new AgentSessionCatalog(sessions, [], sessions.Length));
        }
    }

    /// <summary>The Tasks facade with a fixed list and nothing else: the adapter only
    /// ever reads.</summary>
    private sealed class ListingTaskItems(params TaskItemDto[] entries) : ITaskItems
    {
        public Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItemDto>>(entries);

        public Task<Result<SavedTaskDto>> SaveFromTextAsync(Guid? id, string rawText, int order, string? sourceInboxId = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<ImportPlanResultDto>> ImportPlanAsync(string rawText, string? defaultRepo = null, IReadOnlyDictionary<string, string>? repoMatches = null, string? sourceInboxId = null, IReadOnlyDictionary<string, string>? sourceInboxIds = null, bool layOutOnRoadmap = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<TaskItemDto>> LinkToIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<TaskItemDto>> SetDevbookReferencesAsync(Guid id, IReadOnlyList<string> references, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
