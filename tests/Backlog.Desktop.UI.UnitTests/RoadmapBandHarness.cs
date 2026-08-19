using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Roadmap.UI;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A band over a real stored plan and a real settings file, in this test's own temp
/// folder. Nothing is stubbed but the folders: what makes these tests worth having is
/// that what was written to the storage location is what appears on screen, and a
/// fixture in place of the store would prove the mapping and nothing else.
/// </summary>
public abstract class RoadmapBandHarness : IDisposable
{
    private readonly string _root;

    protected RoadmapBandHarness()
    {
        _root = Path.Combine(Path.GetTempPath(), "roadmap-band-tests-" + Guid.NewGuid().ToString("N"));
        Settings = new WorkspaceSettingsStore(_root, Path.Combine(_root, "settings.json"));
        RepositorySettings = new GitHubSettingsStore(Path.Combine(_root, "github.json"));
        Planning = BacklogTestHost.PlanningFor(Settings);
    }

    protected WorkspaceSettingsStore Settings { get; }

    protected GitHubSettingsStore RepositorySettings { get; }

    protected IRoadmapPlanning Planning { get; }

    protected BunitContext Context()
    {
        var context = new BunitContext();

        // The timeline talks to JS to measure itself. Nothing asserted here depends on
        // what it gets back, so the calls are allowed to pass rather than being planned
        // one by one — the same arrangement RoadmapTimelineTests uses.
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(Settings);
        context.Services.AddSingleton(RepositorySettings);
        context.Services.AddSingleton(Planning);
        return context;
    }

    /// <summary>
    /// Renders the band and waits for the plan to arrive.
    /// <para>
    /// The band reads the plan from disk, so its first render is genuinely empty and
    /// the chart appears on the render after the read completes. Waiting for the
    /// timeline is the honest way to assert on it — a test that read the markup
    /// straight after Render would be asserting on the loading state.
    /// </para>
    /// </summary>
    protected static IRenderedComponent<RoadmapBand> Drawn(BunitContext context)
    {
        var band = context.Render<RoadmapBand>();
        band.WaitForElement("[data-testid=\"roadmap-timeline\"]");
        return band;
    }

    /// <summary>
    /// Clicks a control that opens a dialog, and waits for the dialog to actually be
    /// there before handing back.
    /// <para>
    /// The waiting is the point. A click sets a flag and the dialog appears on the next
    /// render, and reading the DOM on the line after the click assumes that render has
    /// already happened. It usually has on a quiet machine, which is exactly why the
    /// one test that assumed it passed locally in both configurations and failed on a
    /// loaded CI runner. Waiting for the thing you are about to interact with costs
    /// nothing when it is already there.
    /// </para>
    /// </summary>
    protected static void Open(IRenderedComponent<RoadmapBand> band, string controlTestId, string dialogTestId)
    {
        band.Find($"[data-testid=\"{controlTestId}\"]").Click();
        band.WaitForElement($"[data-testid=\"{dialogTestId}\"]");
    }

    protected void Configure(params string[] lines)
    {
        var (repositories, errors) = GitHubSettings.ParseText(string.Join('\n', lines));
        Assert.Empty(errors);
        Assert.Null(RepositorySettings.SetRepositories(repositories));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder the OS is still holding open is not a test failure.
        }

        GC.SuppressFinalize(this);
    }
}
