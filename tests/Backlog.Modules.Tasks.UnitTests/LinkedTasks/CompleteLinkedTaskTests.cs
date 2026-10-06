using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Features.CompleteLinkedTask;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.UnitTests.LinkedTasks;

/// <summary>
/// Finishing a linked task's item at its source when the person finishes the task
/// here (ADR 0020, Consequences §8). What is worth holding: the source is asked only
/// when every condition holds — a linked task, Done here, open there as far as the
/// last sync knows, a connector that can, a target the person switched it on for —
/// a refusal is recorded on the task with its reason and nothing else is touched,
/// and the answer is written onto the task as it is after the source answered, not
/// as it was before.
/// </summary>
public sealed class CompleteLinkedTaskTests
{
    private const string Repo = "JSdotNet/Backlog";
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private readonly CompletingConnector _connector = new();
    private readonly InMemoryTaskRepository _tasks = new();
    private readonly InMemoryConnectedTargets _targets = new(new ConnectedTarget(CompletingConnector.Id, Repo) { CompleteAtSource = true });

    // --- Asking -------------------------------------------------------------------

    [Fact]
    public async Task A_done_linked_task_has_its_item_completed_at_the_source()
    {
        var task = Seed(Linked());
        var writes = _tasks.Writes;

        var outcome = await CompleteAsync(task.Id);

        Assert.Equal(LinkedTaskWriteBackOutcome.Completed, outcome);
        Assert.Equal(task.SourceRef, Assert.Single(_connector.Completed));

        // Nothing to record is nothing to save, and the status and the held state are
        // the person's and the sync's — never the write-back's.
        Assert.Equal(writes, _tasks.Writes);
        Assert.Equal(EntryStatus.Done, task.Status);
        Assert.Equal(NormalisedSourceState.Open, task.SourceRef!.NormalisedState);
    }

    /// <summary>Switching the sync off stops new items arriving; it does not take
    /// back the separate opt-in to finish items at the source.</summary>
    [Fact]
    public async Task A_target_whose_sync_is_off_still_completes_at_the_source_when_that_is_on()
    {
        var task = Seed(Linked());
        _targets.Save(new ConnectedTarget(CompletingConnector.Id, Repo, Enabled: false) { CompleteAtSource = true });

        Assert.Equal(LinkedTaskWriteBackOutcome.Completed, await CompleteAsync(task.Id));
        Assert.Single(_connector.Completed);
    }

    [Fact]
    public async Task The_target_is_found_whatever_case_the_task_spells_it_in()
    {
        var task = Seed(Linked(target: "jsdotnet/backlog"));

        Assert.Equal(LinkedTaskWriteBackOutcome.Completed, await CompleteAsync(task.Id));
    }

    // --- Not asking -----------------------------------------------------------------

    [Theory]
    [InlineData(EntryStatus.Ready)]
    [InlineData(EntryStatus.InProgress)]
    [InlineData(EntryStatus.Archived)]
    public async Task A_task_that_is_not_done_is_not_completed_at_the_source(EntryStatus status)
    {
        var task = Seed(Linked(status: status));

        await AssertNotAskedAsync(task.Id);
    }

    [Fact]
    public async Task Local_work_is_not_completed_anywhere()
    {
        var task = Seed(new TaskItem(Guid.NewGuid(), "Local", string.Empty, EntryType.Task, EntryStatus.Done, Priority.Medium, null, null, null, Now));

        await AssertNotAskedAsync(task.Id);
    }

    [Fact]
    public async Task A_task_that_no_longer_exists_is_not_completed_at_the_source()
    {
        await AssertNotAskedAsync(Guid.NewGuid());
    }

    /// <summary>The source already finished or dropped the item as far as the last
    /// sync knows, so there is nothing to ask it.</summary>
    [Theory]
    [InlineData(NormalisedSourceState.Done)]
    [InlineData(NormalisedSourceState.Dropped)]
    public async Task An_item_the_source_already_closed_is_not_asked_again(NormalisedSourceState held)
    {
        var task = Seed(Linked(held: held));

        await AssertNotAskedAsync(task.Id);
    }

    [Fact]
    public async Task A_connector_that_is_not_installed_is_not_asked()
    {
        var task = Seed(Linked(connectorId: "jira"));

        await AssertNotAskedAsync(task.Id);
    }

