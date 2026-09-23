using Backlog.Modules.Tasks.Abstractions.Services;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The two backlog tools: what they narrow on, and what they refuse.
/// </summary>
public class WorkToolsTests
{
    private static readonly TasksRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");
    private static readonly TasksRepositoryRef Other = new("other", "JSdotNet", "Other");

    /// <summary>
    /// An entry is filed against an <c>owner/name</c> id and not an alias, so the
    /// filter has to compare the resolved id. A tool that compared the alias
    /// would answer with nothing at all for every repository whose alias and name
    /// differ — which is every one somebody has renamed.
    /// </summary>
    [Fact]
    public async Task List_entries_narrows_on_the_repository_id_and_not_the_alias()
    {
        var entries = new FakeTaskItems(
            Entries.Entry("Second", order: 2, repoIds: ["JSdotNet/Backlog"]),
            Entries.Entry("First", order: 1, repoIds: ["JSdotNet/Backlog"]),
            Entries.Entry("Filed by alias", order: 0, repoIds: ["backlog"]),
            Entries.Entry("Somewhere else", order: 0, repoIds: ["JSdotNet/Other"]),
            Entries.Entry("Unfiled", order: 0));

        var tools = new WorkTools(entries, new FakeRepositoryDirectory([Backlog, Other]));

        var answer = await tools.ListEntriesAsync("JSdotNet/Backlog", TestContext.Current.CancellationToken);

        Assert.Equal("JSdotNet/Backlog", answer.Repository);
        Assert.Equal("backlog", answer.RepositoryAlias);
        Assert.Equal(["First", "Second"], answer.Entries.Select(entry => entry.Title));
        Assert.Equal(2, answer.Count);
    }

    /// <summary>The list comes back in rank order however the port hands it
    /// over. A session reading a backlog is reading a ranking.</summary>
    [Fact]
    public async Task List_entries_answers_in_rank_order()
    {
        var entries = new FakeTaskItems(
            Entries.Entry("Third", order: 30, repoIds: ["JSdotNet/Backlog"]),
            Entries.Entry("First", order: 10, repoIds: ["JSdotNet/Backlog"]),
            Entries.Entry("Second", order: 20, repoIds: ["JSdotNet/Backlog"]));

        var tools = new WorkTools(entries, new FakeRepositoryDirectory([Backlog]));

        var answer = await tools.ListEntriesAsync("JSdotNet/Backlog", TestContext.Current.CancellationToken);

        Assert.Equal(["First", "Second", "Third"], answer.Entries.Select(entry => entry.Title));
    }

    /// <summary>
    /// An <c>owner/name</c> the registry has never seen is an ordinary answer
    /// that says so — and never a registration. "A session mentioning a
    /// repository is not a plan introducing one" (local ADR 0012 §4).
    /// </summary>
    [Fact]
    public async Task An_unknown_repository_is_an_error_naming_it_and_never_a_registration()
    {
        var directory = new FakeRepositoryDirectory([Backlog]);
        var tools = new WorkTools(new FakeTaskItems(), directory);

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            tools.ListEntriesAsync("JSdotNet/Nowhere", TestContext.Current.CancellationToken));

        Assert.Contains("repository.not_found", failure.Message, StringComparison.Ordinal);
        Assert.Contains("JSdotNet/Nowhere", failure.Message, StringComparison.Ordinal);
        Assert.Equal(["JSdotNet/Nowhere"], directory.Resolved);
        Assert.Empty(directory.Registered);
    }

    /// <summary>
    /// The stored plan id carries the plan tag's <c>+</c> sigil, and the caller's
    /// value is used exactly as it arrived. Stripping or adding one would answer
    /// for a plan nobody named.
    /// </summary>
    [Fact]
    public async Task Plan_items_match_the_stored_plan_id_ordinally_sigil_and_all()
    {
        var entries = new FakeTaskItems(
            Entries.Entry("In the plan, second", order: 2, importPlanId: "+backlog-mcp-server", importItemId: "two"),
            Entries.Entry("In the plan, first", order: 1, importPlanId: "+backlog-mcp-server", importItemId: "one"),
            Entries.Entry("Sigil stripped", order: 0, importPlanId: "backlog-mcp-server"),
            Entries.Entry("Another plan", order: 0, importPlanId: "+dashboard-headers"),
            Entries.Entry("Typed by hand", order: 0));

        var tools = new WorkTools(entries, new FakeRepositoryDirectory([Backlog]));

        var answer = await tools.GetPlanItemsAsync("+backlog-mcp-server", TestContext.Current.CancellationToken);

        Assert.Equal("+backlog-mcp-server", answer.PlanId);
        Assert.Equal(["In the plan, first", "In the plan, second"], answer.Entries.Select(entry => entry.Title));
        Assert.Equal(["one", "two"], answer.Entries.Select(entry => entry.PlanItemId));
    }

    /// <summary>Ordinal means ordinal: a plan tag differing only in case is a
    /// different tag, because the grammar keeps the two apart.</summary>
    [Fact]
    public async Task A_plan_id_differing_only_in_case_is_a_different_plan()
    {
        var entries = new FakeTaskItems(Entries.Entry("In the plan", importPlanId: "+backlog-mcp-server"));

        var tools = new WorkTools(entries, new FakeRepositoryDirectory([Backlog]));

        var answer = await tools.GetPlanItemsAsync("+Backlog-MCP-Server", TestContext.Current.CancellationToken);

        Assert.Empty(answer.Entries);
    }

    /// <summary>The enums cross as the module's published wire tokens rather than
    /// as ordinals or member names, so what a session reads is what the store
    /// holds and what the grammar writes.</summary>
    [Fact]
    public async Task An_entry_carries_the_published_wire_tokens()
    {
        var entries = new FakeTaskItems(Entries.Entry(
            "In flight",
            repoIds: ["JSdotNet/Backlog"],
            status: Modules.Tasks.Abstractions.EntryStatus.InProgress,
            priority: Modules.Tasks.Abstractions.Priority.Critical));

        var tools = new WorkTools(entries, new FakeRepositoryDirectory([Backlog]));

        var answer = await tools.ListEntriesAsync("JSdotNet/Backlog", TestContext.Current.CancellationToken);

        var entry = Assert.Single(answer.Entries);
        Assert.Equal("in_progress", entry.Status);
        Assert.Equal("critical", entry.Priority);
        Assert.Equal("task", entry.Type);
    }
}
