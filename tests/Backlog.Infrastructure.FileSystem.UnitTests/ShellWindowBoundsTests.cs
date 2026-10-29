namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The next window of a surface opens where the last one stood, clamped to a monitor
/// that is connected now (local ADR 0042 §5): on the monitor it overlaps most, on the
/// primary one when its own is gone, inside the work area and no larger than it.
/// </summary>
public sealed class ShellWindowBoundsTests
{
    private static readonly ScreenArea Primary = new(0, 0, 1920, 1040);
    private static readonly ScreenArea Left = new(-2560, 0, 2560, 1400);

    [Fact]
    public void Bounds_inside_a_connected_monitor_are_kept_as_they_are()
    {
        var bounds = new ShellWindowBounds(100, 100, 800, 600, false);

        Assert.Equal(bounds, bounds.ClampTo([Primary, Left]));
    }

    [Fact]
    public void Bounds_on_a_second_monitor_stay_on_it()
    {
        var bounds = new ShellWindowBounds(-2000, 200, 1200, 800, false);

        Assert.Equal(bounds, bounds.ClampTo([Primary, Left]));
    }

    [Fact]
    public void Bounds_on_a_monitor_that_is_gone_move_onto_the_primary_one()
    {
        var bounds = new ShellWindowBounds(-2000, 200, 1200, 800, Maximized: true);

        var clamped = bounds.ClampTo([Primary]);

        Assert.Equal(new ShellWindowBounds(0, 200, 1200, 800, Maximized: true), clamped);
    }

    [Fact]
    public void Bounds_hanging_off_an_edge_move_inside_the_work_area()
    {
        var bounds = new ShellWindowBounds(1500, 900, 800, 600, false);

        Assert.Equal(new ShellWindowBounds(1120, 440, 800, 600, false), bounds.ClampTo([Primary]));
    }

    [Fact]
    public void Bounds_larger_than_the_monitor_shrink_to_its_work_area()
    {
        var bounds = new ShellWindowBounds(-100, -50, 3000, 2000, false);

        Assert.Equal(new ShellWindowBounds(0, 0, 1920, 1040, false), bounds.ClampTo([Primary]));
    }

    [Fact]
    public void Bounds_across_two_monitors_go_to_the_one_they_overlap_most()
    {
        // 300 px on the left monitor, 900 px on the primary one.
        var bounds = new ShellWindowBounds(-300, 100, 1200, 600, false);

        Assert.Equal(new ShellWindowBounds(0, 100, 1200, 600, false), bounds.ClampTo([Primary, Left]));
    }

    [Fact]
    public void A_window_smaller_than_the_minimum_grows_to_it()
    {
        var bounds = new ShellWindowBounds(10, 10, 50, 40, false);

        var clamped = bounds.ClampTo([Primary])!;

        Assert.Equal(ShellWindowBounds.MinimumSize, clamped.Width);
        Assert.Equal(ShellWindowBounds.MinimumSize, clamped.Height);
    }

    [Fact]
    public void No_connected_monitor_leaves_the_window_to_the_system()
    {
        Assert.Null(new ShellWindowBounds(10, 10, 800, 600, false).ClampTo([]));
    }

    [Fact]
    public void A_monitor_reporting_no_work_area_is_passed_over()
    {
        var bounds = new ShellWindowBounds(-2000, 200, 1200, 800, false);

        Assert.Equal(new ShellWindowBounds(0, 200, 1200, 800, false), bounds.ClampTo([new ScreenArea(0, 0, 0, 0), Primary]));
    }

    [Fact]
    public void Bounds_with_no_size_leave_the_window_to_the_system()
    {
        Assert.Null(new ShellWindowBounds(10, 10, 0, 600, false).ClampTo([Primary]));
    }
}
