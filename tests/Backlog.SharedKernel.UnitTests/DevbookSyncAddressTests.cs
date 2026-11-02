using Backlog.SharedKernel.Devbook;

namespace Backlog.SharedKernel.UnitTests;

/// <summary>
/// A block's address in Backlog's direction map, the blocks a unit's direction is
/// read from, and the resolution over them — the mirror of <c>syncSources</c> in
/// the vendored <c>metadata.mjs</c> and of <c>levelOf</c>/<c>resolveDirection</c>
/// in <c>units.mjs</c>.
/// <para>
/// The resolution table is what <c>node .devbook/_tools/devbook-meta/units.mjs
/// --json</c> answered on this repository when its <c>domain/inbox/context.md</c>
/// stated <c>pull</c> and its <c>domain/sessions/context.md</c> <c>push</c>, and
/// nothing else stated one.
/// </para>
/// </summary>
public sealed class DevbookSyncAddressTests
{
    /// <summary>This repository's two statements, as the map keeps them.</summary>
    private static readonly Dictionary<string, string> Repository = new(StringComparer.Ordinal)
    {
        ["domain/inbox/context.md"] = "pull",
        ["domain/sessions/context.md"] = "push",
    };

    [Theory]
    [InlineData(DevbookFolder.Domain, ".devbook/domain/inbox/domain.md#inbox-item", "domain/inbox/domain.md#inbox-item")]
    [InlineData(DevbookFolder.Domain, ".domain/inbox/domain.md#inbox-item", "domain/inbox/domain.md#inbox-item")]
    [InlineData(DevbookFolder.Domain, "domain/inbox/domain.md#inbox-item", "domain/inbox/domain.md#inbox-item")]
    [InlineData(DevbookFolder.Domain, "inbox/domain.md#inbox-item", "domain/inbox/domain.md#inbox-item")]
    [InlineData(DevbookFolder.Domain, "./.devbook/domain/inbox/context.md", "domain/inbox/context.md")]
    [InlineData(DevbookFolder.Domain, ".devbook\\domain\\inbox\\context.md", "domain/inbox/context.md")]
    [InlineData(DevbookFolder.Arc42, ".devbook/arc42/building-blocks/sync.md", "arc42/building-blocks/sync.md")]
    [InlineData(DevbookFolder.Design, "component-libraries.md#button", "design/component-libraries.md#button")]
    public void An_address_is_the_folder_and_the_path_below_it_with_the_slug_kept(DevbookFolder folder, string path, string expected) =>
        Assert.Equal(expected, DevbookSync.Address(folder, path));

    [Fact]
    public void Folder_relative_still_drops_the_slug()
    {
        Assert.Equal("inbox/domain.md", DevbookSync.FolderRelative(DevbookFolder.Domain, ".devbook/domain/inbox/domain.md#inbox-item"));
    }

    [Theory]
    [InlineData(".devbook/domain/inbox/domain.md#inbox-item", DevbookFolder.Domain, "domain/inbox/domain.md#inbox-item")]
    [InlineData(".domain/inbox/domain.md#inbox-item", DevbookFolder.Domain, "domain/inbox/domain.md#inbox-item")]
    [InlineData(".devbook/domain/inbox#shared-types", DevbookFolder.Domain, "domain/inbox#shared-types")]
    [InlineData(".devbook/arc42/building-blocks/sync.md", DevbookFolder.Arc42, "arc42/building-blocks/sync.md")]
    [InlineData(".arc42/05-building-block-view.md", DevbookFolder.Arc42, "arc42/05-building-block-view.md")]
    [InlineData("design/component-libraries.md#button", DevbookFolder.Design, "design/component-libraries.md#button")]
    [InlineData(".devbook/tech/technology-graph.md", DevbookFolder.Tech, "tech/technology-graph.md")]
    public void A_unit_id_as_units_mjs_prints_it_has_an_address(string unit, DevbookFolder folder, string address)
    {
        Assert.True(DevbookSync.TryAddress(unit, out var named, out var normalised));
        Assert.Equal(folder, named);
        Assert.Equal(address, normalised);
    }

    [Theory]
    [InlineData("src/Core/Backlog.SharedKernel/Devbook/DevbookSync.cs")]
    [InlineData("README.md")]
    [InlineData("")]
    [InlineData(null)]
    public void A_path_in_no_devbook_folder_has_none(string? path)
    {
        Assert.False(DevbookSync.TryAddress(path, out var folder, out var address));
        Assert.Equal(DevbookFolder.Unknown, folder);
        Assert.Empty(address);
    }

    [Fact]
    public void An_aggregate_reads_unit_page_context_folder_in_that_order()
    {
        Assert.Equal(
            [
                (DevbookSyncLevel.Unit, "domain/orders/domain.md#order"),
                (DevbookSyncLevel.Page, "domain/orders/domain.md"),
                (DevbookSyncLevel.Context, "domain/orders/context.md"),
                (DevbookSyncLevel.Folder, "domain/context-map.md"),
            ],
            DevbookSync.Sources(".devbook/domain/orders/domain.md#order"));
    }

    [Fact]
    public void A_switch_chapters_page_is_its_context_listed_once()
    {
        Assert.Equal(
            [
                (DevbookSyncLevel.Unit, "domain/orders/context.md#dark-mode"),
                (DevbookSyncLevel.Context, "domain/orders/context.md"),
                (DevbookSyncLevel.Folder, "domain/context-map.md"),
            ],
            DevbookSync.Sources("domain/orders/context.md#dark-mode"));
    }

