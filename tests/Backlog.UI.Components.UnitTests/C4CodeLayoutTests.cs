namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// <see cref="C4CodeLayout"/>: the Code level laid out by <see cref="C4LayoutEngine"/>,
/// each type a box of one size for the picture, each use a routed path.
/// </summary>
public sealed class C4CodeLayoutTests
{
    private static C4CodeLevel Level => C4CodeViewTests.Level;

    [Fact]
    public void Every_type_is_placed_once_at_the_class_box_width()
    {
        var diagram = C4CodeLayout.Build(Level, allMembers: false);

        Assert.Equal(
            [C4CodeViewTests.Builder, C4CodeViewTests.Database, C4CodeViewTests.Refresher],
            diagram.Types.Select(type => type.Type.Id).Order(StringComparer.Ordinal));
        Assert.All(diagram.Types, type => Assert.Equal(C4CodeLayout.TypeWidth, type.Width));
        Assert.Single(diagram.Types.Select(type => type.Height).Distinct());
        Assert.True(diagram.Width >= C4CodeLayout.TypeWidth);
    }

    /// <summary>The engine layers on the edges: what a type uses sits below it.</summary>
    [Fact]
    public void A_used_type_sits_below_the_type_that_uses_it()
    {
        var diagram = C4CodeLayout.Build(Level, allMembers: false);
        double Top(string id) => diagram.Types.Single(type => type.Type.Id == id).Y;

        Assert.True(Top(C4CodeViewTests.Builder) > Top(C4CodeViewTests.Refresher));
        Assert.Equal(2, diagram.Uses.Count);
        Assert.All(diagram.Uses, use => Assert.StartsWith("M", use.Path, StringComparison.Ordinal));
    }

    [Fact]
    public void The_box_is_as_tall_as_the_longest_member_list_shown()
    {
        var publicOnly = C4CodeLayout.Build(Level, allMembers: false);
        var all = C4CodeLayout.Build(Level, allMembers: true);

        Assert.True(all.Types[0].Height > publicOnly.Types[0].Height);
        Assert.Equal(3, all.Types.Single(type => type.Type.Id == C4CodeViewTests.Refresher).Members.Count);
    }

    [Fact]
    public void A_long_member_list_is_cut_and_says_how_many_more()
    {
        var members = Enumerable.Range(1, 20).Select(index => $"+ M{index}() : void").ToList();
        var level = new C4CodeLevel("Big", ["src"], [new("Big", "Big", "class", "«class»", ["src/Big.cs"], members, members)], [], []);

        var placed = Assert.Single(C4CodeLayout.Build(level, allMembers: false).Types);

        Assert.Equal(C4CodeLayout.MaxMemberLines - 1, placed.Members.Count);
        Assert.Equal(20 - (C4CodeLayout.MaxMemberLines - 1), placed.Hidden);
    }

    [Fact]
    public void A_use_of_a_type_outside_the_level_or_of_itself_is_not_drawn()
    {
        var level = Level with { Uses = [new(C4CodeViewTests.Refresher, "Elsewhere.Type", false), new(C4CodeViewTests.Builder, C4CodeViewTests.Builder, false)] };

        Assert.Empty(C4CodeLayout.Build(level, allMembers: false).Uses);
    }

    /// <summary>The card size is an option only the Code level sets: a C4 view laid out
    /// without it keeps the C4 card's own size.</summary>
    [Fact]
    public void Without_a_card_size_the_c4_card_keeps_its_own()
    {
        var workspace = C4ZoomNavigationTests.Workspace;
        var view = workspace.View("containers-backlog")!;

        var plain = C4LayoutEngine.Build(workspace, view);
        var sized = C4LayoutEngine.Build(workspace, view, new C4LayoutOptions { CardWidth = 196, CardHeight = 84 });

        Assert.All(plain.Nodes, node => Assert.Equal((196d, 84d), (node.Width, node.Height)));
        Assert.Equal(plain.Nodes.Select(node => (node.Alias, node.X, node.Y)), sized.Nodes.Select(node => (node.Alias, node.X, node.Y)));
        Assert.Equal(plain.Edges.Select(edge => edge.Path), sized.Edges.Select(edge => edge.Path));
    }

    [Fact]
    public void No_types_is_an_empty_picture()
    {
        Assert.Same(C4CodeDiagram.Empty, C4CodeLayout.Build(new C4CodeLevel("None", [], [], [], []), allMembers: true));
    }
}
