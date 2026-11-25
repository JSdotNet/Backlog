using Backlog.SharedKernel.Changes;
using Backlog.UI.Components.Changes;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Restoring a change set in the Changes list (local ADR 0061 §4): Restore on an opened
/// row that still stands, the in-page confirmation with its counts and a choice per
/// conflict, and a restored row that says so and offers nothing more.
/// </summary>
public sealed class ChangeSetRestorePanelTests
{
    private static readonly DateTimeOffset Morning = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);

    private static readonly ChangeSet AgentRun = new(Guid.NewGuid(), ChangeActor.Agent, "transition", Morning) { Caller = "claude-code" };

    private static readonly DocumentKey Clean = new("task", "t-1");
    private static readonly DocumentKey Edited = new("task", "t-2");
    private static readonly DocumentKey Impediment = new("impediment", "i-1");

    private static readonly RestorePlan Plan = new(
        AgentRun.Id,
        AgentRun.Label,
        UndoneBy: null,
        [
            new(Clean, "Untouched since", RestoreAction.Revert, Conflict: false),
            new(Edited, "Edited since", RestoreAction.Revert, Conflict: true),
            new(Impediment, "Blocked on review", RestoreAction.Unsupported, Conflict: false),
        ]);

    [Fact]
    public void The_panel_names_how_many_it_restores_and_how_many_conflict()
    {
        using var context = Context();

        var panel = context.Render<ChangeSetRestorePanel>(parameters => parameters.Add(p => p.Plan, Plan));

        var summary = panel.Find("[data-testid='change-restore-summary']").TextContent;
        Assert.Contains("1 document will be restored", Normalize(summary), StringComparison.Ordinal);
        Assert.Contains("1 conflicts with a later change", Normalize(summary), StringComparison.Ordinal);

        var conflict = Assert.Single(panel.FindAll("[data-testid='change-restore-conflict']"));
        Assert.Equal("t-2", conflict.GetAttribute("data-document"));
        Assert.Contains("1 document cannot be written back", panel.Find("[data-testid='change-restore-unsupported']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_conflict_is_kept_by_default()
    {
        using var context = Context();
        IReadOnlySet<DocumentKey>? confirmed = null;

        var panel = context.Render<ChangeSetRestorePanel>(parameters => parameters
            .Add(p => p.Plan, Plan)
            .Add(p => p.OnConfirm, anyway => confirmed = anyway));

        Assert.Equal("true", panel.Find("[data-testid='change-restore-keep']").GetAttribute("aria-checked"));
        panel.Find("[data-testid='change-restore-confirm']").Click();

        Assert.NotNull(confirmed);
        Assert.Empty(confirmed);
    }

    [Fact]
    public void Restore_anyway_counts_the_conflict_and_hands_it_back()
    {
        using var context = Context();
        IReadOnlySet<DocumentKey>? confirmed = null;

        var panel = context.Render<ChangeSetRestorePanel>(parameters => parameters
            .Add(p => p.Plan, Plan)
            .Add(p => p.OnConfirm, anyway => confirmed = anyway));

        panel.Find("[data-testid='change-restore-anyway']").Click();

        Assert.Contains("2 documents will be restored", Normalize(panel.Find("[data-testid='change-restore-summary']").TextContent), StringComparison.Ordinal);
        panel.Find("[data-testid='change-restore-confirm']").Click();
        Assert.Equal([Edited], confirmed);
    }

    [Fact]
    public void Cancel_says_so_and_restores_nothing()
    {
        using var context = Context();
        var cancelled = false;
        var confirmed = false;

        var panel = context.Render<ChangeSetRestorePanel>(parameters => parameters
            .Add(p => p.Plan, Plan)
            .Add(p => p.OnConfirm, _ => confirmed = true)
            .Add(p => p.OnCancel, () => cancelled = true));

        panel.Find("[data-testid='change-restore-cancel']").Click();

        Assert.True(cancelled);
        Assert.False(confirmed);
    }

    [Fact]
    public void An_opened_row_offers_restore_only_when_the_host_can_restore()
    {
        using var context = Context();
        Guid? asked = null;

        var readOnly = context.Render<ChangeSetList>(parameters => parameters
            .Add(p => p.Entries, [new ChangeSetEntry(AgentRun, 2)])
            .Add(p => p.ChangedDocuments, new Dictionary<Guid, IReadOnlyList<ChangedDocument>> { [AgentRun.Id] = [] }));
        readOnly.Find("[data-testid='change-set-toggle']").Click();
        Assert.Empty(readOnly.FindAll("[data-testid='change-set-restore']"));

        var list = context.Render<ChangeSetList>(parameters => parameters
            .Add(p => p.Entries, [new ChangeSetEntry(AgentRun, 2)])
            .Add(p => p.ChangedDocuments, new Dictionary<Guid, IReadOnlyList<ChangedDocument>> { [AgentRun.Id] = [] })
            .Add(p => p.OnRestore, id => asked = id));
        list.Find("[data-testid='change-set-toggle']").Click();
        list.Find("[data-testid='change-set-restore']").Click();

        Assert.Equal(AgentRun.Id, asked);
    }

    [Fact]
    public void The_pending_restore_opens_in_its_own_row()
    {
        using var context = Context();

        var list = context.Render<ChangeSetList>(parameters => parameters
            .Add(p => p.Entries, [new ChangeSetEntry(AgentRun, 3)])
            .Add(p => p.ChangedDocuments, new Dictionary<Guid, IReadOnlyList<ChangedDocument>> { [AgentRun.Id] = [] })
            .Add(p => p.OnRestore, _ => { })
            .Add(p => p.PendingRestore, Plan));

        // A pending restore opens its row itself: the jump an agent's restore proposal
        // makes lands on the panel without the reader opening the row first.
        Assert.Single(list.FindAll("[data-testid='change-restore']"));
        Assert.Empty(list.FindAll("[data-testid='change-set-restore']"));
    }

    [Fact]
    public void Folding_the_row_of_a_pending_restore_cancels_it()
    {
        using var context = Context();
        var cancelled = false;

        var list = context.Render<ChangeSetList>(parameters => parameters
            .Add(p => p.Entries, [new ChangeSetEntry(AgentRun, 3)])
            .Add(p => p.ChangedDocuments, new Dictionary<Guid, IReadOnlyList<ChangedDocument>> { [AgentRun.Id] = [] })
            .Add(p => p.OnRestore, _ => { })
            .Add(p => p.PendingRestore, Plan)
            .Add(p => p.OnCancelRestore, () => cancelled = true));
        list.Find("[data-testid='change-set-toggle']").Click();

        Assert.True(cancelled);
    }

    [Fact]
    public void A_restored_change_set_says_so_and_offers_no_second_restore()
    {
        using var context = Context();
        var restored = AgentRun with { UndoneBy = Guid.NewGuid() };

        var list = context.Render<ChangeSetList>(parameters => parameters
            .Add(p => p.Entries, [new ChangeSetEntry(restored, 2)])
            .Add(p => p.ChangedDocuments, new Dictionary<Guid, IReadOnlyList<ChangedDocument>> { [AgentRun.Id] = [] })
            .Add(p => p.OnRestore, _ => { }));
        list.Find("[data-testid='change-set-toggle']").Click();

        Assert.Equal("Restored", list.Find("[data-testid='change-set-restored']").TextContent);
        Assert.Empty(list.FindAll("[data-testid='change-set-restore']"));
    }

    private static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }
}
