namespace Backlog.ArchitectureTests;

/// <summary>
/// A row of the atlas index grows to hold a label or a status line that wraps.
///
/// <para>The index is a scrolling flex column, and each row sets an explicit
/// <c>min-height</c> for its 44px target. That explicit minimum replaces the flex
/// item's content-based automatic one, so once the list overflowed, the default
/// <c>flex-shrink: 1</c> squeezed every taller row back to 44px and a wrapped row
/// spilled over the one under it. Asserted against the stylesheet for the reason
/// <c>IndeterminateCheckboxTests</c> gives: the markup was right, and bUnit brings
/// no layout engine to notice the rows overlapping.</para>
/// </summary>
public class GraphAtlasIndexRowTests
{
    [Fact]
    public void An_index_row_never_shrinks_below_its_content()
    {
        var row = CssRules.Effective(Stylesheet(), ".graph-atlas-index__option");

        Assert.Equal("0", row.GetValueOrDefault("flex-shrink"));
    }

    private static string Stylesheet() =>
        File.ReadAllText(Path.Combine(
            Repository.Root.FullName, "src", "Core", "Backlog.UI.Components", "wwwroot", "components.css"));
}
