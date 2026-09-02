using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The act that makes the entry that waits on this one.
/// <para>
/// Assertions are about <c>RawText</c>, for the reason
/// <see cref="EntryScheduleControlsTests"/> gives: the text <em>is</em> the entry, so
/// an act that added a row without writing the <c>after:</c> token into it produced a
/// row that forgets what it was for the moment it saves. What the row inherits is
/// asserted from the same place a reader would read it — the metadata line.
/// </para>
/// <para>
/// The refusals are here too, and they are half the behaviour: an entry nothing can
/// point at yet, and one nothing can wait on any more, both keep the row and say why
/// rather than dropping it (<c>.design/interaction-guidelines.md#action-density-and-overflow</c>).
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class EntryFollowUpActionTests
{
    private const string SavedEntry =
        "# Ship the sync spike\n" +
        "`task` `*high` `!in-progress` `@backlog` `repo:backlog-desktop`\n\n" +
        "Notes on the parent.\n";

    [Fact]
    public async Task Creating_a_follow_up_adds_a_row_that_waits_on_the_open_one()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var parent = await host.WriteEntryAsync(SavedEntry);
        var before = host.State.Rows.Count;

        var pane = host.Render();
        await pane.Find("[data-testid='entry-action-followup-set']").ClickAsync(new());

        Assert.Equal(before + 1, host.State.Rows.Count);

        var followUp = host.State.Rows[^1];
        var metaLine = followUp.RawText.Split('\n')[1];

        // What it is for: the token naming the entry it comes after.
        Assert.Contains($"`after:{parent.Id}`", metaLine);

        // Where it is filed. The parent's own area and repository, not the reader's
        // current filter — a follow-up belongs beside the work it follows.
        Assert.Contains("`@backlog`", metaLine);
        Assert.Contains("`repo:backlog-desktop`", metaLine);

        // A fresh entry rather than a copy of the parent: an ordinary draft task at
        // the ordinary ranking, and nothing else carried over.
        Assert.Contains("`task`", metaLine);
        Assert.Contains("`*medium`", metaLine);
        Assert.Contains("`!draft`", metaLine);
        Assert.DoesNotContain("`*high`", metaLine);
        Assert.DoesNotContain("!in-progress", metaLine);

        // Nothing was typed into it, so nothing is claimed about it: the title is
        // empty, the new row is the open one, and the caret is already in its title —
        // the rename field being on screen is that, since the pane consumes the
        // pending intent on the render that follows the click rather than leaving the
        // flag standing for a test to read.
        Assert.Equal(string.Empty, followUp.PreviewTitle);
        Assert.Same(followUp, host.State.SelectedRow);
        Assert.True(followUp.IsUntouched);
        Assert.Single(pane.FindAll("[data-testid='entry-panel-rename']"));
    }

    /// <summary>An entry that has never been saved has no id, and an <c>after:</c>
    /// token needs one. The row stays and says so — a reader who never sees the act
    /// concludes the product cannot do it.</summary>
    [Fact]
    public async Task An_unsaved_entry_refuses_and_says_why()
    {
        using var host = await TasksPaneHost.CreateAsync();
        host.State.NewRow();
        var row = host.State.Rows[^1];

        Assert.False(row.IsPersisted);
        Assert.Same(row, host.State.SelectedRow);

        var pane = host.Render();

        Assert.True(pane.Find("[data-testid='entry-action-followup-set']").HasAttribute("disabled"));

        var reason = pane.Find("[data-testid='entry-action-followup-reason']").TextContent;
        Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.Equal(reason, pane.Find("[data-testid='entry-action-followup']").GetAttribute("title"));
    }

    /// <summary>A finished entry cannot be waited on: the wait would be over before
    /// the follow-up existed.</summary>
    [Fact]
    public async Task A_finished_entry_refuses_and_says_why()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Renew the certificate\n`task` `*medium` `!done`\n");

        Assert.Equal(EntryStatus.Done, row.PreviewStatus);
        await host.OpenAsync(row);

        var pane = host.Render();

        Assert.True(pane.Find("[data-testid='entry-action-followup-set']").HasAttribute("disabled"));

        var reason = pane.Find("[data-testid='entry-action-followup-reason']").TextContent;
        Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.Equal(reason, pane.Find("[data-testid='entry-action-followup']").GetAttribute("title"));
    }
}
