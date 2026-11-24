using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Features.CreateTaskIfAbsent;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// Creating a task from its text under an id the caller chose, only when this machine holds no
/// task with that id. A caller that derives the id from what the task is for — so two machines
/// asking for the same task ask for the same id — files it once here, and the replica folds the
/// two machines' copies into one.
/// <para>
/// A task held under the id, live or deleted, is left exactly as it is: the call writes nothing
/// and succeeds, so a deleted task stays deleted. The text is refused on the terms an ordinary
/// create refuses it: no title, or a <c>plan</c> entry.
/// </para>
/// </summary>
public sealed class CreateTaskIfAbsentTests
{
    private static readonly Guid Id = new("6f1d2c3b-4a5e-8f60-9a1b-2c3d4e5f6a7b");

    [Fact]
    public async Task A_task_no_one_holds_is_created_under_the_given_id_with_its_text_and_rank()
    {
        var store = new InMemoryTaskRepository();

        var result = await Handle(store, "# Harvest Techorama Belgium\n`task`\n", order: 7);

        Assert.True(Assert.IsType<Ok<bool>>(result.Value).Value);
        var created = store.Entries[Id];
        Assert.Equal("Harvest Techorama Belgium", created.Title);
        Assert.Equal(EntryType.Task, created.Type);
        Assert.Equal(7, created.Order);
    }

    [Fact]
    public async Task A_task_held_live_is_left_as_it_is()
    {
        var held = Held("Harvest Techorama");
        var store = new InMemoryTaskRepository(held);

        var result = await Handle(store, "# Harvest Techorama Belgium\n`task`\n");

        var ok = Assert.IsType<Ok<bool>>(result.Value);
        Assert.False(ok.Value);
        Assert.Equal(0, store.Writes);
        Assert.Equal("Harvest Techorama", store.Entries[Id].Title);
    }

    [Fact]
    public async Task A_task_held_deleted_stays_deleted()
    {
        var held = Held("Harvest Techorama Belgium");
        held.MarkDeleted();
        var store = new InMemoryTaskRepository(held);

        var result = await Handle(store, "# Harvest Techorama Belgium\n`task`\n");

        var ok = Assert.IsType<Ok<bool>>(result.Value);
        Assert.False(ok.Value);
        Assert.Equal(0, store.Writes);
        Assert.NotNull(store.Entries[Id].DeletedAt);
    }

    [Fact]
    public async Task Text_with_no_title_is_refused_and_nothing_is_written()
    {
        var store = new InMemoryTaskRepository();

        // A heading marker with nothing typed after it: the editor's half-typed state.
        var result = await Handle(store, "# \n`task`\n");

        Assert.Equal("entry.needs_title", Assert.IsType<Error>(result.Value).Code);
        Assert.Empty(store.Entries);
    }

    [Fact]
    public async Task A_plan_entry_is_refused_and_nothing_is_written()
    {
        var store = new InMemoryTaskRepository();

        var result = await Handle(store, "# Imported plans on the roadmap\n`plan` `+roadmap-imported-plans`\n");

        Assert.Equal(TaskEntryErrorCodes.PlanBelongsToImport, Assert.IsType<Error>(result.Value).Code);
        Assert.Empty(store.Entries);
    }

    private static TaskItem Held(string title) =>
        new(Id, title, string.Empty, EntryType.Task, EntryStatus.Draft, Priority.Medium, null, null, null, DateTimeOffset.UtcNow);

    private static Task<Result<bool>> Handle(InMemoryTaskRepository store, string rawText, int order = 0) =>
        new CreateTaskIfAbsentCommandHandler(store, new FakeRepositoryDirectory())
            .Handle(new CreateTaskIfAbsentCommand(Id, rawText, order), TestContext.Current.CancellationToken);
}
