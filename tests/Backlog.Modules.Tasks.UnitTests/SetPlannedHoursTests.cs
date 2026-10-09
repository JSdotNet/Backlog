using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Features.SaveTaskFromText;
using Backlog.Modules.Tasks.Features.SetPlannedHours;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// Setting the hours a person sets aside for a task on given days, as a use case.
/// <para>
/// The blocks are the task's own field, beside its text rather than in it, as the
/// Devbook references are: the text save neither writes nor clears them. A block is a
/// day and a number of hours; one a day.
/// </para>
/// </summary>
public sealed class SetPlannedHoursTests
{
    private static readonly DateOnly Monday = new(2026, 10, 12);

    [Fact]
    public async Task The_whole_list_is_written_in_date_order_and_answered()
    {
        var store = new InMemoryTaskRepository();
        var entry = new TaskItem("Refactor sync", string.Empty, EntryType.Task);
        await store.SaveAsync(entry, TestContext.Current.CancellationToken);
        store.Writes = 0;

        var result = await Handle(store, entry.Id, [new(Monday.AddDays(2), 2m), new(Monday, 3m)]);

        Assert.True(result.IsSuccess);
        Assert.Equal([new PlannedHoursDto(Monday, 3m), new PlannedHoursDto(Monday.AddDays(2), 2m)], result.Value.PlannedHours);
        Assert.Equal([Monday, Monday.AddDays(2)], store.Entries[entry.Id].PlannedHours.Select(block => block.On));
        Assert.Equal(1, store.Writes);
    }

    /// <summary>A day holds one block: the last figure given for a day is the one kept.</summary>
    [Fact]
    public async Task A_day_holds_one_block_and_the_last_figure_wins()
    {
        var store = new InMemoryTaskRepository();
        var entry = new TaskItem("Refactor sync", string.Empty, EntryType.Task);
        await store.SaveAsync(entry, TestContext.Current.CancellationToken);

        var result = await Handle(store, entry.Id, [new(Monday, 3m), new(Monday, 5m)]);

        Assert.Equal([new PlannedHoursDto(Monday, 5m)], result.Value.PlannedHours);
    }

    [Fact]
    public async Task A_figure_is_kept_in_quarters_of_an_hour()
    {
        var store = new InMemoryTaskRepository();
        var entry = new TaskItem("Refactor sync", string.Empty, EntryType.Task);
        await store.SaveAsync(entry, TestContext.Current.CancellationToken);

        var result = await Handle(store, entry.Id, [new(Monday, 2.4m)]);

        Assert.Equal(2.5m, Assert.Single(result.Value.PlannedHours).Hours);
    }

    [Fact]
    public async Task An_empty_list_clears_the_blocks()
    {
        var store = new InMemoryTaskRepository();
        var entry = new TaskItem("Refactor sync", string.Empty, EntryType.Task);
        entry.SetPlannedHours([PlannedHoursBlock.Create(Monday, 3m)]);
        await store.SaveAsync(entry, TestContext.Current.CancellationToken);

        var result = await Handle(store, entry.Id, []);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.PlannedHours);
        Assert.Empty(store.Entries[entry.Id].PlannedHours);
    }

    /// <summary>Zero, less, or more than a day is no number of hours to plan: the call is
    /// a validation failure and nothing is written, not even the good blocks beside it.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(24.5)]
    public async Task A_figure_out_of_range_is_refused_and_nothing_is_written(double hours)
    {
        var store = new InMemoryTaskRepository();
        var entry = new TaskItem("Refactor sync", string.Empty, EntryType.Task);
        entry.SetPlannedHours([PlannedHoursBlock.Create(Monday, 3m)]);
        await store.SaveAsync(entry, TestContext.Current.CancellationToken);
        store.Writes = 0;

        var result = await Handle(store, entry.Id, [new(Monday.AddDays(1), 2m), new(Monday.AddDays(2), (decimal)hours)]);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal(SetPlannedHoursCommandHandler.InvalidHoursCode, result.Error.Code);
        Assert.Equal(0, store.Writes);
        Assert.Equal([PlannedHoursBlock.Create(Monday, 3m)], store.Entries[entry.Id].PlannedHours);
    }

    [Fact]
    public async Task A_missing_task_is_not_found()
    {
        var result = await Handle(new InMemoryTaskRepository(), Guid.NewGuid(), [new(Monday, 3m)]);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>The blocks are not in the text, so a save of the text leaves them where
    /// they are — and writes nothing about them into it.</summary>
    [Fact]
    public async Task Saving_the_text_leaves_the_blocks_alone_and_out_of_the_text()
    {
        var store = new InMemoryTaskRepository();
        var entry = new TaskItem("Refactor sync", string.Empty, EntryType.Task);
        await store.SaveAsync(entry, TestContext.Current.CancellationToken);
        await Handle(store, entry.Id, [new(Monday, 3m)]);

        var saved = await new SaveTaskFromTextCommandHandler(store, new FakeRepositoryDirectory()).Handle(
            new SaveTaskFromTextCommand(entry.Id, "# Refactor sync, renamed\n`task` `!ready`\n\nA new body.\n", 0),
            TestContext.Current.CancellationToken);

        Assert.True(saved.IsSuccess);
        Assert.Equal([new PlannedHoursDto(Monday, 3m)], saved.Value.Entry.PlannedHours);
        Assert.Equal([PlannedHoursBlock.Create(Monday, 3m)], store.Entries[entry.Id].PlannedHours);
        Assert.DoesNotContain("3h", store.Entries[entry.Id].ContentMd, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-10-12", store.Entries[entry.Id].ContentMd, StringComparison.Ordinal);
    }

    private static Task<Result<TaskItemDto>> Handle(
        InMemoryTaskRepository store,
        Guid id,
        IReadOnlyList<PlannedHoursDto> blocks) =>
        new SetPlannedHoursCommandHandler(store)
            .Handle(new SetPlannedHoursCommand(id, blocks), TestContext.Current.CancellationToken);
}
