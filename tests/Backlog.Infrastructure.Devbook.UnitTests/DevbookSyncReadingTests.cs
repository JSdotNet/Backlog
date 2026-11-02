using Backlog.Infrastructure.Devbook.Chapters;
using Backlog.SharedKernel.Devbook;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// The <c>sync</c> reading from the adapter's side, with no screen in the room:
/// where a block sits comes from the Markdown, what it states from the direction
/// map handed in — never from a <c>sync</c> line the chapter still carries — and a
/// unit resolves nearest-wins through its page, its <c>context.md</c> and the
/// folder overview, reporting the level it came from. The item path is the one the
/// Devbook UI project's <c>Scope</c> extension hands its writer.
/// </summary>
public sealed class DevbookSyncReadingTests : IDisposable
{
    private const string Orders = """
        # Orders

        ```meta
        type: domain
        ```

        ## Order

        ```meta
        type: aggregate
        sync: off
        ```

        Order text.

        ### Order Line

        ```meta
        type: entity
        ```

        ## Order Placed

        ```meta
        type: domain-event
        ```
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-devbook-sync-reading-tests", Guid.NewGuid().ToString("n"));

    private static Func<string, string?> Map(params (string Address, string Direction)[] stated)
    {
        var map = stated.ToDictionary(entry => entry.Address, entry => entry.Direction, StringComparer.Ordinal);
        return address => map.GetValueOrDefault(address);
    }

    [Fact]
    public void A_chapter_is_addressed_by_slug_and_the_title_by_the_bare_path()
    {
        Write("orders/domain.md", Orders);

        var reading = DevbookSyncReading.Read(_root, DevbookFolder.Domain, "orders/domain.md", Map());

        Assert.Equal("orders/domain.md#order-line", reading.ItemPath("orders/domain.md", DevbookMetadataLevel.Chapter, "Order Line"));
        Assert.Equal("orders/domain.md", reading.ItemPath("orders/domain.md", DevbookMetadataLevel.Chapter, "Orders"));
        Assert.Equal("orders/domain.md", reading.ItemPath("orders/domain.md", DevbookMetadataLevel.File, null));
    }

    [Fact]
    public void A_heading_the_document_does_not_hold_is_addressed_by_the_bare_path()
    {
        Assert.Equal(
            "orders/domain.md",
            DevbookSyncReading.None.ItemPath("orders/domain.md", DevbookMetadataLevel.Chapter, "Order"));
    }

    [Fact]
    public void Every_level_is_asked_of_the_map_by_its_address()
    {
        Write("orders/domain.md", Orders);
        var asked = new List<string>();

        DevbookSyncReading.Read(_root, DevbookFolder.Domain, ".devbook/domain/orders/domain.md", address =>
        {
            asked.Add(address);
            return null;
        });

        Assert.Equal(
            [
                "domain/orders/domain.md",
                "domain/orders/domain.md#order",
                "domain/orders/domain.md#order-line",
                "domain/orders/domain.md#order-placed",
                "domain/orders/context.md",
                "domain/context-map.md",
            ],
            asked);
    }

    /// <summary>The map's address carries units.mjs's slug — an apostrophe
    /// dropped, an underscore kept — so the view and the sweeps name one unit
    /// with one id. The heading lookup still finds the block either way.</summary>
    [Fact]
    public void A_chapter_is_addressed_by_the_units_mjs_slug()
    {
        Write("orders/domain.md", "# Orders\n\n```meta\ntype: domain\n```\n\n## Customer's order\n\n```meta\ntype: aggregate\n```\n\n## user_id\n\n```meta\ntype: domain-service\n```\n");
        var asked = new List<string>();

        var reading = DevbookSyncReading.Read(_root, DevbookFolder.Domain, "orders/domain.md", address =>
        {
            asked.Add(address);
            return address == "domain/orders/domain.md#customers-order" ? "push" : null;
        });

        Assert.Contains("domain/orders/domain.md#customers-order", asked);
        Assert.Contains("domain/orders/domain.md#user_id", asked);
        Assert.Equal("orders/domain.md#customers-order", reading.ItemPath("orders/domain.md", DevbookMetadataLevel.Chapter, "Customer's order"));
        Assert.Equal("orders/domain.md#user_id", reading.ItemPath("orders/domain.md", DevbookMetadataLevel.Chapter, "user_id"));
        Assert.Equal("push", reading.Describe(DevbookMetadataLevel.Chapter, "Customer's order")?.Stated);
    }

    [Fact]
    public void A_sync_line_left_in_the_chapter_is_not_read()
    {
        Write("orders/domain.md", Orders);

        var unit = DevbookSyncReading.Read(_root, DevbookFolder.Domain, "orders/domain.md", Map())
            .Describe(DevbookMetadataLevel.Chapter, "Order");

        Assert.NotNull(unit);
        Assert.Null(unit.Stated);
        Assert.Equal(new DevbookSyncDirection("report", null), unit.Effective);
    }

    [Fact]
    public void A_unit_resolves_nearest_wins_and_says_which_level_answered()
    {
        Write("orders/domain.md", Orders);

        var reading = DevbookSyncReading.Read(
            _root,
            DevbookFolder.Domain,
            ".devbook/domain/orders/domain.md",
            Map(("domain/context-map.md", "report"), ("domain/orders/context.md", "push")));

        // Nothing on the unit or its page: the context answers.
        var unit = reading.Describe(DevbookMetadataLevel.Chapter, "Order");
        Assert.NotNull(unit);
        Assert.Equal(DevbookSyncLevel.Unit, unit.Level);
        Assert.Null(unit.Stated);
        Assert.Equal(new DevbookSyncDirection("push", DevbookSyncLevel.Context), unit.Effective);

        // The page is a level of its own, handing the same direction down.
        var page = reading.Describe(DevbookMetadataLevel.File, null);
        Assert.Equal(DevbookSyncLevel.Page, page!.Level);
        Assert.Equal("push", page.Effective.Direction);

        // An owned chapter is no level, and offers nothing.
        Assert.Null(reading.Describe(DevbookMetadataLevel.Chapter, "Order Line"));
        Assert.Null(reading.Describe(DevbookMetadataLevel.Chapter, "Order Placed"));
    }

    [Fact]
    public void The_page_beats_the_context_and_the_unit_beats_the_page()
    {
        Write("orders/domain.md", Orders);

        var fromPage = DevbookSyncReading.Read(_root, DevbookFolder.Domain, "orders/domain.md", Map(("domain/orders/domain.md", "off"), ("domain/orders/context.md", "push")))
            .Describe(DevbookMetadataLevel.Chapter, "Order");
        Assert.Equal(new DevbookSyncDirection("off", DevbookSyncLevel.Page), fromPage!.Effective);

        var fromUnit = DevbookSyncReading.Read(
                _root,
                DevbookFolder.Domain,
                "orders/domain.md",
                Map(("domain/orders/domain.md", "off"), ("domain/orders/context.md", "push"), ("domain/orders/domain.md#order", "pull")))
            .Describe(DevbookMetadataLevel.Chapter, "Order");
        Assert.Equal("pull", fromUnit!.Stated);
        Assert.Equal(new DevbookSyncDirection("pull", DevbookSyncLevel.Unit), fromUnit.Effective);
    }

    [Fact]
    public void A_typo_in_the_map_is_skipped()
    {
        Write("orders/domain.md", Orders);

        var unit = DevbookSyncReading.Read(_root, DevbookFolder.Domain, "orders/domain.md", Map(("domain/orders/context.md", "pushh")))
            .Describe(DevbookMetadataLevel.Chapter, "Order");

        Assert.Equal(new DevbookSyncDirection("report", null), unit!.Effective);
        Assert.True(unit.Effective.IsDefault);
    }

    [Fact]
    public void A_building_block_inherits_from_the_building_block_view()
    {
        Write("05-building-block-view.md", "# Building Block View\n\nText.\n");
        Write("building-blocks/sync.md", "# Sync\n\nText.\n");
        Write("building-blocks/README.md", "# Building Blocks\n\n```meta\nindex: root\n```\n");
        var map = Map(("arc42/05-building-block-view.md", "pull"));

        var block = DevbookSyncReading.Read(_root, DevbookFolder.Arc42, ".devbook/arc42/building-blocks/sync.md", map)
            .Describe(DevbookMetadataLevel.File, null);
        Assert.Equal(DevbookSyncLevel.Unit, block!.Level);
        Assert.Equal(new DevbookSyncDirection("pull", DevbookSyncLevel.Folder), block.Effective);

        // The file view reports a file's title as a chapter heading; it is still
        // the file's own block.
        Assert.Equal(block, DevbookSyncReading.Read(_root, DevbookFolder.Arc42, "building-blocks/sync.md", map).Describe(DevbookMetadataLevel.Chapter, "Sync"));

        // The directory's entry point is no building block.
        Assert.Null(DevbookSyncReading.Read(_root, DevbookFolder.Arc42, "building-blocks/README.md", map).Describe(DevbookMetadataLevel.File, null));
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}
