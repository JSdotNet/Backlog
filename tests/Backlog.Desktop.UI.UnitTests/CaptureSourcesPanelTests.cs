using System.Globalization;

using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.DataTransferObjects;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Capture.UI;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The sources panel on the Inbox pane: which sources are watched, what they
/// are pointed at, when each was last looked at, and the log behind it.
/// <para>
/// The switch and the targets are asserted against the store rather than
/// against what the rows show, because the store is what a run reads: a toggle
/// that flipped and wrote nothing would leave the next run looking at exactly
/// what it looked at before. Committing on change with no save button is the
/// house rule the Settings screen follows, and this panel came from there.
/// The last-run lines are asserted against a log a test wrote, because the
/// panel only ever reads it.
/// </para>
/// </summary>
public sealed class CaptureSourcesPanelTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_panel_opens_folded_with_a_summary_of_what_is_on()
    {
        using var panel = RenderPanel();

        var trigger = panel.Component.Find("[data-testid='capture-sources-toggle']");
        Assert.Equal("false", trigger.GetAttribute("aria-expanded"));
        Assert.Equal("none on", panel.Component.Find("[data-testid='capture-sources-summary']").TextContent.Trim());
        Assert.True(panel.Component.Find("[data-testid='capture-sources']").ParentElement!.HasAttribute("hidden"));
    }

    [Fact]
    public void The_summary_counts_the_sources_that_are_on()
    {
        using var panel = RenderPanel(before: store =>
        {
            store.SetEnabled(CaptureSourceKind.YouTube, true);
            store.SetEnabled(CaptureSourceKind.Email, true);
        });

        Assert.Equal("2 of 3 on", panel.Component.Find("[data-testid='capture-sources-summary']").TextContent.Trim());
    }

    /// <summary>The fold is the host's: the panel asks, and shows what it is
    /// told. A host that does not answer leaves it where it was.</summary>
    [Fact]
    public void The_trigger_asks_the_host_to_open_it()
    {
        var asked = new List<bool>();
        using var panel = RenderPanel(expanded: false, expandedChanged: asked.Add);

        panel.Component.Find("[data-testid='capture-sources-toggle']").Click();

        Assert.Equal([true], asked);
    }

    [Fact]
    public void Every_monitorable_source_gets_a_toggle_a_targets_field_and_a_last_run()
    {
        using var panel = RenderPanel(expanded: true);

        foreach (var kind in CaptureSourceKinds.Monitorable)
        {
            var slug = CaptureSourceKinds.Slug(kind);

            var block = panel.Component.Find($"[data-testid='capture-source-{slug}']");
            Assert.Contains(CaptureSourceKinds.Label(kind), block.TextContent, StringComparison.Ordinal);
            Assert.NotEmpty(panel.Component.FindAll($"[data-testid='capture-source-{slug}-enabled']"));
            Assert.NotEmpty(panel.Component.FindAll($"[data-testid='capture-source-{slug}-targets']"));
            Assert.Equal("Never captured.", panel.Component.Find($"[data-testid='capture-source-{slug}-last-run']").TextContent.Trim());
            Assert.NotEmpty(panel.Component.FindAll($"[data-testid='capture-source-{slug}-log-toggle']"));
        }
    }

    [Fact]
    public void Toggling_a_source_is_stored_straight_away()
    {
        using var panel = RenderPanel(expanded: true);

        panel.Component.Find("[data-testid='capture-source-youtube-enabled'] input").Change(true);

        Assert.True(panel.CaptureSources.Current.For(CaptureSourceKind.YouTube).Enabled);
        Assert.False(panel.CaptureSources.Current.For(CaptureSourceKind.Website).Enabled);
        Assert.Empty(panel.Component.FindAll("[data-testid='capture-source-youtube'] .field__error"));
        Assert.Contains("On, but pointed at nothing yet", panel.Component.Find("[data-testid='capture-source-youtube'] .field__help").TextContent, StringComparison.Ordinal);
    }

    /// <summary>One target per line, the way the Repositories tab takes its
    /// list. Blank lines are not targets.</summary>
    [Fact]
    public void Committed_targets_are_stored_one_per_line()
    {
        using var panel = RenderPanel(expanded: true);

        var field = panel.Component.Find("[data-testid='capture-source-website-targets'] textarea");
        field.Input("https://example.com/blog\n\n  https://example.org/changelog  \n");
        field.Change("https://example.com/blog\n\n  https://example.org/changelog  \n");

        Assert.Equal(
            ["https://example.com/blog", "https://example.org/changelog"],
            panel.CaptureSources.Current.For(CaptureSourceKind.Website).Targets);
    }

    [Fact]
    public void The_field_opens_showing_what_is_in_force()
    {
        using var panel = RenderPanel(
            expanded: true,
            before: store => store.SetTargets(CaptureSourceKind.Email, ["news@example.com", "digest@example.org"]));

        var field = panel.Component.Find("[data-testid='capture-source-email-targets'] textarea");
        Assert.Equal("news@example.com\ndigest@example.org", field.GetAttribute("value"));
    }

    /// <summary>The store's refusal is the field's own error line, the shape
    /// the library gives every field.</summary>
    [Fact]
    public void A_store_error_is_shown_on_the_field()
    {
        using var panel = RenderPanel(expanded: true, captureSources: new RefusingCaptureSources());

        panel.Component.Find("[data-testid='capture-source-email-enabled'] input").Change(true);

        var error = panel.Component.Find("[data-testid='capture-source-email'] .field__error");
        Assert.Contains("couldn't be saved", error.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sources_last_run_is_its_date_and_count()
    {
        using var panel = RenderPanel(expanded: true, log: log => log.Record(new CaptureRunResultDto(
            [
                new CaptureRunSourceResult(CaptureSourceKind.YouTube, 2, "YouTube: 2 new items."),
                new CaptureRunSourceResult(CaptureSourceKind.Website, 1, "Website: 1 new item.")
            ],
            Noon)));

        var youtube = panel.Component.Find("[data-testid='capture-source-youtube-last-run']");
        Assert.StartsWith("Last capture", youtube.TextContent.Trim(), StringComparison.Ordinal);
        Assert.Contains("2 new items", youtube.TextContent, StringComparison.Ordinal);
        // Invariant on both sides: the panel writes the date the way the Inbox
        // rows do, whatever the machine's culture.
        Assert.Equal(Noon.ToString("o", CultureInfo.InvariantCulture), youtube.QuerySelector("time")!.GetAttribute("datetime"));
        Assert.Contains(Noon.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture), youtube.TextContent, StringComparison.Ordinal);

        Assert.Contains("1 new item", panel.Component.Find("[data-testid='capture-source-website-last-run']").TextContent, StringComparison.Ordinal);
        Assert.Equal("Never captured.", panel.Component.Find("[data-testid='capture-source-email-last-run']").TextContent.Trim());
    }

    /// <summary>The panel redraws when the run writes the log, so a run pressed
    /// on the pane lands on the row without the reader doing anything.</summary>
    [Fact]
    public void A_run_written_after_the_panel_opened_lands_on_the_row()
    {
        using var panel = RenderPanel(expanded: true);

        panel.Log.Record(new CaptureRunResultDto([new CaptureRunSourceResult(CaptureSourceKind.Email, 0, "Email: 0 new items.")], Noon));

        panel.Component.WaitForAssertion(() =>
            Assert.Contains("0 new items", panel.Component.Find("[data-testid='capture-source-email-last-run']").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void The_log_button_opens_that_sources_runs_newest_first()
    {
        using var panel = RenderPanel(expanded: true, log: log =>
        {
            log.Record(new CaptureRunResultDto([new CaptureRunSourceResult(CaptureSourceKind.YouTube, 1, "YouTube: 1 new item.")], Noon));
            log.Record(new CaptureRunResultDto(
                [
                    new CaptureRunSourceResult(CaptureSourceKind.YouTube, 0, "YouTube: 0 new items · https://x: the server answered 404."),
                    new CaptureRunSourceResult(CaptureSourceKind.Website, 3, "Website: 3 new items.")
                ],
                Noon.AddHours(1)));
        });

        Assert.Empty(panel.Component.FindAll("[data-testid='capture-source-youtube-log']"));
        var toggle = panel.Component.Find("[data-testid='capture-source-youtube-log-toggle']");
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.Equal("Log", toggle.TextContent.Trim());

        toggle.Click();

        var log = panel.Component.Find("[data-testid='capture-source-youtube-log']");
        var entries = log.QuerySelectorAll(".capture-sources__log-entry");
        Assert.Equal(2, entries.Length);
        Assert.Contains("https://x: the server answered 404", entries[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("0 new items", entries[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("YouTube: 1 new item.", entries[1].TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Website", log.TextContent, StringComparison.Ordinal);

        toggle = panel.Component.Find("[data-testid='capture-source-youtube-log-toggle']");
        Assert.Equal("true", toggle.GetAttribute("aria-expanded"));
        Assert.Equal(log.Id, toggle.GetAttribute("aria-controls"));
        Assert.Equal("Hide log", toggle.TextContent.Trim());

        // The other source's log is its own.
        Assert.Empty(panel.Component.FindAll("[data-testid='capture-source-website-log']"));

        toggle.Click();
        Assert.Empty(panel.Component.FindAll("[data-testid='capture-source-youtube-log']"));
    }

    [Fact]
    public void An_empty_log_says_so_rather_than_showing_nothing()
    {
        using var panel = RenderPanel(expanded: true);

        panel.Component.Find("[data-testid='capture-source-email-log-toggle']").Click();

        Assert.Contains(
            "No runs have looked at Email yet.",
            panel.Component.Find("[data-testid='capture-source-email-log']").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_panel_says_where_the_choices_are_kept()
    {
        using var panel = RenderPanel(expanded: true);

        Assert.Contains(
            panel.CaptureSources.SettingsPath,
            panel.Component.Find("[data-testid='capture-sources']").TextContent,
            StringComparison.Ordinal);
    }

    private static PanelRenderContext RenderPanel(
        bool expanded = false,
        Action<bool>? expandedChanged = null,
        ICaptureSourceSettings? captureSources = null,
        Action<ICaptureSourceSettings>? before = null,
        Action<ICaptureRunLog>? log = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-capture-sources-panel-tests", Guid.NewGuid().ToString("n"));

        captureSources ??= new CaptureSourcesSettingsStore(Path.Combine(root, "capture-sources.json"));
        // Before the render: the panel seeds its drafts once, when it initializes.
        before?.Invoke(captureSources);
        var runLog = new CaptureRunLogStore(Path.Combine(root, "capture-runs.json"));
        log?.Invoke(runLog);

        var context = new BunitContext();
        context.Services.AddSingleton(captureSources);
        context.Services.AddSingleton<ICaptureRunLog>(runLog);

        var component = context.Render<CaptureSourcesPanel>(parameters => parameters
            .Add(p => p.Expanded, expanded)
            .Add(p => p.ExpandedChanged, open => expandedChanged?.Invoke(open)));

        return new PanelRenderContext(root, context, component, captureSources, runLog);
    }

    /// <summary>A store whose disk has gone away: every write is refused with
    /// the message the real one gives, and nothing changes.</summary>
    private sealed class RefusingCaptureSources : ICaptureSourceSettings
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public CaptureSourceSettings Current { get; } = new();

        public string SettingsPath => Path.Combine("nowhere", "capture-sources.json");

        public string? SetEnabled(CaptureSourceKind kind, bool enabled) =>
            "Changed, but the capture sources couldn't be saved for next time.";

        public string? SetTargets(CaptureSourceKind kind, IReadOnlyList<string> targets) =>
            "Changed, but the capture sources couldn't be saved for next time.";
    }

    private sealed record PanelRenderContext(
        string Root,
        BunitContext TestContext,
        IRenderedComponent<CaptureSourcesPanel> Component,
        ICaptureSourceSettings CaptureSources,
        ICaptureRunLog Log) : IDisposable
    {
        public void Dispose()
        {
            TestContext.Dispose();

            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
