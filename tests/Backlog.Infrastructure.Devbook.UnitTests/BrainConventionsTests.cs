using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// The areas a Brain's <c>CONVENTIONS.md</c> declares (local ADR 0026 §2), and
/// the PARA set when it declares none — <c>requirements.md#the-areas-are-the-declared-ones</c>
/// in the devbook context, from the parsing side.
/// </summary>
public class BrainConventionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-brain-conventions", Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_table_declares_the_areas_in_its_order()
    {
        var areas = BrainConventions.Parse("""
            # Conventions

            ## Areas

            | Area | Folder |
            |------|--------|
            | Work | work |
            | Home life | `home` |
            """);

        Assert.Equal([new BrainArea("Work", "work", 1), new BrainArea("Home life", "home", 2)], areas);
    }

    [Fact]
    public void A_table_s_order_column_wins_over_the_row_order()
    {
        var areas = BrainConventions.Parse("""
            ## Areas

            | Name | Folder | Order |
            |---|---|---|
            | Later | later | 2 |
            | Unnumbered | unnumbered | |
            | First | first | 1 |
            """);

        Assert.Equal(["first", "later", "unnumbered"], areas.Select(area => area.Folder));
        Assert.Equal([1, 2, 3], areas.Select(area => area.Order));
    }

    [Fact]
    public void A_list_declares_the_areas_with_or_without_names()
    {
        var areas = BrainConventions.Parse("""
            # Conventions

            ### Areas

            - Inbox — `_inbox`
            - `projects`
            - people
            * Side work: `side-work`
            """);

        Assert.Equal(
            [
                new BrainArea("Inbox", "_inbox", 1),
                new BrainArea("Projects", "projects", 2),
                new BrainArea("People", "people", 3),
                new BrainArea("Side work", "side-work", 4)
            ],
            areas);
    }

    [Fact]
    public void Only_the_areas_section_is_read()
    {
        var areas = BrainConventions.Parse("""
            # Conventions

            - not an area

            ## Areas

            - `work`

            ```markdown
            - `fenced`
            ```

            ## Naming

            - `naming`
            """);

        Assert.Equal(["work"], areas.Select(area => area.Folder));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData(".hidden")]
    [InlineData("C:")]
    public void A_folder_that_is_not_one_plain_segment_is_skipped(string folder)
    {
        var areas = BrainConventions.Declared($"## Areas\n\n- `{folder}`\n- `kept`\n");

        Assert.Equal(["kept"], areas.Select(area => area.Folder));
    }

    [Fact]
    public void A_byte_order_mark_does_not_hide_the_first_heading()
    {
        var areas = BrainConventions.Declared("﻿## Areas\n\n- `work`\n");

        Assert.Equal(["work"], areas.Select(area => area.Folder));
    }

    [Fact]
    public void A_holds_column_is_read()
    {
        var areas = BrainConventions.Declared("## Areas\n\n| Area | Folder | Holds |\n|:---|:---:|---|\n| Clients | `clients` | One folder per client. |\n");

        Assert.Equal([new BrainArea("Clients", "clients", 1, "One folder per client.")], areas);
    }

    [Fact]
    public void A_folder_declared_twice_counts_once()
    {
        var areas = BrainConventions.Declared("## Areas\n\n- `work`\n- Work again — `Work`\n- `home`\n");

        Assert.Equal([new BrainArea("Work", "work", 1), new BrainArea("Home", "home", 2)], areas);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("# Conventions\n\nNo areas here.\n")]
    [InlineData("## Areas\n\nNone declared yet.\n")]
    public void Nothing_declared_is_the_para_set(string? markdown)
    {
        Assert.Empty(BrainConventions.Declared(markdown));
        Assert.Equal(
            ["_inbox", "projects", "areas", "resources", "people", "journal", "archive"],
            BrainConventions.Parse(markdown).Select(area => area.Folder));
    }

    [Fact]
    public void A_brain_without_conventions_reads_as_para()
    {
        Directory.CreateDirectory(_root);

        Assert.Same(BrainConventions.Para, BrainConventions.Read(_root));
        Assert.Same(BrainConventions.Para, BrainConventions.Read(null));
    }

    [Fact]
    public void A_brain_s_conventions_file_is_read_from_its_root()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, BrainConventions.FileName), "## Areas\n\n- Work — `work`\n- Home — `home`\n");

        Assert.Equal(["work", "home"], BrainConventions.Read(_root).Select(area => area.Folder));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
