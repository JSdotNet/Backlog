using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The story-point control on an entry: a Fibonacci picker in the Ranking group,
/// and a badge that makes the size readable without opening it.
/// <para>
/// Like the scheduling controls, the picker writes by rewriting the metadata
/// line, so the assertions are about <c>RawText</c> and the preview read off it:
/// pressing the control and typing <c>effort:5</c> are the same edit arriving by
/// two routes, and the point of the control is that both land the same text.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class EntryEffortControlTests
{
    private const string Entry =
        "# Deploy SpecManager\n" +
        "`task` `*high` `!ready` `@backlog`\n\n" +
        "Ship it before the demo.\n";

    [Fact]
    public async Task The_effort_select_writes_the_points_it_offered()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);

        var pane = host.Render();
        await pane.Find("[data-testid='entry-action-effort-set']").ClickAsync(new());
        await pane.Find("[data-testid='entry-effort-select'] select").ChangeAsync(new() { Value = "5" });

        Assert.Equal(5, row.PreviewEffort);
        Assert.Contains("`effort:5`", row.RawText, StringComparison.Ordinal);
    }

    /// <summary>Picking "Not estimated" is how a size is retracted. It has to be
    /// reachable from the select as well as from the ✕, because a reader who opened
    /// the picker to change the number may decide the honest answer is "no
    /// estimate".</summary>
    [Fact]
    public async Task Choosing_not_estimated_clears_the_effort()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(
            "# Weekly review\n`task` `effort:8`\n\nRead the week.\n");

        var pane = host.Render();
        await pane.Find("[data-testid='entry-action-effort-set']").ClickAsync(new());
        await pane.Find("[data-testid='entry-effort-select'] select").ChangeAsync(new() { Value = string.Empty });

        Assert.Null(row.PreviewEffort);
        Assert.DoesNotContain("effort:", row.RawText, StringComparison.Ordinal);
    }

    /// <summary>The ✕ on the row clears the estimate too, the same gesture the
    /// neighbouring scheduling rows offer — an unset effort is a real state, so
    /// unlike priority the row carries a clear.</summary>
    [Fact]
    public async Task The_clear_button_retracts_the_estimate()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(
            "# Weekly review\n`task` `effort:8`\n\nRead the week.\n");

        var pane = host.Render();
        await pane.Find("[data-testid='entry-action-effort-clear']").ClickAsync(new());

        Assert.Null(row.PreviewEffort);
        Assert.DoesNotContain("effort:", row.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_badge_shows_only_once_an_estimate_is_set()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);

        var pane = host.Render();

        // Nothing estimated yet, so no badge — not a badge reading "none".
        Assert.Empty(pane.FindAll("[data-testid='entry-effort-badge']"));

        await pane.Find("[data-testid='entry-action-effort-set']").ClickAsync(new());
        await pane.Find("[data-testid='entry-effort-select'] select").ChangeAsync(new() { Value = "3" });

        var badge = pane.Find("[data-testid='entry-effort-badge']");
        Assert.Contains("3", badge.TextContent, StringComparison.Ordinal);
    }
}