    [Fact]
    public async Task A_connector_that_cannot_complete_is_not_asked()
    {
        var cannot = new CompletingConnector(canComplete: false);
        var task = Seed(Linked());

        Assert.Equal(LinkedTaskWriteBackOutcome.NotAsked, await CompleteAsync(task.Id, cannot));
        Assert.Empty(cannot.Completed);
    }

    [Fact]
    public async Task A_target_that_is_not_connected_is_not_asked()
    {
        var task = Seed(Linked());
        _targets.Remove(CompletingConnector.Id, Repo);

        await AssertNotAskedAsync(task.Id);
    }

    [Fact]
    public async Task A_target_without_complete_at_source_is_not_asked()
    {
        var task = Seed(Linked());
        _targets.Save(new ConnectedTarget(CompletingConnector.Id, Repo));

        await AssertNotAskedAsync(task.Id);
    }

    // --- Refusals -------------------------------------------------------------------

    [Fact]
    public async Task A_refusal_is_recorded_on_the_task_with_its_reason_and_the_task_stays_done()
    {
        var task = Seed(Linked());
        _connector.Refusal = "GitHub refused (403): Resource not accessible by personal access token";

        var outcome = await CompleteAsync(task.Id);

        Assert.Equal(LinkedTaskWriteBackOutcome.Refused, outcome);
        var stored = _tasks.Entries[task.Id];
        Assert.Equal("GitHub refused (403): Resource not accessible by personal access token", stored.SourceRef!.WriteBackRefusal);
        Assert.Equal(EntryStatus.Done, stored.Status);
        Assert.Equal(NormalisedSourceState.Open, stored.SourceRef.NormalisedState);
    }

    [Fact]
    public async Task A_connector_that_throws_is_read_as_a_refusal_with_the_exceptions_message()
    {
        var task = Seed(Linked());
        _connector.Failure = new HttpRequestException("No such host is known.");

        var outcome = await CompleteAsync(task.Id);

        Assert.Equal(LinkedTaskWriteBackOutcome.Refused, outcome);
        Assert.Equal("No such host is known.", _tasks.Entries[task.Id].SourceRef!.WriteBackRefusal);
    }

    /// <summary>The app closing cancels the write-back, and a write-back the app
    /// asked to stop has nothing to say about the source.</summary>
    [Fact]
    public async Task A_write_back_cancelled_by_the_caller_records_nothing()
    {
        var task = Seed(Linked());
        using var stopping = new CancellationTokenSource();
        _connector.DuringCompletion = stopping.Cancel;
        _connector.Failure = new OperationCanceledException(stopping.Token);
        var writes = _tasks.Writes;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CompleteAsync(task.Id, cancellationToken: stopping.Token));

