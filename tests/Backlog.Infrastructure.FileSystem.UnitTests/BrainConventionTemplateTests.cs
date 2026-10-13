using Backlog.Infrastructure.FileSystem;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The convention the app ships for a new Brain, and how the areas a
/// <c>CONVENTIONS.md</c> declares are read back: the PARA set by default, a
/// person's own table when there is one, and a half-typed row skipped rather than
/// costing the rest.
/// </summary>
public sealed class BrainConventionTemplateTests
{
    [Fact]
    public void The_shipped_readme_marks_the_root()
    {
        Assert.StartsWith("# Brain", BrainConventionTemplate.Readme, StringComparison.Ordinal);
        Assert.Contains("```meta\nindex: root\n```", BrainConventionTemplate.Readme, StringComparison.Ordinal);
    }

    /// <summary>The shipped convention declares exactly the PARA set the ADR
    /// names, in its order, so reading it back is the same as having none.</summary>
    [Fact]
    public void The_shipped_conventions_declare_the_para_set_in_order()
    {
        var areas = BrainConventionTemplate.ReadDeclaredAreas(BrainConventionTemplate.Conventions);

        Assert.Equal(
            ["_inbox", "projects", "areas", "resources", "people", "journal", "archive"],
            areas.Select(area => area.Folder));
        Assert.Equal(BrainConventionTemplate.ParaAreas, areas);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("# Conventions\n\nNo areas here.\n")]
    [InlineData("# Conventions\n\n## Areas\n\nNone yet.\n")]
    public void A_conventions_file_that_declares_none_falls_back_to_para(string? markdown)
    {
        Assert.Same(BrainConventionTemplate.ParaAreas, BrainConventionTemplate.ReadDeclaredAreas(markdown));
    }

    [Fact]
    public void A_persons_own_table_decides_the_areas_and_their_order()
    {
        const string markdown = """
            # Conventions

            ## Areas

            | Area | Folder | Holds |
            |:---|:---:|---|
            | Clients | `clients` | One folder per client. |
            | Inbox | _inbox | Unfiled. |

            ## Notes

            | Field | Holds |
            |---|---|
            | para | not an area |
            """;

        var areas = BrainConventionTemplate.ReadDeclaredAreas(markdown);

        Assert.Equal(
            [new BrainArea("Clients", "clients", "One folder per client."), new BrainArea("Inbox", "_inbox", "Unfiled.")],
            areas);
    }

    [Fact]
    public void Rows_whose_folder_is_not_one_plain_folder_are_skipped()
    {
        const string markdown = """
            ## Areas

            | Area | Folder |
            |---|---|
            | Up | `..` |
            | Nested | `a/b` |
            | Rooted | `C:\notes` |
            | Empty | |
            | Kept | `kept` |
            | Again | `KEPT` |
            """;

        var areas = BrainConventionTemplate.ReadDeclaredAreas(markdown);

        Assert.Equal(["kept"], areas.Select(area => area.Folder));
    }

    [Fact]
    public void A_table_inside_a_code_fence_is_not_read()
    {
        const string markdown = """
            ## Areas

            ```
            | Area | Folder |
            |---|---|
            | Hidden | `hidden` |
            ```
            """;

        Assert.Same(BrainConventionTemplate.ParaAreas, BrainConventionTemplate.ReadDeclaredAreas(markdown));
    }
}
