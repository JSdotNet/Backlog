using Backlog.Infrastructure.Devbook.Chapters;
using Backlog.SharedKernel.Devbook;
using Backlog.Tests;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// The one-time import's reading of the Markdown: every <c>sync</c> a folder
/// still states where the rule allows one, as direction map entries — and nothing
/// a misplaced or misspelt value said, since neither was ever in force.
/// </summary>
public sealed class DevbookSyncMarkdownImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-devbook-sync-import-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public void A_context_and_a_unit_stating_a_direction_are_imported_by_address()
    {
        Write("orders/context.md", "# Orders\n\n```meta\ntype: context\nsync: pull\n```\n");
        Write("orders/domain.md", "# Orders\n\n```meta\ntype: domain\n```\n\n## Order\n\n```meta\ntype: aggregate\nsync: Push\n```\n");
        Write("context-map.md", "# Context Map\n\n```meta\nsync: report\n```\n");

        var imported = DevbookSyncMarkdownImport.Read(_root, DevbookFolder.Domain);

        Assert.Equal(
            [
                ("domain/context-map.md", "report"),
                ("domain/orders/context.md", "pull"),
                ("domain/orders/domain.md#order", "push"),
            ],
            imported.OrderBy(entry => entry.Address, StringComparer.Ordinal));
    }

    [Fact]
    public void A_chapter_is_imported_under_the_units_mjs_slug()
    {
        Write("orders/domain.md", "# Orders\n\n```meta\ntype: domain\n```\n\n## Customer's order\n\n```meta\ntype: aggregate\nsync: push\n```\n\n## user_id\n\n```meta\ntype: domain-service\nsync: pull\n```\n");

        Assert.Equal(
            [("domain/orders/domain.md#customers-order", "push"), ("domain/orders/domain.md#user_id", "pull")],
            DevbookSyncMarkdownImport.Read(_root, DevbookFolder.Domain));
    }

    [Fact]
    public void A_chapter_a_unit_owns_is_refused_and_not_imported()
    {
        Write("orders/domain.md", "# Orders\n\n```meta\ntype: domain\n```\n\n## Order\n\n```meta\ntype: aggregate\n```\n\n### Order Line\n\n```meta\ntype: entity\nsync: push\n```\n");
        Write("orders/requirements.md", "# Requirements\n\n```meta\ntype: requirements\nsync: pull\n```\n");

        Assert.Empty(DevbookSyncMarkdownImport.Read(_root, DevbookFolder.Domain));
    }

    [Fact]
    public void A_value_outside_the_five_is_skipped()
    {
        Write("orders/context.md", "# Orders\n\n```meta\ntype: context\nsync: both\n```\n");

        Assert.Empty(DevbookSyncMarkdownImport.Read(_root, DevbookFolder.Domain));
    }

    [Fact]
    public void A_fence_in_a_code_block_is_not_a_block()
    {
        Write("orders/context.md", "# Orders\n\n````markdown\n## Sample\n\n```meta\ntype: aggregate\nsync: push\n```\n````\n");

        Assert.Empty(DevbookSyncMarkdownImport.Read(_root, DevbookFolder.Domain));
    }

    [Fact]
    public void A_building_block_and_a_component_are_imported_and_an_index_root_is_not()
    {
        var arc42 = Path.Combine(_root, "arc42");
        Write("arc42/building-blocks/sync.md", "# Sync\n\n```meta\nsync: pull\n```\n");
        Write("arc42/building-blocks/README.md", "# Building Blocks\n\n```meta\nindex: root\nsync: push\n```\n");
        Write("design/component-libraries.md", "# Component Libraries\n\n## Button\n\n```meta\nsync: sync\n```\n");

        Assert.Equal([("arc42/building-blocks/sync.md", "pull")], DevbookSyncMarkdownImport.Read(arc42, DevbookFolder.Arc42));
        Assert.Equal(
            [("design/component-libraries.md#button", "sync")],
            DevbookSyncMarkdownImport.Read(Path.Combine(_root, "design"), DevbookFolder.Design));
    }

    [Theory]
    [InlineData(DevbookFolder.Tech)]
    [InlineData(DevbookFolder.Ai)]
    public void A_folder_that_takes_no_direction_imports_nothing(DevbookFolder folder)
    {
        Write("technology-graph.md", "# Graph\n\n```meta\nsync: push\n```\n");

        Assert.Empty(DevbookSyncMarkdownImport.Read(_root, folder));
    }

    [Fact]
    public void A_folder_that_is_not_there_imports_nothing()
    {
        Assert.Empty(DevbookSyncMarkdownImport.Read(Path.Combine(_root, "missing"), DevbookFolder.Domain));
    }

    /// <summary>
    /// This repository's own <c>domain/</c> folder states no direction any more:
    /// devbook contract 38's <c>038-workflow-state-leaves-the-chapter</c> deleted
    /// every <c>sync</c> field once the one-time import had moved the directions
    /// into the app's store, so the import finds nothing to bring over.
    /// </summary>
    [Fact]
    public void This_repositorys_domain_has_no_direction_left_to_import()
    {
        var domain = Path.Combine(RepositoryRoot.Root.FullName, ".devbook", "domain");

        Assert.Empty(DevbookSyncMarkdownImport.Read(domain, DevbookFolder.Domain));
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
        File.WriteAllText(path, content);
    }
}
