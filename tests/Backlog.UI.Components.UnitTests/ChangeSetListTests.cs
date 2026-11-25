using Backlog.SharedKernel.Changes;
using Backlog.UI.Components.Changes;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The change journal read back: rows newest first as handed over, who made each,
/// the filter chips, and an opened row's documents before and now.
/// </summary>
public sealed class ChangeSetListTests
{
    private static readonly DateTimeOffset Morning = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);

    private static readonly ChangeSet AgentRun = new(Guid.NewGuid(), ChangeActor.Agent, "transition", Morning.AddMinutes(30))
    {
        Caller = "claude-code",
        SessionId = "session-42",
    };

    private static readonly ChangeSet MyEdit = new(Guid.NewGuid(), ChangeActor.Person, "editing “Plan”", Morning.AddMinutes(20));

    private static readonly ChangeSet SyncPage = new(Guid.NewGuid(), ChangeActor.Sync, "sync page", Morning.AddMinutes(10));

    private static readonly ChangeSet Stranger = new(Guid.NewGuid(), ChangeActor.Unknown, null, Morning);

    private static readonly ChangeSet Housekeeping = new(Guid.NewGuid(), ChangeActor.System, "recurrence", Morning.AddMinutes(-10));

    private static readonly IReadOnlyList<ChangeSetEntry> Entries =
    [
        new(AgentRun, 3),
        new(MyEdit, 1),
        new(SyncPage, 2),
        new(Stranger, 1),
        new(Housekeeping, 1),
    ];

    [Fact]
    public void Each_row_shows_who_what_when_and_how_many_in_the_order_handed_over()
    {
        using var context = Context();

        var list = Render(context);

        var rows = list.FindAll("[data-testid='change-set']");
        Assert.Equal(
            [AgentRun.Id.ToString(), MyEdit.Id.ToString(), SyncPage.Id.ToString(), Stranger.Id.ToString(), Housekeeping.Id.ToString()],
            rows.Select(row => row.GetAttribute("data-change-set")));
        Assert.Equal(
            ["Agent", "You", "Sync", "Unknown writer", "Backlog"],
            list.FindAll("[data-testid='change-set-actor']").Select(badge => badge.TextContent));
        Assert.Equal(
            ["transition", "editing “Plan”", "sync page", "No label", "recurrence"],
            list.FindAll("[data-testid='change-set-label']").Select(label => label.TextContent));
        Assert.Equal(
            ["3 documents", "1 document", "2 documents", "1 document", "1 document"],
            list.FindAll("[data-testid='change-set-count']").Select(count => count.TextContent));
        Assert.Equal("at 09:30", rows[0].QuerySelector("[data-testid='change-set-time']")!.TextContent);
    }

    [Fact]
    public void Only_an_agents_row_names_its_caller_and_session()
    {
        using var context = Context();

        var agent = Assert.Single(Render(context).FindAll("[data-testid='change-set-agent']"));

        Assert.Contains("claude-code", agent.TextContent, StringComparison.Ordinal);
        Assert.Contains("session session-42", agent.TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ChangeSetFilter.Agents, "Agent")]
    [InlineData(ChangeSetFilter.Me, "You")]
    [InlineData(ChangeSetFilter.Sync, "Sync")]
    [InlineData(ChangeSetFilter.Unknown, "Unknown writer")]
    public void A_filter_keeps_only_the_change_sets_its_actor_made(ChangeSetFilter filter, string actor)
    {
        using var context = Context();

        var list = Render(context, filter);

        Assert.Equal(actor, Assert.Single(list.FindAll("[data-testid='change-set-actor']")).TextContent);
    }

    [Fact]
    public void The_chips_count_what_each_keeps_and_a_press_reports_the_filter()
    {
        using var context = Context();
        ChangeSetFilter? chosen = null;

        var list = context.Render<ChangeSetList>(parameters => parameters
            .Add(p => p.Entries, Entries)
            .Add(p => p.FilterChanged, filter => chosen = filter));

        Assert.Equal(
            ["All5", "Agents1", "Me1", "Sync1", "Unknown1"],
            list.FindAll("[data-testid^='change-sets-filter-']").Select(chip => string.Concat(chip.TextContent.Where(c => !char.IsWhiteSpace(c)))));
        Assert.Equal("true", list.Find("[data-testid='change-sets-filter-all']").GetAttribute("aria-checked"));

        list.Find("[data-testid='change-sets-filter-agents']").Click();

        Assert.Equal(ChangeSetFilter.Agents, chosen);
    }

    [Fact]
    public void Opening_a_row_asks_for_its_documents_and_says_it_is_reading_them()
    {
        using var context = Context();
        Guid? asked = null;

        var list = context.Render<ChangeSetList>(parameters => parameters
            .Add(p => p.Entries, Entries)
            .Add(p => p.OnOpen, id => asked = id));

        list.FindAll("[data-testid='change-set-toggle']")[0].Click();

        Assert.Equal(AgentRun.Id, asked);
        Assert.Contains("Reading what it changed", list.Find("[data-testid='change-set-reading']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_opened_row_shows_each_document_before_beside_now()
    {
        using var context = Context();
        var documents = new Dictionary<Guid, IReadOnlyList<ChangedDocument>>
        {
            [AgentRun.Id] =
            [
                new("task", "t-1", "Plan the trip", "# Plan the trip\n`task` `*medium` `!ready`\n", "# Plan the trip\n`task` `*medium` `!done`\n"),
                new("task", "t-2", "Brand new", null, "# Brand new\n`task` `*medium` `!draft`\n"),
            ],
        };

        var list = context.Render<ChangeSetList>(parameters => parameters
            .Add(p => p.Entries, Entries)
            .Add(p => p.ChangedDocuments, documents));

        list.FindAll("[data-testid='change-set-toggle']")[0].Click();

        var shown = list.FindAll("[data-testid='changed-document']");
        Assert.Equal(["Plan the trip", "Brand new"], shown.Select(document => document.QuerySelector("[data-testid='changed-document-title']")!.TextContent));
        Assert.Equal(2, list.FindAll("[data-testid='changed-document-compare']").Count);
        Assert.Contains("!ready", shown[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("!done", shown[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Before — it did not exist", shown[1].TextContent, StringComparison.Ordinal);
        Assert.Equal("true", list.FindAll("[data-testid='change-set-toggle']")[0].GetAttribute("aria-expanded"));
    }

    [Fact]
    public void Another_kinds_fields_read_one_per_line()
    {
        using var context = Context();
        var documents = new Dictionary<Guid, IReadOnlyList<ChangedDocument>>
        {
            [MyEdit.Id] = [new("goal", "g-1", "Ship it", "id: g-1\ntitle: Ship it", null)],
        };

        var list = context.Render<ChangeSetList>(parameters => parameters
            .Add(p => p.Entries, Entries)
            .Add(p => p.ChangedDocuments, documents));

        list.FindAll("[data-testid='change-set-toggle']")[1].Click();

        var document = list.Find("[data-testid='changed-document']");
        Assert.Equal("goal", document.QuerySelector("[data-testid='changed-document-kind']")!.TextContent);
        Assert.Contains("title: Ship it", document.TextContent, StringComparison.Ordinal);
        Assert.Contains("Now — not available", document.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_journal_and_an_empty_filter_say_so()
    {
        using var context = Context();

        var empty = context.Render<ChangeSetList>(parameters => parameters.Add(p => p.Entries, []));
        Assert.Contains("No changes recorded yet", empty.Find("[data-testid='change-sets-empty']").TextContent, StringComparison.Ordinal);

        var filtered = context.Render<ChangeSetList>(parameters => parameters
            .Add(p => p.Entries, [new ChangeSetEntry(MyEdit, 1)])
            .Add(p => p.Filter, ChangeSetFilter.Agents));
        Assert.Contains("Nothing under this filter", filtered.Find("[data-testid='change-sets-empty']").TextContent, StringComparison.Ordinal);
    }

    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    private static IRenderedComponent<ChangeSetList> Render(BunitContext context, ChangeSetFilter filter = ChangeSetFilter.All) =>
        context.Render<ChangeSetList>(parameters => parameters
            .Add(p => p.Entries, Entries)
            .Add(p => p.Filter, filter)
            .Add(p => p.TimeFormat, at => $"at {at.UtcDateTime:HH:mm}"));
}
