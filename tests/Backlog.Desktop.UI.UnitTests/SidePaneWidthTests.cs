using Backlog.Desktop.UI.Services;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class SidePaneWidthTests
{
    [Fact]
    public void Dragging_past_the_edges_stops_at_the_bounds()
    {
        Assert.Equal(SidePaneWidth.MinRem, SidePaneWidth.Clamp(4));
        Assert.Equal(SidePaneWidth.MaxRem, SidePaneWidth.Clamp(400));
        Assert.Equal(30, SidePaneWidth.Clamp(30));
    }

    [Fact]
    public void Arrow_keys_move_the_separator_the_way_it_points()
    {
        Assert.Equal(38, SidePaneWidth.Adjust(36, "ArrowLeft"));
        Assert.Equal(34, SidePaneWidth.Adjust(36, "ArrowRight"));
    }

    [Fact]
    public void Home_and_end_jump_to_the_reported_aria_bounds()
    {
        Assert.Equal(SidePaneWidth.MinRem, SidePaneWidth.Adjust(36, "Home"));
        Assert.Equal(SidePaneWidth.MaxRem, SidePaneWidth.Adjust(36, "End"));
    }

    [Fact]
    public void Paging_moves_further_but_still_clamps()
    {
        Assert.Equal(42, SidePaneWidth.Adjust(36, "PageUp"));
        Assert.Equal(SidePaneWidth.MaxRem, SidePaneWidth.Adjust(52, "PageUp"));
        Assert.Equal(SidePaneWidth.MinRem, SidePaneWidth.Adjust(26, "PageDown"));
    }

    [Fact]
    public void Unrelated_keys_leave_the_pane_alone()
    {
        Assert.Equal(36, SidePaneWidth.Adjust(36, "Enter"));
        Assert.Equal(36, SidePaneWidth.Adjust(36, null));
    }
}
