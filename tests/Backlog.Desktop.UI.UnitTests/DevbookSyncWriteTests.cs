using Backlog.SharedKernel.Devbook;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The <c>sync</c> field of devbook contract 25, from the file's side: the writer
/// puts a direction only where the rule allows one and takes it off anywhere, and
/// the reading resolves a unit's direction nearest-wins through its page, its
/// <c>context.md</c> and the context map — reporting the level it came from.
/// </summary>
public sealed class DevbookSyncWriteTests : IDisposable
{
    private const string DomainText = """
        # Orders

        ```meta
        type: domain
        ```

        ## Order

        ```meta
        type: aggregate
        status: draft
        ```

        Order text.

        ### Order Line

        ```meta
        type: entity
        ```

        ## Order Placed

        ```meta
        type: domain-event
        related: [.devbook/domain/orders/domain.md#order]
        ```
        """;

    /// <summary>The page with LF newlines whatever the checkout did to this file.</summary>
    private static readonly string Domain = DomainText.Replace("\r\n", "\n", StringComparison.Ordinal);

    private readonly List<string> _roots = [];

    [Fact]
    public void Writing_a_direction_on_a_unit_adds_the_field_at_the_end_of_its_fence_and_touches_nothing_else()
    {
        var (root, path) = Folder(("orders/domain.md", Domain));

        DevbookMarkdownSyncWriter.UpdateSync(root, ".devbook/domain/orders/domain.md#order", ".domain/", "Push");

        Assert.Equal(Domain.Replace("type: aggregate\nstatus: draft\n", "type: aggregate\nstatus: draft\nsync: push\n", StringComparison.Ordinal), Read(path("orders/domain.md")));
    }

