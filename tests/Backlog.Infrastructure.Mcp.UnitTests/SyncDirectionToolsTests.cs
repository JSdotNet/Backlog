using System.Text.Json;

using Backlog.Modules.Tasks.Abstractions.Services;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// <c>list_sync_directions</c> as a sweep meets it over MCP: each unit sent comes
/// back with <c>units.mjs</c>'s own <c>sync</c>, <c>syncFrom</c> and
/// <c>syncLevel</c>, resolved nearest wins over Backlog's direction map, and every
/// stated entry beside them. <c>syncFrom</c> is in the path form the unit was sent
/// in, so a sweep can match it against the ids it already holds.
/// </summary>
public sealed class SyncDirectionToolsTests
{
    private const string Repository = "JSdotNet/Backlog";

    private static readonly TasksRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");

    private static SyncDirectionTools Tools(out FakeDevbookSyncDirectionStore store)
    {
        store = new FakeDevbookSyncDirectionStore();

        // This repository's own two statements.
        store.Set("backlog", "domain/inbox/context.md", "pull", "markdown");
        store.Set("backlog", "domain/sessions/context.md", "push", "markdown");

        return new SyncDirectionTools(store, new FakeRepositoryDirectory([Backlog]));
    }

    [Fact]
    public void A_unit_under_a_stating_context_answers_its_direction_from_the_context_in_its_own_path_form()
    {
        var tools = Tools(out _);

        var answer = tools.ListSyncDirectionsOf(Repository, [
            ".devbook/domain/inbox/domain.md#inbox-item",
            ".domain/sessions/domain.md#question-relay",
            ".devbook/domain/inbox#shared-types",
        ]);

        Assert.Equal(
            [
                new SyncUnitPayload(".devbook/domain/inbox/domain.md#inbox-item", "pull", ".devbook/domain/inbox/context.md", "context"),
                new SyncUnitPayload(".domain/sessions/domain.md#question-relay", "push", ".domain/sessions/context.md", "context"),
                new SyncUnitPayload(".devbook/domain/inbox#shared-types", "pull", ".devbook/domain/inbox/context.md", "context"),
            ],
            answer.Units);
    }

    [Theory]
    [InlineData(".devbook/domain/tasks/domain.md#task")]
    [InlineData(".devbook/domain/capture#shared-types")]
    [InlineData(".devbook/arc42/building-blocks/sync.md")]
    [InlineData("src/Program.cs")]
    public void A_unit_nothing_states_a_direction_for_is_report_from_default(string unit)
    {
        var tools = Tools(out _);

        var answer = tools.ListSyncDirectionsOf(Repository, [unit]);

        Assert.Equal([new SyncUnitPayload(unit, "report", null, "default")], answer.Units);
    }

    [Fact]
    public void A_unit_level_statement_wins_and_answers_unit()
    {
        var tools = Tools(out var store);
        store.Set("backlog", ".devbook/domain/inbox/domain.md#inbox-item", "off", "DEV-TOWER");

        var unit = Assert.Single(tools.ListSyncDirectionsOf(Repository, [".devbook/domain/inbox/domain.md#inbox-item"]).Units);

        Assert.Equal(new SyncUnitPayload(".devbook/domain/inbox/domain.md#inbox-item", "off", ".devbook/domain/inbox/domain.md#inbox-item", "unit"), unit);
    }

    [Fact]
    public void Page_and_folder_are_spelt_as_units_mjs_spells_them()
    {
        var tools = Tools(out var store);
        store.Set("backlog", "domain/tasks/domain.md", "sync", "DEV-TOWER");
        store.Set("backlog", "arc42/05-building-block-view.md", "push", "DEV-TOWER");

        var answer = tools.ListSyncDirectionsOf(Repository, [".devbook/domain/tasks/domain.md#task", ".devbook/arc42/building-blocks/sync.md"]);

        Assert.Equal(
            [
                new SyncUnitPayload(".devbook/domain/tasks/domain.md#task", "sync", ".devbook/domain/tasks/domain.md", "page"),
                new SyncUnitPayload(".devbook/arc42/building-blocks/sync.md", "push", ".devbook/arc42/05-building-block-view.md", "folder"),
            ],
            answer.Units);
    }

    [Fact]
    public void Every_stated_entry_is_listed_with_who_and_when_and_no_units_without_any_asked()
    {
        var tools = Tools(out var store);

        var answer = tools.ListSyncDirectionsOf(Repository);

        Assert.Equal("JSdotNet/Backlog", answer.Repository);
        Assert.Empty(answer.Units);
        Assert.Equal(
            [
                new SyncStatementPayload("domain/inbox/context.md", "pull", "markdown", "2026-11-02T09:30:00Z"),
                new SyncStatementPayload("domain/sessions/context.md", "push", "markdown", "2026-11-02T09:30:00Z"),
            ],
            answer.Stated);
        Assert.Equal(["backlog"], store.AskedAliases);
    }

    [Fact]
    public void The_answer_carries_units_mjs_field_names_on_the_wire()
    {
        var tools = Tools(out _);

        var wire = JsonSerializer.Serialize(
            tools.ListSyncDirectionsOf(Repository, [".devbook/domain/inbox/domain.md#inbox-item"]),
            McpJsonUtilities.DefaultOptions);

        using var document = JsonDocument.Parse(wire);
        var unit = document.RootElement.GetProperty("units")[0];
        Assert.Equal(["sync", "syncFrom", "syncLevel", "unit"], [.. unit.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)]);
        Assert.Equal(
            ["address", "at", "by", "direction"],
            [.. document.RootElement.GetProperty("stated")[0].EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public void The_brain_has_no_sync_units_and_is_refused()
    {
        var tools = Tools(out var store);

        var refused = Assert.Throws<McpException>(() => tools.ListSyncDirectionsOf(BrainMcpScope.Value, [".devbook/domain/inbox/domain.md#inbox-item"]));

        Assert.Contains(SyncDirectionTools.BrainCode, refused.Message, StringComparison.Ordinal);
        Assert.Empty(store.AskedAliases);
    }

    [Fact]
    public void A_repository_this_host_does_not_know_is_refused_before_the_map_is_read()
    {
        var tools = Tools(out var store);

        var refused = Assert.Throws<McpException>(() => tools.ListSyncDirectionsOf("someone/else"));

        Assert.Contains(RepositoryScope.NotFoundCode, refused.Message, StringComparison.Ordinal);
        Assert.Empty(store.AskedAliases);
    }
}
