using Backlog.Entries;
using Xunit;

namespace Backlog.Entries.UnitTests;

public class BacklogEntryLifecycleTests
{
    private static BacklogEntry NewEntry() =>
        new("Write release notes", "body", EntryType.Task);

    [Fact]
    public void ManuallyCreatedEntry_DefaultsToDraft_WithNoSourceInbox()
    {
        var entry = NewEntry();

        Assert.Equal(EntryStatus.Draft, entry.Status);
        Assert.Null(entry.SourceInboxId);
        Assert.Equal(Priority.Medium, entry.Priority);
    }

    [Theory]
    [InlineData(EntryStatus.Draft, EntryStatus.Ready)]
    [InlineData(EntryStatus.Ready, EntryStatus.InProgress)]
    [InlineData(EntryStatus.Ready, EntryStatus.Draft)]
    [InlineData(EntryStatus.InProgress, EntryStatus.Done)]
    [InlineData(EntryStatus.InProgress, EntryStatus.Ready)]
    [InlineData(EntryStatus.Done, EntryStatus.Archived)]
    [InlineData(EntryStatus.Done, EntryStatus.InProgress)]
    [InlineData(EntryStatus.Archived, EntryStatus.Draft)]
    public void ValidTransitions_AreAllowed(EntryStatus from, EntryStatus to)
    {
        var entry = MoveTo(from);

        entry.ChangeStatus(to);

        Assert.Equal(to, entry.Status);
    }

    [Theory]
    [InlineData(EntryStatus.Draft, EntryStatus.InProgress)]
    [InlineData(EntryStatus.Draft, EntryStatus.Done)]
    [InlineData(EntryStatus.Draft, EntryStatus.Archived)]
    [InlineData(EntryStatus.Ready, EntryStatus.Done)]
    [InlineData(EntryStatus.Ready, EntryStatus.Archived)]
    [InlineData(EntryStatus.InProgress, EntryStatus.Draft)]
    [InlineData(EntryStatus.InProgress, EntryStatus.Archived)]
    [InlineData(EntryStatus.Done, EntryStatus.Draft)]
    [InlineData(EntryStatus.Done, EntryStatus.Ready)]
    [InlineData(EntryStatus.Archived, EntryStatus.Ready)]
    [InlineData(EntryStatus.Archived, EntryStatus.InProgress)]
    [InlineData(EntryStatus.Archived, EntryStatus.Done)]
    public void InvalidTransitions_Throw(EntryStatus from, EntryStatus to)
    {
        var entry = MoveTo(from);

        var ex = Assert.Throws<InvalidStatusTransitionException>(() => entry.ChangeStatus(to));
        Assert.Equal(from, ex.From);
        Assert.Equal(to, ex.To);
        Assert.Equal(from, entry.Status); // unchanged
    }

    [Fact]
    public void ChangeStatus_ToSameStatus_IsNoOp()
    {
        var entry = NewEntry();
        entry.ChangeStatus(EntryStatus.Draft);
        Assert.Equal(EntryStatus.Draft, entry.Status);
    }

    // Walk the lifecycle forward to reach a desired starting state.
    private static BacklogEntry MoveTo(EntryStatus target)
    {
        var entry = NewEntry();
        switch (target)
        {
            case EntryStatus.Draft:
                break;
            case EntryStatus.Ready:
                entry.ChangeStatus(EntryStatus.Ready);
                break;
            case EntryStatus.InProgress:
                entry.ChangeStatus(EntryStatus.Ready);
                entry.ChangeStatus(EntryStatus.InProgress);
                break;
            case EntryStatus.Done:
                entry.ChangeStatus(EntryStatus.Ready);
                entry.ChangeStatus(EntryStatus.InProgress);
                entry.ChangeStatus(EntryStatus.Done);
                break;
            case EntryStatus.Archived:
                entry.ChangeStatus(EntryStatus.Ready);
                entry.ChangeStatus(EntryStatus.InProgress);
                entry.ChangeStatus(EntryStatus.Done);
                entry.ChangeStatus(EntryStatus.Archived);
                break;
        }
        return entry;
    }
}

public class BacklogEntrySubItemTests
{
    private static BacklogEntry NewEntry() =>
        new("Ship feature", "body", EntryType.Task);

    [Fact]
    public void AddSubItem_AppendsInOrder()
    {
        var entry = NewEntry();

        var a = entry.AddSubItem("first");
        var b = entry.AddSubItem("second");

        Assert.Equal(2, entry.TotalSubItemCount);
        Assert.Equal(0, a.Order);
        Assert.Equal(1, b.Order);
    }

    [Fact]
    public void ToggleSubItem_FlipsBetweenPendingAndDone()
    {
        var entry = NewEntry();
        var s = entry.AddSubItem("task");

        entry.ToggleSubItem(s.Id);
        Assert.Equal(SubItemStatus.Done, s.Status);

        entry.ToggleSubItem(s.Id);
        Assert.Equal(SubItemStatus.Pending, s.Status);
    }

    [Fact]
    public void RemoveSubItem_ReindexesRemaining()
    {
        var entry = NewEntry();
        var a = entry.AddSubItem("a");
        var b = entry.AddSubItem("b");
        var c = entry.AddSubItem("c");

        entry.RemoveSubItem(b.Id);

        Assert.Equal(2, entry.TotalSubItemCount);
        Assert.Equal(0, a.Order);
        Assert.Equal(1, c.Order);
    }

    [Fact]
    public void ReorderSubItem_MovesAndReindexes()
    {
        var entry = NewEntry();
        var a = entry.AddSubItem("a");
        var b = entry.AddSubItem("b");
        var c = entry.AddSubItem("c");

        entry.ReorderSubItem(c.Id, 0);

        Assert.Equal(0, c.Order);
        Assert.Equal(1, a.Order);
        Assert.Equal(2, b.Order);
        Assert.Equal(new[] { c.Id, a.Id, b.Id }, entry.SubItems.Select(s => s.Id));
    }

    [Fact]
    public void ParentProgress_ReflectsSubItemCompletion()
    {
        var entry = NewEntry();
        var items = Enumerable.Range(1, 5).Select(i => entry.AddSubItem($"s{i}")).ToList();

        entry.ToggleSubItem(items[0].Id);
        entry.ToggleSubItem(items[1].Id);
        entry.ToggleSubItem(items[2].Id);

        Assert.Equal(3, entry.CompletedSubItemCount);
        Assert.Equal(5, entry.TotalSubItemCount);
        Assert.Equal(0.6d, entry.Progress, 3);
    }

    [Fact]
    public void Progress_IsZero_WhenNoSubItems()
    {
        var entry = NewEntry();
        Assert.Equal(0d, entry.Progress);
    }

    [Fact]
    public void AddSubItem_WithBlankTitle_Throws()
    {
        var entry = NewEntry();
        Assert.Throws<ArgumentException>(() => entry.AddSubItem("  "));
    }
}

public class ValueObjectTests
{
    [Fact]
    public void ProjectionRef_HasValueEquality()
    {
        var a = new ProjectionRef("repo", "42", "github-issue");
        var b = new ProjectionRef("repo", "42", "github-issue");
        Assert.Equal(a, b);
    }

    [Fact]
    public void UsageEvent_HasValueEquality()
    {
        var ts = DateTimeOffset.UtcNow;
        Assert.Equal(new UsageEvent(ts, "copy"), new UsageEvent(ts, "copy"));
    }
}