    [Fact]
    public void A_shared_types_unit_skips_its_own_root_and_reads_its_page_its_context_then_its_folder()
    {
        Assert.Equal(
            [
                (DevbookSyncLevel.Page, "domain/orders/domain.md"),
                (DevbookSyncLevel.Context, "domain/orders/context.md"),
                (DevbookSyncLevel.Folder, "domain/context-map.md"),
            ],
            DevbookSync.Sources(".devbook/domain/orders#shared-types"));
    }

    [Fact]
    public void A_shared_types_unit_takes_a_direction_stated_on_its_page()
    {
        var stated = new Dictionary<string, string>(Repository) { ["domain/inbox/domain.md"] = "off" };

        Assert.Equal(
            new DevbookSyncDirection("off", DevbookSyncLevel.Page, "domain/inbox/domain.md"),
            DevbookSync.ResolveUnit(".devbook/domain/inbox#shared-types", address => stated.GetValueOrDefault(address)));
    }

    [Fact]
    public void A_building_block_and_a_component_read_themselves_then_the_overview()
    {
        Assert.Equal(
            [(DevbookSyncLevel.Unit, "arc42/building-blocks/ordering.md"), (DevbookSyncLevel.Folder, "arc42/05-building-block-view.md")],
            DevbookSync.Sources(".devbook/arc42/building-blocks/ordering.md"));
        Assert.Equal(
            [(DevbookSyncLevel.Unit, "design/component-libraries.md#button"), (DevbookSyncLevel.Folder, "design/component-libraries.md")],
            DevbookSync.Sources(".devbook/design/component-libraries.md#button"));
    }

    [Fact]
    public void A_folder_with_no_overview_lists_the_unit_alone()
    {
        Assert.Equal([(DevbookSyncLevel.Unit, "tech/technology-graph.md")], DevbookSync.Sources(".devbook/tech/technology-graph.md"));
    }

    /// <summary>units.mjs on this repository, unit by unit.</summary>
    [Theory]
    [InlineData(".devbook/domain/inbox/domain.md#inbox-item", "pull", DevbookSyncLevel.Context, "domain/inbox/context.md")]
    [InlineData(".devbook/domain/inbox/features.md#routing", "pull", DevbookSyncLevel.Context, "domain/inbox/context.md")]
    [InlineData(".devbook/domain/inbox/context.md#inbox-pane", "pull", DevbookSyncLevel.Context, "domain/inbox/context.md")]
    [InlineData(".devbook/domain/inbox#shared-types", "pull", DevbookSyncLevel.Context, "domain/inbox/context.md")]
    [InlineData(".devbook/domain/sessions/domain.md#question-relay", "push", DevbookSyncLevel.Context, "domain/sessions/context.md")]
    [InlineData(".devbook/domain/sessions/context.md#sessions-area", "push", DevbookSyncLevel.Context, "domain/sessions/context.md")]
    [InlineData(".devbook/domain/sessions#shared-types", "push", DevbookSyncLevel.Context, "domain/sessions/context.md")]
    public void A_unit_under_a_stating_context_resolves_from_it_at_context(string unit, string sync, DevbookSyncLevel level, string source)
    {
        var resolved = DevbookSync.ResolveUnit(unit, address => Repository.GetValueOrDefault(address));

        Assert.Equal(new DevbookSyncDirection(sync, level, source), resolved);
    }

    [Theory]
    [InlineData(".devbook/domain/capture#shared-types")]
    [InlineData(".devbook/domain/tasks/domain.md#task")]
    [InlineData(".devbook/arc42/building-blocks/sync.md")]
    [InlineData(".devbook/design/component-libraries.md#candidate-comparison")]
    [InlineData(".devbook/tech/technology-graph.md")]
    [InlineData("src/Program.cs")]
    public void A_unit_nothing_states_a_direction_for_is_report_from_default(string unit)
    {
        var resolved = DevbookSync.ResolveUnit(unit, address => Repository.GetValueOrDefault(address));

        Assert.Equal(new DevbookSyncDirection("report", null), resolved);
        Assert.True(resolved.IsDefault);
    }

    [Fact]
    public void A_unit_level_statement_wins_over_its_context()
    {
        var stated = new Dictionary<string, string>(Repository) { ["domain/inbox/domain.md#inbox-item"] = "off" };

        var resolved = DevbookSync.ResolveUnit(".devbook/domain/inbox/domain.md#inbox-item", address => stated.GetValueOrDefault(address));

        Assert.Equal(new DevbookSyncDirection("off", DevbookSyncLevel.Unit, "domain/inbox/domain.md#inbox-item"), resolved);
    }

    [Fact]
    public void A_page_beats_the_context_and_the_folder_answers_last()
    {
        var stated = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["domain/orders/domain.md"] = "sync",
            ["domain/orders/context.md"] = "pull",
            ["domain/context-map.md"] = "push",
        };

        Assert.Equal(
            new DevbookSyncDirection("sync", DevbookSyncLevel.Page, "domain/orders/domain.md"),
            DevbookSync.ResolveUnit("domain/orders/domain.md#order", address => stated.GetValueOrDefault(address)));
        Assert.Equal(
            new DevbookSyncDirection("push", DevbookSyncLevel.Folder, "domain/context-map.md"),
            DevbookSync.ResolveUnit("domain/billing/domain.md#invoice", address => stated.GetValueOrDefault(address)));
    }

    [Fact]
    public void A_value_outside_the_five_is_skipped_on_the_way_up()
    {
        var stated = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["domain/orders/domain.md#order"] = "both",
            ["domain/orders/context.md"] = "Pull",
        };

        Assert.Equal(
            new DevbookSyncDirection("pull", DevbookSyncLevel.Context, "domain/orders/context.md"),
            DevbookSync.ResolveUnit("domain/orders/domain.md#order", address => stated.GetValueOrDefault(address)));
    }
}
