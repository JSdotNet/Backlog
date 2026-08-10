using Backlog.Entries;
using Backlog.Desktop.UI.Services;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The entry lifecycle only allows one step at a time, so a status someone
/// types is not always a status the entry can take. These cover the promise
/// that such a word is never silently dropped: the badge keeps telling the
/// truth, and the editor hint explains the refusal.
/// </summary>
public class StatusReadingTests
{
    private static EntryRow RowAt(EntryStatus saved, string typedStatusToken) => new()
    {
        Status = saved,
        RawText = $"# A thing\n`task` `{typedStatusToken}`\n"
    };

    [Fact]
    public void A_reachable_status_is_previewed()
    {
        var row = RowAt(EntryStatus.Draft, "!ready");

        Assert.Equal(EntryStatus.Ready, row.PreviewStatus);
        Assert.Null(row.BlockedStatus);
    }

    [Fact]
    public void An_unreachable_status_leaves_the_preview_alone()
    {
        var row = RowAt(EntryStatus.Draft, "!in-progress");

        Assert.Equal(EntryStatus.Draft, row.PreviewStatus);
        Assert.Equal(EntryStatus.InProgress, row.BlockedStatus);
    }

    [Fact]
    public void Restating_the_current_status_is_not_a_refusal()
    {
        var row = RowAt(EntryStatus.InProgress, "!in-progress");

        Assert.Equal(EntryStatus.InProgress, row.PreviewStatus);
        Assert.Null(row.BlockedStatus);
    }

    [Fact]
    public void A_refusal_is_explained_in_the_hint()
    {
        var row = RowAt(EntryStatus.Draft, "!done");

        var status = row.MetaReadings.Single(r => r.Kind == "status");

        Assert.Equal("draft", status.Value);
        Assert.NotNull(status.Note);
        Assert.Contains("done", status.Note);
        Assert.Contains("ready", status.Note);
    }

    [Fact]
    public void An_accepted_status_carries_no_note()
    {
        var row = RowAt(EntryStatus.Draft, "!ready");

        var status = row.MetaReadings.Single(r => r.Kind == "status");

        Assert.Equal("ready", status.Value);
        Assert.Null(status.Note);
    }

    [Fact]
    public void The_hint_lists_every_legal_next_step()
    {
        // Ready can go forward to in-progress or back to draft; a refusal from
        // there should offer both rather than only the forward one.
        var row = RowAt(EntryStatus.Ready, "!archived");

        var note = row.MetaReadings.Single(r => r.Kind == "status").Note;

        Assert.Contains("in-progress", note);
        Assert.Contains("draft", note);
    }

    [Theory]
    [InlineData(EntryStatus.Draft, EntryStatus.Ready, true)]
    [InlineData(EntryStatus.Draft, EntryStatus.InProgress, false)]
    [InlineData(EntryStatus.Draft, EntryStatus.Draft, true)]
    [InlineData(EntryStatus.Archived, EntryStatus.Draft, true)]
    [InlineData(EntryStatus.Done, EntryStatus.Draft, false)]
    public void The_lifecycle_graph_is_what_the_readme_documents(EntryStatus from, EntryStatus to, bool allowed)
        => Assert.Equal(allowed, BacklogEntry.IsTransitionAllowed(from, to));

    [Fact]
    public void Every_status_has_somewhere_to_go()
    {
        // A dead-end status would strand entries with no way out through text.
        foreach (var status in Enum.GetValues<EntryStatus>())
        {
            Assert.NotEmpty(BacklogEntry.NextStatusesFrom(status));
        }
    }
}
