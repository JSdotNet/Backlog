using Backlog.Desktop.UI.Services;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class SidePaneWidthTests
{
    [Fact]
    public void Dragging_past_the_edges_stops_at_the_bounds()
    {
        Assert.Equal(SidePaneWidth.MinRem, SidePaneWidth.Clamp(4));
        Assert.Equal(SidePaneWidth.MaxRem, SidePaneWidth.Clamp(4000));
        Assert.Equal(30, SidePaneWidth.Clamp(30));
    }

    [Fact]
    public void The_pane_may_be_wider_than_the_backlog_when_the_window_allows_it()
    {
        Assert.Equal(90, SidePaneWidth.Clamp(90, 120));
        Assert.Equal(70, SidePaneWidth.Clamp(90, 70));
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
        Assert.Equal(80, SidePaneWidth.Adjust(36, "End", 80));
    }

    [Fact]
    public void Paging_moves_further_but_still_clamps()
    {
        Assert.Equal(42, SidePaneWidth.Adjust(36, "PageUp"));
        Assert.Equal(70, SidePaneWidth.Adjust(68, "PageUp", 70));
        Assert.Equal(SidePaneWidth.MinRem, SidePaneWidth.Adjust(26, "PageDown"));
    }

    [Fact]
    public void Unrelated_keys_leave_the_pane_alone()
    {
        Assert.Equal(36, SidePaneWidth.Adjust(36, "Enter"));
        Assert.Equal(36, SidePaneWidth.Adjust(36, null));
    }
}
