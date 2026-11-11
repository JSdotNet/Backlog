using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The rollup adapter's Brain read (local ADR 0053): the owner's notes, asked once per
/// gathering — once for a whole plan, not once per item — and a host with no Brain
/// port reads as a device with no Brain.
/// </summary>
public sealed class RoadmapItemRollupServiceBrainTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rollup-brain-" + Guid.NewGuid().ToString("N"));

    public RoadmapItemRollupServiceBrainTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public async Task An_item_s_brain_ref_resolves_from_the_brain_port()
    {
        var brain = new CountingBrain(new RoadmapBrainNote("projects/sync.md", null, "Sync rework", 5, []));
        var service = new RoadmapItemRollupService(new EmptyTaskItems(), () => _root, brainNotes: brain);

        var rollup = await service.GatherAsync(Item("sync", "#brain:projects/sync.md"), TestContext.Current.CancellationToken);

        var note = Assert.Single(rollup.BrainNotes);
        Assert.Equal("Sync rework", note.Title);
        Assert.Equal(5, note.Effort);
        Assert.Equal(1, brain.Reads);
    }

    [Fact]
    public async Task A_whole_plan_reads_the_brain_once()
    {
        var brain = new CountingBrain(new RoadmapBrainNote("projects/sync.md", null, "Sync rework", 5, ["sync", "other"]));
        var service = new RoadmapItemRollupService(new EmptyTaskItems(), () => _root, brainNotes: brain);

        var rollups = await service.GatherPlanAsync(
            new RoadmapPlanDto([Item("sync"), Item("other"), Item("third")], [], []),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, brain.Reads);
        Assert.Equal([1, 1, 0], rollups.Values.Select(rollup => rollup.BrainNotes.Count));
    }

    [Fact]
    public async Task A_host_with_no_brain_port_reads_every_brain_ref_unresolved()
    {
        var service = new RoadmapItemRollupService(new EmptyTaskItems(), () => _root);

        var rollup = await service.GatherAsync(Item("sync", "#brain:projects/sync.md"), TestContext.Current.CancellationToken);

        var note = Assert.Single(rollup.BrainNotes);
        Assert.True(note.Unresolved);
        Assert.Equal(RoadmapUnresolvedReason.NoBrain, note.UnresolvedBecause);
        Assert.Equal("#brain:projects/sync.md", note.Title);
    }

    [Fact]
    public async Task A_brain_port_with_no_brain_on_this_device_reads_its_refs_as_no_brain()
    {
        var service = new RoadmapItemRollupService(new EmptyTaskItems(), () => _root, brainNotes: new NoRoadmapBrainNotes());

        var rollup = await service.GatherAsync(Item("sync", "#brain:projects/sync.md"), TestContext.Current.CancellationToken);

        Assert.Equal(RoadmapUnresolvedReason.NoBrain, Assert.Single(rollup.BrainNotes).UnresolvedBecause);
    }

    private static RoadmapItemDto Item(string tag, params string[] knowledgeRefs) =>
        new(Guid.NewGuid(), "Ship it", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30),
            PlanningPriority.Medium, [], Lane: null, TaskId: null, DependsOn: [], Tag: tag, KnowledgeRefs: knowledgeRefs);

    private sealed class CountingBrain(params RoadmapBrainNote[] notes) : IRoadmapBrainNotes
    {
        public int Reads { get; private set; }

        public Task<IReadOnlyList<RoadmapBrainNote>?> ReadAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<RoadmapBrainNote>?>(notes);
        }
    }

    /// <summary>The Tasks facade with an empty backlog: the adapter only ever lists.</summary>
    private sealed class EmptyTaskItems : ITaskItems
    {
        public Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItemDto>>([]);

        public Task<Result<SavedTaskDto>> SaveFromTextAsync(Guid? id, string rawText, int order, string? sourceInboxId = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<ImportPlanResultDto>> ImportPlanAsync(string rawText, string? defaultRepo = null, IReadOnlyDictionary<string, string>? repoMatches = null, string? sourceInboxId = null, IReadOnlyDictionary<string, string>? sourceInboxIds = null, bool layOutOnRoadmap = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<TaskItemDto>> LinkToIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<TaskItemDto>> UnlinkFromIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<TaskItemDto>> SetDevbookReferencesAsync(Guid id, IReadOnlyList<string> references, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> RefileRepositoryTasksAsync(string repositoryId, string? moveToId, bool drop, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
