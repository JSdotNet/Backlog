using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Features.ImportPlan;
using Backlog.SharedKernel.Changes;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// "Create plan" from the Inbox is one change set (local ADR 0061 §2): the import an
/// Inbox decision makes writes under that decision's change set, so restoring it takes
/// back the item and the plan it became together. Any other import opens its own.
/// </summary>
public sealed class ImportPlanJoinsTriageChangeSetTests
{
    private const string InboxItem = "0199a3f2-7c41-7d1a-9d0f-3d2c5e6f7a8b";
    private const string Plan = "# First prompt\n`prompt` `+myplan` `id:first`\n\n# Second prompt\n`prompt` `+myplan` `id:second`\n";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_import_routed_from_the_inbox_joins_the_triage_change_set()
    {
        var store = new InMemoryTaskRepository();
        var triage = ChangeSet.Start(ChangeActor.Person, "Triage of Ship it");

        using (ChangeContext.Enter(triage))
        {
            var result = await new ImportPlanCommandHandler(store, new FakeRepositoryDirectory())
                .Handle(new ImportPlanCommand(Plan, SourceInboxId: InboxItem), Ct);
            Assert.IsType<Ok<ImportPlanResultDto>>(result.Value);
        }

        Assert.Equal(2, store.SavedUnder.Count);
        Assert.All(store.SavedUnder.Values, saved => Assert.Same(triage, saved));
    }

    [Fact]
    public async Task A_batch_routed_from_the_inbox_joins_the_triage_change_set()
    {
        var store = new InMemoryTaskRepository();
        var triage = ChangeSet.Start(ChangeActor.Person, "Triage of 2 items");
        var sources = new Dictionary<string, string> { ["first"] = InboxItem, ["second"] = InboxItem };

        using (ChangeContext.Enter(triage))
        {
            await new ImportPlanCommandHandler(store, new FakeRepositoryDirectory())
                .Handle(new ImportPlanCommand(Plan, SourceInboxIds: sources), Ct);
        }

        Assert.All(store.SavedUnder.Values, saved => Assert.Same(triage, saved));
    }

    [Fact]
    public async Task A_pasted_import_inside_a_change_set_still_opens_its_own()
    {
        var store = new InMemoryTaskRepository();
        var gesture = ChangeSet.Start(ChangeActor.Person, "pasting a plan");

        using (ChangeContext.Enter(gesture))
        {
            await new ImportPlanCommandHandler(store, new FakeRepositoryDirectory())
                .Handle(new ImportPlanCommand(Plan), Ct);
        }

        var saved = Assert.Single(store.SavedUnder.Values.Distinct());
        Assert.NotNull(saved);
        Assert.NotSame(gesture, saved);
        Assert.Equal(ChangeActor.Person, saved.Actor);
        Assert.Equal("Import of +myplan", saved.Label);
    }

    [Fact]
    public async Task An_inbox_import_with_no_change_set_current_opens_its_own()
    {
        var store = new InMemoryTaskRepository();

        await new ImportPlanCommandHandler(store, new FakeRepositoryDirectory())
            .Handle(new ImportPlanCommand(Plan, SourceInboxId: InboxItem), Ct);

        var saved = Assert.Single(store.SavedUnder.Values.Distinct());
        Assert.Equal("Import of +myplan", saved?.Label);
    }
}
