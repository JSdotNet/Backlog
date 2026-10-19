using Backlog.UI.Components.Goals;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The "Goal" line on a piece of work: the goal's title when it counts toward one,
/// "Attach to goal" while it counts toward none, and one press that asks the host for
/// the picker.
/// </summary>
public sealed class GoalControlTests : IDisposable
{
    private readonly BunitContext _context = new();

    public GoalControlTests() => _context.JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void Without_a_goal_it_offers_to_attach_one()
    {
        var control = _context.Render<GoalControl>();

        Assert.Equal("Goal", control.Find(".goal-control__label").TextContent.Trim());
        var button = control.Find("[data-testid=goal-control-open]");
        Assert.Equal("Attach to goal", button.TextContent.Trim());
        Assert.Equal("Attach to goal", button.GetAttribute("aria-label"));
    }

    [Fact]
    public void With_a_goal_it_reads_the_goals_title()
    {
        var control = _context.Render<GoalControl>(parameters => parameters.Add(p => p.GoalTitle, "Launch the desktop app"));

        var button = control.Find("[data-testid=goal-control-open]");
        Assert.Equal("Launch the desktop app", button.TextContent.Trim());
        Assert.Equal("Goal: Launch the desktop app", button.GetAttribute("aria-label"));
    }

    [Fact]
    public async Task A_press_asks_the_host_to_open_the_picker()
    {
        var opened = 0;
        var control = _context.Render<GoalControl>(parameters => parameters.Add(p => p.OnOpen, () => opened++));

        await control.Find("[data-testid=goal-control-open]").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, opened);
    }

    public void Dispose() => _context.Dispose();
}