        Assert.Equal(writes, _tasks.Writes);
        Assert.Null(task.SourceRef!.WriteBackRefusal);
    }

    /// <summary>An <c>HttpClient</c> timeout throws a cancellation nobody asked for;
    /// the source may or may not have acted, and the person is told so.</summary>
    [Fact]
    public async Task A_source_that_times_out_is_recorded_as_a_refusal_that_says_so()
    {
        var task = Seed(Linked());
        _connector.Failure = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");

        var outcome = await CompleteAsync(task.Id);

        Assert.Equal(LinkedTaskWriteBackOutcome.Refused, outcome);
        Assert.Equal(
            "The source did not answer in time; the item may not have been completed there.",
            _tasks.Entries[task.Id].SourceRef!.WriteBackRefusal);
    }

    /// <summary>A refusal is drawn on a row's metadata line, so only its first line
    /// is kept, and that cut short.</summary>
    [Fact]
    public async Task A_long_refusal_is_kept_to_its_first_line_and_cut_short()
    {
        var task = Seed(Linked());
        _connector.Refusal = "  " + new string('x', 400) + "\n   at Backlog.Somewhere()\n   at Backlog.Elsewhere()";

        await CompleteAsync(task.Id);

        var refusal = _tasks.Entries[task.Id].SourceRef!.WriteBackRefusal!;
        Assert.Equal(CompleteLinkedTaskCommandHandler.RefusalLimit, refusal.Length);
        Assert.EndsWith("…", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("Backlog.Somewhere", refusal, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GitHub refused (403): Not allowed.\r\nDetails follow.", "GitHub refused (403): Not allowed.")]
    [InlineData("\n\n  Refused after a blank line  \n", "Refused after a blank line")]
    [InlineData("   ", "The source refused.")]
    public void A_refusal_is_read_from_its_first_line_that_says_something(string answered, string recorded)
    {
        Assert.Equal(recorded, CompleteLinkedTaskCommandHandler.Shorten(answered));
    }

    [Fact]
    public async Task A_later_success_clears_an_earlier_refusal()
    {
        var task = Seed(Linked());
        task.SetSourceRef(task.SourceRef! with { WriteBackRefusal = "Refused the first time" });

        var outcome = await CompleteAsync(task.Id);

        Assert.Equal(LinkedTaskWriteBackOutcome.Completed, outcome);
        Assert.Null(_tasks.Entries[task.Id].SourceRef!.WriteBackRefusal);
    }

    // --- Reading the task again ---------------------------------------------------------

    /// <summary>The source is on the network, and the person may edit the task while
    /// it answers. The answer is written onto the task as it is then, so the edit
    /// survives.</summary>
    [Fact]
    public async Task The_answer_is_written_onto_the_task_as_it_is_after_the_source_answered()
    {
        var task = Seed(Linked());
        var edited = Linked(id: task.Id);
        edited.Rename("Renamed while the source was asked");
        edited.UpdateContent("Notes added meanwhile.");
        _connector.Refusal = "Refused";
        _connector.DuringCompletion = () => _tasks.Entries[task.Id] = edited;

        await CompleteAsync(task.Id);

        var stored = _tasks.Entries[task.Id];
        Assert.Same(edited, stored);
        Assert.Equal("Renamed while the source was asked", stored.Title);
        Assert.Equal("Notes added meanwhile.", stored.ContentMd);
        Assert.Equal("Refused", stored.SourceRef!.WriteBackRefusal);
    }

    /// <summary>Reopening is what clears a refusal, so one that arrives after the
    /// person reopened the task has nothing left to explain.</summary>
    [Fact]
    public async Task A_refusal_for_a_task_reopened_meanwhile_is_not_recorded()
    {
        var task = Seed(Linked());
        _connector.Refusal = "Refused";
        _connector.DuringCompletion = () => task.SetStatus(EntryStatus.Ready, DateOnly.FromDateTime(Now.Date));

        await CompleteAsync(task.Id);

        Assert.Null(_tasks.Entries[task.Id].SourceRef!.WriteBackRefusal);
    }

    [Fact]
    public async Task A_task_deleted_meanwhile_is_left_deleted()
    {
        var task = Seed(Linked());
        _connector.Refusal = "Refused";
        _connector.DuringCompletion = task.MarkDeleted;
        var writes = _tasks.Writes;

        await CompleteAsync(task.Id);

        Assert.Equal(writes, _tasks.Writes);
        Assert.Null(task.SourceRef!.WriteBackRefusal);
    }

    // --- Helpers ---------------------------------------------------------------------------

    private async Task AssertNotAskedAsync(Guid taskId)
    {
        var writes = _tasks.Writes;

        Assert.Equal(LinkedTaskWriteBackOutcome.NotAsked, await CompleteAsync(taskId));

        Assert.Empty(_connector.Completed);
        Assert.Equal(writes, _tasks.Writes);
    }

    private async Task<LinkedTaskWriteBackOutcome> CompleteAsync(Guid taskId, ITaskConnector? connector = null, CancellationToken? cancellationToken = null)
    {
        var handler = new CompleteLinkedTaskCommandHandler([connector ?? _connector], _tasks, _targets);

        Result<LinkedTaskWriteBackOutcome> result = await handler.Handle(
            new CompleteLinkedTaskCommand(taskId),
            cancellationToken ?? TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private TaskItem Seed(TaskItem task)
    {
        _tasks.Entries[task.Id] = task;
        return task;
    }

    private static TaskItem Linked(
        Guid? id = null,
        EntryStatus status = EntryStatus.Done,
        NormalisedSourceState held = NormalisedSourceState.Open,
        string connectorId = CompletingConnector.Id,
        string target = Repo)
    {
        var task = new TaskItem(id ?? Guid.NewGuid(), "Ship the installer", string.Empty, EntryType.Task, status, Priority.Medium, null, null, null, Now);
        task.SetSourceRef(new SourceRef(connectorId, target, "I_1", "https://example.test/I_1", "#1", null, "open", Now, normalisedState: held));
        return task;
    }
}
