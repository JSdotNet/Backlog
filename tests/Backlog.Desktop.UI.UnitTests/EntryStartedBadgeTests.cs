using System.Globalization;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The day work started, in the Tasks detail panel. The design's "list and
/// details" artboard states <c>Started</c> beside Due, Repository, Plan and
/// Effort; the app stamps <c>started:</c> on the first move into progress, and
/// this is where a reader sees it. Read-only: nobody sets it, so it is a badge
/// with no picker and no clear.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class EntryStartedBadgeTests
{
    [Fact]
    public async Task An_entry_with_a_started_stamp_shows_the_day_it_started()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync("# Build the board\n`task` `!in-progress` `started:2026-10-01`\n\nColumns.\n");

        var pane = host.Render();

        var badge = pane.Find("[data-testid='entry-started-badge']");
        var day = new DateOnly(2026, 10, 1).ToString("d", CultureInfo.CurrentCulture);
        Assert.Equal($"Started {day}", badge.TextContent.Trim());

        // Read-only: a plain badge, not a button or a link, with nothing inside
        // it to press.
        Assert.Equal("SPAN", badge.TagName);
        Assert.Empty(badge.QuerySelectorAll("button, input, select, a"));
    }

    [Fact]
    public async Task An_entry_never_started_shows_no_started_badge()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync("# Build the board\n`task` `!ready`\n\nColumns.\n");

        var pane = host.Render();

        // The panel is open on the entry, and it simply has no Started to show.
        Assert.Single(pane.FindAll("[data-testid='entry-detail']"));
        Assert.Empty(pane.FindAll("[data-testid='entry-started-badge']"));
    }

    /// <summary>The stamp the status change writes is the one the badge reads:
    /// moving a Ready entry into progress puts today's day on it.</summary>
    [Fact]
    public async Task Starting_an_entry_puts_the_badge_on_it()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Build the board\n`task` `!ready`\n\nColumns.\n");

        var pane = host.Render();
        Assert.Empty(pane.FindAll("[data-testid='entry-started-badge']"));

        await host.State.ChangeStatusAsync(row, Backlog.Modules.Tasks.Abstractions.EntryStatus.InProgress);
        pane.Render();

        pane.WaitForAssertion(() =>
        {
            // Today's day — the stamp the status change writes, read back off the panel.
            var today = DateOnly.FromDateTime(DateTime.Now);
            Assert.Equal(today, row.PreviewStartedOn);
            var day = today.ToString("d", CultureInfo.CurrentCulture);
            Assert.Equal($"Started {day}", pane.Find("[data-testid='entry-started-badge']").TextContent.Trim());
        });
    }
}