    [Fact]
    public void Writing_again_replaces_the_line_rather_than_adding_a_second()
    {
        var (root, path) = Folder(("orders/domain.md", Domain));

        DevbookMarkdownSyncWriter.UpdateSync(root, ".domain/orders/domain.md#order", ".domain/", "push");
        DevbookMarkdownSyncWriter.UpdateSync(root, ".domain/orders/domain.md#order", ".domain/", "off");

        var text = Read(path("orders/domain.md"));
        Assert.Contains("status: draft\nsync: off\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sync: push", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_takes_a_direction_on_its_file_block()
    {
        var (root, path) = Folder(("orders/domain.md", Domain));

        DevbookMarkdownSyncWriter.UpdateSync(root, ".domain/orders/domain.md", ".domain/", "pull");

        Assert.StartsWith("# Orders\n\n```meta\ntype: domain\nsync: pull\n```", Read(path("orders/domain.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_heading_with_no_fence_gets_one_holding_only_the_direction()
    {
        var (root, path) = Folder(("05-building-block-view.md", "# Building Block View\n\nText.\n"));

        DevbookMarkdownSyncWriter.UpdateSync(root, ".arc42/05-building-block-view.md", ".arc42/", "pull");

        Assert.Equal("# Building Block View\n\n```meta\nsync: pull\n```\n\nText.\n", Read(path("05-building-block-view.md")));
    }

    [Theory]
    [InlineData(".domain/orders/domain.md#order-line")]
    [InlineData(".domain/orders/domain.md#order-placed")]
    public void A_chapter_a_unit_owns_is_refused_and_the_file_left_alone(string itemPath)
    {
        var (root, path) = Folder(("orders/domain.md", Domain));

        Assert.Throws<InvalidOperationException>(() => DevbookMarkdownSyncWriter.UpdateSync(root, itemPath, ".domain/", "push"));

        Assert.Equal(Domain, Read(path("orders/domain.md")));
    }

    [Fact]
    public void A_page_that_only_holds_owned_chapters_is_refused()
    {
        const string requirements = "# Requirements\n\n```meta\ntype: requirements\n```\n";
        var (root, _) = Folder(("orders/requirements.md", requirements));

        Assert.Throws<InvalidOperationException>(() => DevbookMarkdownSyncWriter.UpdateSync(root, ".domain/orders/requirements.md", ".domain/", "push"));
    }

    [Theory]
    [InlineData(".tech/", "shared.md")]
    [InlineData(".ai/", "adoption-map.md")]
    public void A_folder_that_takes_no_direction_is_refused(string prefix, string file)
    {
        var (root, _) = Folder((file, "# Title\n\n```meta\nstatus: trial\n```\n"));

        Assert.Throws<InvalidOperationException>(() => DevbookMarkdownSyncWriter.UpdateSync(root, prefix + file, prefix, "push"));
    }

    [Fact]
    public void A_value_outside_the_five_is_refused_before_the_file_is_opened()
    {
        var (root, path) = Folder(("orders/domain.md", Domain));

        Assert.Throws<ArgumentException>(() => DevbookMarkdownSyncWriter.UpdateSync(root, ".domain/orders/domain.md#order", ".domain/", "both"));

        Assert.Equal(Domain, Read(path("orders/domain.md")));
    }

    [Fact]
    public void Removing_takes_the_line_off_even_where_it_was_misplaced_and_keeps_the_fence()
    {
        var misplaced = Domain.Replace("type: entity\n", "type: entity\nsync: push\n", StringComparison.Ordinal);
        var (root, path) = Folder(("orders/domain.md", misplaced));

        DevbookMarkdownSyncWriter.RemoveSync(root, ".domain/orders/domain.md#order-line", ".domain/");

        Assert.Equal(Domain, Read(path("orders/domain.md")));
    }

    [Fact]
    public void Removing_where_nothing_states_a_direction_leaves_the_file_untouched()
    {
        var (root, path) = Folder(("orders/domain.md", Domain));
        var before = File.GetLastWriteTimeUtc(path("orders/domain.md"));

        DevbookMarkdownSyncWriter.RemoveSync(root, ".domain/orders/domain.md#order", ".domain/");

        Assert.Equal(before, File.GetLastWriteTimeUtc(path("orders/domain.md")));
    }

    [Fact]
    public void A_crlf_file_keeps_its_newlines()
    {
        var (root, path) = Folder(("orders/domain.md", Domain.Replace("\n", "\r\n", StringComparison.Ordinal)));

        DevbookMarkdownSyncWriter.UpdateSync(root, ".domain/orders/domain.md#order", ".domain/", "push");

        var text = File.ReadAllText(path("orders/domain.md"));
        Assert.Contains("status: draft\r\nsync: push\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\r", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_unit_resolves_nearest_wins_and_says_which_level_answered()
    {
        var (root, _) = Folder(
            ("context-map.md", "# Context Map\n\n```meta\nsync: report\n```\n"),
            ("orders/context.md", "# Orders\n\n```meta\ntype: context\nsync: push\n```\n"),
            ("orders/domain.md", Domain));

        var reading = DevbookSyncReading.Read(root, DevbookFolder.Domain, ".devbook/domain/orders/domain.md");

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
        var page = Domain
            .Replace("type: domain\n", "type: domain\nsync: off\n", StringComparison.Ordinal);
        var (root, _) = Folder(
            ("orders/context.md", "# Orders\n\n```meta\nsync: push\n```\n"),
            ("orders/domain.md", page));

        var fromPage = DevbookSyncReading.Read(root, DevbookFolder.Domain, "orders/domain.md").Describe(DevbookMetadataLevel.Chapter, "Order");
        Assert.Equal(new DevbookSyncDirection("off", DevbookSyncLevel.Page), fromPage!.Effective);

        var (root2, _) = Folder(
            ("orders/context.md", "# Orders\n\n```meta\nsync: push\n```\n"),
            ("orders/domain.md", page.Replace("status: draft\n", "status: draft\nsync: pull\n", StringComparison.Ordinal)));

        var fromUnit = DevbookSyncReading.Read(root2, DevbookFolder.Domain, "orders/domain.md").Describe(DevbookMetadataLevel.Chapter, "Order");
        Assert.Equal("pull", fromUnit!.Stated);
        Assert.Equal(new DevbookSyncDirection("pull", DevbookSyncLevel.Unit), fromUnit.Effective);
    }

    [Fact]
    public void Nothing_stated_anywhere_is_report_from_no_level_and_a_typo_is_skipped()
    {
        var (root, _) = Folder(
            ("orders/context.md", "# Orders\n\n```meta\nsync: pushh\n```\n"),
            ("orders/domain.md", Domain));

        var unit = DevbookSyncReading.Read(root, DevbookFolder.Domain, "orders/domain.md").Describe(DevbookMetadataLevel.Chapter, "Order");

        Assert.Equal(new DevbookSyncDirection("report", null), unit!.Effective);
        Assert.True(unit.Effective.IsDefault);
    }

    [Fact]
    public void A_building_block_inherits_from_the_building_block_view()
    {
        var (root, _) = Folder(
            ("05-building-block-view.md", "# Building Block View\n\n```meta\nsync: pull\n```\n"),
            ("building-blocks/sync.md", "# Sync\n\nText.\n"),
            ("building-blocks/README.md", "# Building Blocks\n\n```meta\nindex: root\n```\n"));

        var block = DevbookSyncReading.Read(root, DevbookFolder.Arc42, ".devbook/arc42/building-blocks/sync.md")
            .Describe(DevbookMetadataLevel.File, null);
        Assert.Equal(DevbookSyncLevel.Unit, block!.Level);
        Assert.Equal(new DevbookSyncDirection("pull", DevbookSyncLevel.Folder), block.Effective);

        // The file view reports a file's title as a chapter heading; it is still
        // the file's own block.
        Assert.Equal(block, DevbookSyncReading.Read(root, DevbookFolder.Arc42, "building-blocks/sync.md").Describe(DevbookMetadataLevel.Chapter, "Sync"));

        // The directory's entry point is no building block.
        Assert.Null(DevbookSyncReading.Read(root, DevbookFolder.Arc42, "building-blocks/README.md").Describe(DevbookMetadataLevel.File, null));
    }

    [Fact]
    public void A_scope_addresses_a_chapter_by_slug_and_the_title_by_the_bare_path()
    {
        var (root, _) = Folder(("orders/domain.md", Domain));
        var writes = new List<(string Path, string? Direction)>();

        var scope = DevbookSyncReading.Read(root, DevbookFolder.Domain, ".devbook/domain/orders/domain.md")
            .Scope(".devbook/domain/orders/domain.md", (path, direction) => { writes.Add((path, direction)); return Task.CompletedTask; });

        scope.ChangeAsync(DevbookMetadataLevel.Chapter, "Order", "push");
        scope.ChangeAsync(DevbookMetadataLevel.Chapter, "Orders", null);

        Assert.True(scope.CanChange);
        Assert.Equal(
            new List<(string, string?)> { (".devbook/domain/orders/domain.md#order", "push"), (".devbook/domain/orders/domain.md", null) },
            writes);
    }

    [Fact]
    public void A_scope_with_no_writer_cannot_change_anything()
    {
        var (root, _) = Folder(("orders/domain.md", Domain));

        var scope = DevbookSyncReading.Read(root, DevbookFolder.Domain, "orders/domain.md").Scope("orders/domain.md", null);

        Assert.False(scope.CanChange);
        Assert.NotNull(scope.Describe(DevbookMetadataLevel.Chapter, "Order"));
    }

    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static string Read(string path) => File.ReadAllText(path);

    /// <summary>A folder root holding the given files exactly as given, and a way
    /// to name a file under it.</summary>
    private (string Root, Func<string, string> Path) Folder(params (string File, string Content)[] files)
    {
        var parent = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "devbook-sync-tests", Guid.NewGuid().ToString("N"));
        var root = System.IO.Path.Combine(parent, "folder");
        _roots.Add(parent);

        foreach (var (file, content) in files)
        {
            var path = System.IO.Path.Combine(root, file.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        return (root, file => System.IO.Path.Combine(root, file.Replace('/', System.IO.Path.DirectorySeparatorChar)));
    }
}
