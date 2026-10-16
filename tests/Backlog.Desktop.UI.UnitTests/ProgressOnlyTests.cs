using Backlog.Desktop.UI.Shell;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.UI;
using Backlog.SharedKernel;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The progress switch (<c>.devbook/domain/progress/context.md#progress-area</c>): listed
/// in the catalog off and marked DEV, and with it off nothing of Progress is drawn.
/// </summary>
public sealed class ProgressOnlyTests : IDisposable
{
    private readonly BunitContext _context = new();

    [Fact]
    public void The_catalog_lists_progress_as_a_domain_feature_that_ships_off_and_marked_dev()
    {
        var progress = Assert.Single(AppFeatures.All, feature => feature.Key == ProgressFeatures.Progress);

        Assert.Equal("progress", progress.Key);
        Assert.False(progress.EnabledByDefault);
        Assert.Equal(AppFeatureStatus.Dev, progress.Status);
        Assert.Equal(AppFeatureGroup.Domain, progress.Group);
    }

    [Fact]
    public void With_progress_off_nothing_is_drawn()
    {
        _context.Services.AddSingleton<IAppFeatureSettings>(new ProgressSwitch(on: false));

        var rendered = _context.Render<ProgressOnly>(parameters => parameters
            .AddChildContent("<section class=\"sprint\">Sprint 4</section>"));

        Assert.Empty(rendered.Markup.Trim());
    }

    [Fact]
    public void With_no_feature_settings_composed_nothing_is_drawn()
    {
        var rendered = _context.Render<ProgressOnly>(parameters => parameters
            .AddChildContent("<section class=\"sprint\">Sprint 4</section>"));

        Assert.Empty(rendered.Markup.Trim());
    }

    [Fact]
    public void Turning_progress_on_and_off_draws_and_withdraws_the_content_without_a_restart()
    {
        var features = new ProgressSwitch(on: false);
        _context.Services.AddSingleton<IAppFeatureSettings>(features);
        var rendered = _context.Render<ProgressOnly>(parameters => parameters
            .AddChildContent("<section class=\"sprint\">Sprint 4</section>"));

        features.SetEnabled(ProgressFeatures.Progress, true);
        rendered.WaitForAssertion(() => Assert.Single(rendered.FindAll(".sprint")));

        features.SetEnabled(ProgressFeatures.Progress, false);
        rendered.WaitForAssertion(() => Assert.Empty(rendered.FindAll(".sprint")));
    }

    public void Dispose() => _context.Dispose();

    private sealed class ProgressSwitch(bool on) : IAppFeatureSettings
    {
        private bool _on = on;

        public event Action? Changed;

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "features.json";

        public bool IsEnabled(string key) => key == ProgressFeatures.Progress && _on;

        public string? SetEnabled(string key, bool enabled)
        {
            _on = enabled;
            Changed?.Invoke();
            return null;
        }
    }
}
