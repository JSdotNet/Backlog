using System.Diagnostics.CodeAnalysis;

using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.FileSystem;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Which remembered windows reopen at start (local ADR 0043 §5): every one whose
/// surface the catalog still offers, in the order they were remembered, and none whose
/// surface it no longer offers — those are dropped, so they are not tried again.
/// </summary>
public sealed class ShellWindowReopenPlanTests
{
    private sealed class Component;

    [Fact]
    public void Nothing_remembered_opens_nothing_and_drops_nothing()
    {
        var plan = ShellWindowReopenPlan.For([], new Catalog("devbook"));

        Assert.Empty(plan.Open);
        Assert.Empty(plan.Drop);
    }

    [Fact]
    public void Every_remembered_window_on_offer_reopens_in_the_order_remembered_with_its_query_and_bounds()
    {
        var bounds = new ShellWindowBounds(10, 20, 800, 600, Maximized: true);
        RememberedShellWindow[] remembered =
        [
            new("second-pinned-first", "roadmap", new Dictionary<string, string>(), null),
            new("then-this", "devbook", new Dictionary<string, string> { ["chapter"] = "a.md" }, bounds),
        ];

        var plan = ShellWindowReopenPlan.For(remembered, new Catalog("devbook", "roadmap"));

        Assert.Equal(["second-pinned-first", "then-this"], plan.Open.Select(window => window.Key));
        Assert.Equal("a.md", plan.Open[1].Query["chapter"]);
        Assert.Equal(bounds, plan.Open[1].Bounds);
        Assert.Empty(plan.Drop);
    }

    [Fact]
    public void A_window_whose_surface_is_no_longer_offered_is_dropped_and_the_rest_still_reopen()
    {
        RememberedShellWindow[] remembered =
        [
            new("flag-off", "sessions", new Dictionary<string, string>(), null),
            new("kept", "devbook", new Dictionary<string, string>(), null),
            new("module-gone", "proposals", new Dictionary<string, string>(), null),
        ];

        var plan = ShellWindowReopenPlan.For(remembered, new Catalog("devbook"));

        Assert.Equal(["kept"], plan.Open.Select(window => window.Key));
        Assert.Equal(["flag-off", "module-gone"], plan.Drop);
    }

    private sealed class Catalog(params string[] offered) : ISurfaceCatalog
    {
        public IReadOnlyList<ShellSurface> Surfaces =>
            [.. offered.Select((id, index) => new ShellSurface(id, id, ShellSurfaceGroup.Takeover, index, typeof(Component)))];

        public bool TryGet(string surfaceId, [NotNullWhen(true)] out ShellSurface? surface)
        {
            surface = Surfaces.FirstOrDefault(candidate => string.Equals(candidate.Id, surfaceId, StringComparison.Ordinal));
            return surface is not null;
        }
    }
}
