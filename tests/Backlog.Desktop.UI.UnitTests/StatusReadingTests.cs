using Backlog.Modules.Backlog;
using Backlog.Modules.Backlog.DomainModels;
using Backlog.Desktop.UI.Services;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Direct metadata edits can jump to any status, while the domain lifecycle
/// graph still documents the guided transition flow for callers that need it.
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
    public void A_skipped_status_is_previewed()
    {
        var row = RowAt(EntryStatus.Draft, "!in-progress");

        Assert.Equal(EntryStatus.InProgress, row.PreviewStatus);
        Assert.Null(row.BlockedStatus);
    }

    [Fact]
    public void Restating_the_current_status_is_not_a_refusal()
    {
        var row = RowAt(EntryStatus.InProgress, "!in-progress");

        Assert.Equal(EntryStatus.InProgress, row.PreviewStatus);
        Assert.Null(row.BlockedStatus);
    }

    [Fact]
    public void A_skipped_status_is_read_without_a_refusal_note()
    {
        var row = RowAt(EntryStatus.Draft, "!done");

        var status = row.MetaReadings.Single(r => r.Kind == "status");

        Assert.Equal("done", status.Value);
        Assert.Null(status.Note);
    }

    [Fact]
    public void An_accepted_status_carries_no_note()
    {
        var row = RowAt(EntryStatus.Draft, "!ready");

        var status = row.MetaReadings.Single(r => r.Kind == "status");

        Assert.Equal("ready", status.Value);
        Assert.Null(status.Note);
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
        // A dead-end status would strand guided lifecycle callers with no next step.
        foreach (var status in Enum.GetValues<EntryStatus>())
        {
            Assert.NotEmpty(BacklogEntry.NextStatusesFrom(status));
        }
    }
}
