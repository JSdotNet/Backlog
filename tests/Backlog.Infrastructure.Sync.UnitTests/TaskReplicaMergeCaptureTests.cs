using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// A capture document is not a task. It goes to the Inbox's intake whole, never
/// to the task store, and how it is counted follows what the intake said —
/// which is the only thing about a capture the merge is allowed to know.
/// </summary>
public sealed class TaskReplicaMergeCaptureTests
{
    private static readonly Guid Phone = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_capture_goes_to_the_intake_and_not_to_the_task_store()
    {
        var tasks = new InMemoryTaskStore();
        var inbox = new RecordingInboxIntake();
        var capture = Captures.Change("Call the dentist", Noon);

        var merged = await new TaskReplicaMerge(tasks, inbox)
            .ApplyAsync([Captures.Record(capture, Phone, 100)], DateTimeOffset.MinValue, TestContext.Current.CancellationToken);

        Assert.Equal(1, merged.Applied);
        Assert.Equal(0, merged.Skipped);
        Assert.Empty(tasks.Tasks);
        Assert.Empty(tasks.Writes);

        var received = Assert.Single(inbox.Received);
        Assert.Equal(capture.Id, received.Id);
        Assert.Equal("Call the dentist", received.Title);
        Assert.Equal("phone", received.Channel);
        Assert.Equal(Noon, received.CapturedAt);
        Assert.Null(received.WithdrawnAt);
    }

    [Fact]
    public async Task A_tombstoned_capture_carries_its_withdrawal_stamp()
    {
        var inbox = new RecordingInboxIntake { Answer = InboxIntakeOutcome.Withdrawn };
        var capture = Captures.Change("Dismissed on the phone", Noon, deletedAt: Noon.AddMinutes(5));

        var merged = await new TaskReplicaMerge(new InMemoryTaskStore(), inbox)
            .ApplyAsync([Captures.Record(capture, Phone, 100)], DateTimeOffset.MinValue, TestContext.Current.CancellationToken);

        Assert.Equal(1, merged.Applied);
        Assert.Equal(Noon.AddMinutes(5), Assert.Single(inbox.Received).WithdrawnAt);
    }

    [Theory]
    [InlineData(InboxIntakeOutcome.AlreadyKnown)]
    [InlineData(InboxIntakeOutcome.Ignored)]
    public async Task A_capture_the_inbox_had_nothing_to_do_with_is_held_not_applied(InboxIntakeOutcome answer)
    {
        var inbox = new RecordingInboxIntake { Answer = answer };

        var merged = await new TaskReplicaMerge(new InMemoryTaskStore(), inbox)
            .ApplyAsync([Captures.Record(Captures.Change("Echo", Noon), Phone, 100)], DateTimeOffset.MinValue, TestContext.Current.CancellationToken);

        Assert.Equal(0, merged.Applied);
        Assert.Equal(0, merged.Skipped);
        Assert.Single(inbox.Received);
    }

    /// <summary>The phone, or any head composed without an inbox store: the
    /// capture stays on the replica and the task store is untouched. Not
    /// counted as unreadable — nothing is wrong with the document.</summary>
    [Fact]
    public async Task Without_an_intake_a_capture_is_held_and_the_task_store_untouched()
    {
        var tasks = new InMemoryTaskStore();

        var merged = await new TaskReplicaMerge(tasks)
            .ApplyAsync([Captures.Record(Captures.Change("Call the dentist", Noon), Phone, 100)], DateTimeOffset.MinValue, TestContext.Current.CancellationToken);

        Assert.Equal(0, merged.Applied);
        Assert.Equal(0, merged.Skipped);
        Assert.Empty(tasks.Tasks);
    }

    /// <summary>The intake never parses a token, so a capture carrying a status
    /// this build has no member for still lands — where the same token on a task
    /// would have it skipped as unreadable.</summary>
    [Fact]
    public async Task A_capture_with_an_unreadable_status_still_reaches_the_intake()
    {
        var inbox = new RecordingInboxIntake();

        var merged = await new TaskReplicaMerge(new InMemoryTaskStore(), inbox)
            .ApplyAsync(
                [Captures.Record(Captures.Change("From a newer phone", Noon, status: "hovering"), Phone, 100)],
                DateTimeOffset.MinValue,
                TestContext.Current.CancellationToken);

        Assert.Equal(1, merged.Applied);
        Assert.Equal(0, merged.Skipped);
        Assert.Single(inbox.Received);
    }

