using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Features.SaveTaskFromText;
using Backlog.Modules.Tasks.Services;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// <see cref="TaskItem.BlockedSince"/> records the day a person marked a task
/// blocked by hand, and is a stored fact distinct from the Blocked readiness a
/// dependency chain derives.
/// <para>
/// The aggregate half is driven directly, against a date the test names: marking
/// a task blocked reads no clock and runs no lifecycle rule, so what is pinned is
/// that the mark is a plain value and that nothing else moves with it. The save
/// half goes through the real save use case, because what matters there is that a
/// <c>blocked:</c> token typed into the text lands on the aggregate and that
/// deleting the token is how a task is unblocked.
/// </para>
/// </summary>
public sealed class TaskItemBlockedTests
{
    /// <summary>The day the aggregate half says it is. Deliberately not today, so
    /// a stamp read off a real clock cannot pass for it.</summary>
    private static readonly DateOnly Day = new(2025, 3, 14);

    [Fact]
    public void A_new_entry_is_not_marked_blocked()
    {
        var entry = new TaskItem("Title", string.Empty, EntryType.Task);

        Assert.Null(entry.BlockedSince);
        Assert.False(entry.IsBlocked);
    }

    [Fact]
    public void Marking_an_entry_blocked_records_the_day_and_clearing_it_unblocks()
    {
        var entry = new TaskItem("Title", string.Empty, EntryType.Task);

        entry.SetBlockedSince(Day);
        Assert.Equal(Day, entry.BlockedSince);
        Assert.True(entry.IsBlocked);

        entry.SetBlockedSince(null);
        Assert.Null(entry.BlockedSince);
        Assert.False(entry.IsBlocked);
    }

    /// <summary>The mark is not a status and not a tick. Marking a task blocked
    /// leaves its lifecycle and its completion exactly where they were, and a
    /// marked task can still be ticked off — the obstacle may simply have gone
    /// away with the work.</summary>
    [Fact]
    public void Marking_blocked_moves_nothing_else_and_a_marked_entry_can_still_be_ticked()
    {
        var entry = new TaskItem("Title", string.Empty, EntryType.Task);
        entry.SetStatus(EntryStatus.InProgress, Day);

        entry.SetBlockedSince(Day);

        Assert.Equal(EntryStatus.InProgress, entry.Status);
        Assert.Null(entry.CompletedOn);

        entry.SetCompletedOn(Day);

        Assert.True(entry.IsCompleted);
        Assert.True(entry.IsBlocked);
    }

    [Fact]
    public async Task Saving_text_with_a_blocked_token_marks_the_entry_and_deleting_it_unblocks()
    {
        var store = new InMemoryTaskRepository();

        var id = await Save(store, null, "# Title\n`task` `!ready` `blocked:2026-10-05`\n");
        Assert.Equal(new DateOnly(2026, 10, 5), store.Entries[id].BlockedSince);

        await Save(store, id, "# Title\n`task` `!ready`\n");
        Assert.Null(store.Entries[id].BlockedSince);
    }

    [Fact]
    public void The_published_entry_carries_the_mark()
    {
        var entry = new TaskItem("Title", string.Empty, EntryType.Task);
        entry.SetBlockedSince(Day);

        // The DTO is what the canonical line is rebuilt from, so a mark the DTO
        // did not carry would be destroyed by the next save.
        Assert.Equal(Day, entry.ToDto().BlockedSince);
    }

    /// <summary>What was in the way of last week's occurrence is not known to be
    /// in the way of this one, so a repeat's successor starts unblocked — the
    /// "does not carry" half of <see cref="RecurrencePolicy.NextOccurrence"/>.</summary>
    [Fact]
    public void A_recurrence_successor_does_not_inherit_the_block()
    {
        var completed = new TaskItem("Weekly review", string.Empty, EntryType.Task);
        completed.SetRecurrence(new Recurrence(1, RecurrenceUnit.Week));
        completed.SetBlockedSince(Day);

        var successor = RecurrencePolicy.NextOccurrence(completed);

        Assert.Null(successor.BlockedSince);
        Assert.False(successor.IsBlocked);
    }

    private static async Task<Guid> Save(InMemoryTaskRepository store, Guid? id, string rawText)
    {
        var result = await new SaveTaskFromTextCommandHandler(store, new FakeRepositoryDirectory())
            .Handle(new SaveTaskFromTextCommand(id, rawText, 0));

        Assert.True(result.IsSuccess);
        return result.Value.Entry.Id;
    }
}
