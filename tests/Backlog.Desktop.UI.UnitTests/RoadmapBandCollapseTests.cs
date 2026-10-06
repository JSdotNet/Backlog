using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Roadmap.UI;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A repository band folded to one lane stays folded: the choice is how the reader last
/// had the roadmap drawn, so it is kept on this device beside the Hours switch (local
/// ADR 0019, §4) and a band opened again — after another surface, or a restart — draws
/// it folded until the reader expands it.
/// </summary>
public sealed class RoadmapBandCollapseTests : RoadmapBandHarness
{
    private const string Toggle = "[data-testid='roadmap-timeline-group-backlog-collapse']";

    [Fact]
    public async Task A_folded_band_is_remembered_on_the_device_until_it_is_expanded()
    {
        using (var context = Context())
        {
            var band = await PlannedAsync(context);
            Assert.Equal("true", band.Find(Toggle).GetAttribute("aria-expanded"));

            band.Find(Toggle).Click();

            band.WaitForAssertion(() => Assert.Equal("false", band.Find(Toggle).GetAttribute("aria-expanded")));
        }

        Assert.Single(new ShellNavigationStore(ShellNavigationFile).RoadmapCollapsedGroups);

        using (var reopened = Context())
        {
            var band = Folded(Drawn(reopened));

            band.Find(Toggle).Click();

            band.WaitForAssertion(() => Assert.Equal("true", band.Find(Toggle).GetAttribute("aria-expanded")));
        }

        Assert.Empty(new ShellNavigationStore(ShellNavigationFile).RoadmapCollapsedGroups);
    }

    private static IRenderedComponent<RoadmapBand> Folded(IRenderedComponent<RoadmapBand> band)
    {
        band.WaitForAssertion(() => Assert.Equal("false", band.Find(Toggle).GetAttribute("aria-expanded")));
        return band;
    }
}