    /// <summary>The intake can refuse a document — a blank title the push
    /// endpoint let through, say — and it does so by throwing. That has to be
    /// the same skip a task the build cannot read gets: counted, logged, and
    /// the rest of the page applied. An exception escaping here would leave
    /// the pull cursor where it was, and the same page would throw again on
    /// every sync after.</summary>
    [Fact]
    public async Task A_capture_the_intake_refuses_is_skipped_and_the_rest_of_the_page_still_applies()
    {
        var tasks = new InMemoryTaskStore();
        var inbox = new RecordingInboxIntake
        {
            Refuse = capture => capture.Title.Length == 0 ? new ArgumentException("Title is required.") : null
        };

        var merged = await new TaskReplicaMerge(tasks, inbox).ApplyAsync(
            [
                Captures.Record(Captures.Change(string.Empty, Noon), Phone, 100),
                Captures.Record(Captures.Change("Kept", Noon), Phone, 101),
                TaskChanges.Record(TaskChanges.Change("A task", Noon), Phone, 102),
            ],
            DateTimeOffset.MinValue,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, merged.Applied);
        Assert.Equal(1, merged.Skipped);
        Assert.Equal(2, inbox.Received.Count);
        Assert.Equal("A task", Assert.Single(tasks.Tasks.Values).Title);
    }

    /// <summary>
    /// A capture written before the <c>capture</c> kind existed is a
    /// <c>task</c> document with <c>sourceInboxId: "mobile"</c>, and nothing
    /// tells it apart from a task now. It lands as the draft task it claims to
    /// be, with the phone's name kept as its source — accepted once, as
    /// documented on the merge's capture branch: one user, both ends upgraded
    /// together, and the handful of such documents are a draft each to tidy
    /// rather than a rule to carry for ever. Once, because the same stamps
    /// arriving again are an echo.
    /// </summary>
    [Fact]
    public async Task A_legacy_capture_document_becomes_a_draft_task_once()
    {
        var tasks = new InMemoryTaskStore();
        var inbox = new RecordingInboxIntake();
        var page = new[] { Captures.Record(Captures.LegacyChange("Call the dentist", Noon), Phone, 100) };
        var merge = new TaskReplicaMerge(tasks, inbox);

        var first = await merge.ApplyAsync(page, DateTimeOffset.MinValue, TestContext.Current.CancellationToken);
        var again = await merge.ApplyAsync(page, DateTimeOffset.MinValue, TestContext.Current.CancellationToken);

        Assert.Equal(1, first.Applied);
        Assert.Equal(0, again.Applied);
        Assert.Empty(inbox.Received);

        var task = Assert.Single(tasks.Tasks.Values);
        Assert.Equal("Call the dentist", task.Title);
        Assert.Equal(EntryStatus.Draft, task.Status);
        Assert.Equal("mobile", task.SourceInboxId);
        Assert.Single(tasks.Writes);
    }

    /// <summary>A page that mixes both kinds keeps both paths apart: the task
    /// lands in the store, the capture in the inbox, and the counts add up.</summary>
    [Fact]
    public async Task Tasks_and_captures_on_one_page_each_go_their_own_way()
    {
        var tasks = new InMemoryTaskStore();
        var inbox = new RecordingInboxIntake();

        var merged = await new TaskReplicaMerge(tasks, inbox).ApplyAsync(
            [
                TaskChanges.Record(TaskChanges.Change("A task", Noon), Phone, 100),
                Captures.Record(Captures.Change("A capture", Noon), Phone, 101),
            ],
            DateTimeOffset.MinValue,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, merged.Applied);
        Assert.Equal("A task", Assert.Single(tasks.Tasks.Values).Title);
        Assert.Equal("A capture", Assert.Single(inbox.Received).Title);
    }
}
