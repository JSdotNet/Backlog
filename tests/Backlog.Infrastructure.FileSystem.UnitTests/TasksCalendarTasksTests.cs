using Backlog.Infrastructure.FileSystem.Calendar;
using Backlog.Modules.Calendar.Abstractions.DataTransferObjects;
using Backlog.Modules.Calendar.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.Services;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The Calendar's Tasks layer port, answered over Tasks' published port (local ADR 0056
/// §2, <c>.devbook/domain/calendar/requirements.md</c>): the dated tasks due in the window
/// and every block of planned hours in it, in the Calendar's words; the due date and the
/// hours written back through <see cref="ITaskItems"/>; and, in a host composed without
/// Tasks, a port that reads Off and refuses every write.
/// </summary>
public sealed class TasksCalendarTasksTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);
    private static readonly CalendarWindow TheWeek = new(Monday, Monday.AddDays(6));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // --- The read -------------------------------------------------------------------------

    [Fact]
    public async Task Only_the_tasks_due_in_the_window_are_read_and_the_tasks_are_available()
    {
        var inside = Entry("Inside", dueOn: Monday.AddDays(2));
        var items = new FakeTaskItems(
            Entry("Before", dueOn: Monday.AddDays(-1)),
            inside,
            Entry("After", dueOn: Monday.AddDays(7)),
            Entry("Undated"));
        using var calendar = new TasksCalendarTasks(items);

        var read = await calendar.ReadAsync(TheWeek, Ct);

        Assert.True(read.Available);
        var task = Assert.Single(read.Tasks);
        Assert.Equal(inside.Id, task.Id);
        Assert.Equal("Inside", task.Title);
        Assert.Equal(Monday.AddDays(2), task.DueOn);
    }

    [Fact]
    public async Task The_first_and_last_days_of_the_window_are_in_it()
    {
        var items = new FakeTaskItems(Entry("First", dueOn: TheWeek.First), Entry("Last", dueOn: TheWeek.Last));
        using var calendar = new TasksCalendarTasks(items);

        var read = await calendar.ReadAsync(TheWeek, Ct);

        Assert.Equal(["First", "Last"], read.Tasks.Select(task => task.Title));
    }

    /// <summary>An archived task leaves the Calendar, its hours with it (local ADR 0029).</summary>
    [Fact]
    public async Task An_archived_task_and_its_hours_are_left_out()
    {
        var items = new FakeTaskItems(Entry(
            "Archived",
            dueOn: Monday,
            status: EntryStatus.Archived,
            hours: [new PlannedHoursDto(Monday, 2m)]));
        using var calendar = new TasksCalendarTasks(items);

        var read = await calendar.ReadAsync(TheWeek, Ct);

        Assert.Empty(read.Tasks);
        Assert.Empty(read.Hours);
    }

    [Theory]
    [InlineData(EntryStatus.Draft, false, CalendarTaskStatus.Draft)]
    [InlineData(EntryStatus.Ready, false, CalendarTaskStatus.Ready)]
    [InlineData(EntryStatus.InProgress, false, CalendarTaskStatus.InProgress)]
    [InlineData(EntryStatus.Done, false, CalendarTaskStatus.Done)]
    [InlineData(EntryStatus.Ready, true, CalendarTaskStatus.Done)]
    public async Task A_ticked_task_or_one_done_is_done(EntryStatus status, bool ticked, CalendarTaskStatus expected)
    {
        var items = new FakeTaskItems(Entry("One", dueOn: Monday, status: status, completedOn: ticked ? Monday : null));
        using var calendar = new TasksCalendarTasks(items);

        var read = await calendar.ReadAsync(TheWeek, Ct);

        Assert.Equal(expected, Assert.Single(read.Tasks).Status);
    }

    [Fact]
    public async Task A_repeating_task_says_it_repeats_and_its_repositories_are_passed_through()
    {
        var items = new FakeTaskItems(
            Entry("Weekly", dueOn: Monday, recurrence: new Recurrence(1, RecurrenceUnit.Week), repoIds: ["acme/app", "acme/api"]),
            Entry("Once", dueOn: Monday));
        using var calendar = new TasksCalendarTasks(items);

        var read = await calendar.ReadAsync(TheWeek, Ct);

        Assert.Equal(
            [("Weekly", true, "acme/app,acme/api"), ("Once", false, "")],
            read.Tasks.Select(task => (task.Title, task.Repeats, string.Join(',', task.RepositoryIds))));
    }

    /// <summary>The tray went with ADR 0056, but an undated task's hours still draw: they
    /// are why the hours are a list of their own.</summary>
    [Fact]
    public async Task The_hours_in_the_window_are_read_from_dated_and_undated_tasks_alike()
    {
        var dated = Entry("Dated", dueOn: Monday.AddDays(20), repoIds: ["acme/app"], hours:
        [
            new PlannedHoursDto(Monday.AddDays(-1), 1m),
            new PlannedHoursDto(Monday.AddDays(1), 2m),
        ]);
        var undated = Entry("Undated", hours: [new PlannedHoursDto(Monday.AddDays(3), 1.5m)]);
        using var calendar = new TasksCalendarTasks(new FakeTaskItems(dated, undated));

        var read = await calendar.ReadAsync(TheWeek, Ct);

        Assert.Empty(read.Tasks);
        Assert.Equal(
            [
                (dated.Id, "Dated", Monday.AddDays(1), 2m, "acme/app"),
                (undated.Id, "Undated", Monday.AddDays(3), 1.5m, ""),
            ],
            read.Hours.Select(block => (block.TaskId, block.Title, block.On, block.Hours, string.Join(',', block.RepositoryIds))));
    }

    // --- Changed --------------------------------------------------------------------------

    [Fact]
    public void A_task_written_or_the_store_moving_says_the_tasks_changed()
    {
        var signal = new TaskChangeSignal();
        var store = new FakeTaskStore();
        using var calendar = new TasksCalendarTasks(new FakeTaskItems(), signal, store);
        var said = 0;
        calendar.Changed += () => said++;

        signal.Raise();
        store.Move();

        Assert.Equal(2, said);
    }

    [Fact]
    public void Disposing_stops_listening()
    {
        var signal = new TaskChangeSignal();
        var store = new FakeTaskStore();
        var calendar = new TasksCalendarTasks(new FakeTaskItems(), signal, store);
        var said = 0;
        calendar.Changed += () => said++;

        calendar.Dispose();
        signal.Raise();
        store.Move();

        Assert.Equal(0, said);
    }

    // --- The due date ---------------------------------------------------------------------

    [Fact]
    public async Task Moving_a_task_sets_its_due_date_through_the_tasks_port()
    {
        var task = Entry("Refactor sync", dueOn: Monday);
        var items = new FakeTaskItems(task);
        using var calendar = new TasksCalendarTasks(items);

        var refusal = await calendar.SetDueDateAsync(task.Id, Monday.AddDays(2), Ct);

        Assert.Null(refusal);
        Assert.Equal([(task.Id, (DateOnly?)Monday.AddDays(2))], items.DueWrites);
    }

    [Fact]
    public async Task A_refused_due_date_comes_back_in_words()
    {
        var items = new FakeTaskItems { Refusal = Error.NotFound("entry.not_found", "That entry no longer exists.") };
        using var calendar = new TasksCalendarTasks(items);

        var refusal = await calendar.SetDueDateAsync(Guid.NewGuid(), Monday, Ct);

        Assert.Equal("That entry no longer exists.", refusal);
    }

    // --- The planned hours ----------------------------------------------------------------

    [Fact]
    public async Task Setting_a_days_hours_adds_its_block_and_keeps_the_other_days()
    {
        var task = Entry("Refactor sync", hours: [new PlannedHoursDto(Monday.AddDays(2), 1m)]);
        var items = new FakeTaskItems(task);
        using var calendar = new TasksCalendarTasks(items);

        Assert.Null(await calendar.SetPlannedHoursAsync(task.Id, Monday, 3m, Ct));

        var (id, blocks) = Assert.Single(items.HoursWrites);
        Assert.Equal(task.Id, id);
        Assert.Equal([new PlannedHoursDto(Monday, 3m), new PlannedHoursDto(Monday.AddDays(2), 1m)], blocks);
    }

    [Fact]
    public async Task Changing_a_days_hours_replaces_its_block()
    {
        var task = Entry("Refactor sync", hours: [new PlannedHoursDto(Monday, 1m), new PlannedHoursDto(Monday.AddDays(2), 1m)]);
        var items = new FakeTaskItems(task);
        using var calendar = new TasksCalendarTasks(items);

        await calendar.SetPlannedHoursAsync(task.Id, Monday, 4m, Ct);

        Assert.Equal([new PlannedHoursDto(Monday, 4m), new PlannedHoursDto(Monday.AddDays(2), 1m)], Assert.Single(items.HoursWrites).Blocks);
    }

    [Fact]
    public async Task No_hours_removes_the_days_block_and_keeps_the_other_days()
    {
        var task = Entry("Refactor sync", hours: [new PlannedHoursDto(Monday, 1m), new PlannedHoursDto(Monday.AddDays(2), 1m)]);
        var items = new FakeTaskItems(task);
        using var calendar = new TasksCalendarTasks(items);

        await calendar.SetPlannedHoursAsync(task.Id, Monday, null, Ct);

        Assert.Equal([new PlannedHoursDto(Monday.AddDays(2), 1m)], Assert.Single(items.HoursWrites).Blocks);
    }

    [Fact]
    public async Task The_same_figure_again_or_removing_a_block_that_is_not_there_writes_nothing()
    {
        var task = Entry("Refactor sync", hours: [new PlannedHoursDto(Monday, 2m)]);
        var items = new FakeTaskItems(task);
        using var calendar = new TasksCalendarTasks(items);

        Assert.Null(await calendar.SetPlannedHoursAsync(task.Id, Monday, 2m, Ct));
        Assert.Null(await calendar.SetPlannedHoursAsync(task.Id, Monday.AddDays(1), null, Ct));

        Assert.Empty(items.HoursWrites);
    }

    [Fact]
    public async Task A_refused_figure_comes_back_in_words()
    {
        var task = Entry("Refactor sync");
        var items = new FakeTaskItems(task) { Refusal = Error.Validation("entry.invalid_planned_hours", "25h is not a number of hours to plan.") };
        using var calendar = new TasksCalendarTasks(items);

        var refusal = await calendar.SetPlannedHoursAsync(task.Id, Monday, 25m, Ct);

        Assert.Equal("25h is not a number of hours to plan.", refusal);
    }

    [Fact]
    public async Task Hours_for_a_task_that_is_gone_are_refused()
    {
        var items = new FakeTaskItems();
        using var calendar = new TasksCalendarTasks(items);

        Assert.NotNull(await calendar.SetPlannedHoursAsync(Guid.NewGuid(), Monday, 2m, Ct));
        Assert.Empty(items.HoursWrites);
    }

    /// <summary>Two quick edits on one task — Monday, then Tuesday — each load and save the
    /// whole list of blocks; the second must see the first, or it saves Monday away.</summary>
    [Fact]
    public async Task Two_hours_edits_in_quick_succession_keep_both_days()
    {
        var task = Entry("Refactor sync");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var items = new FakeTaskItems(task) { ReadGate = gate.Task };
        using var calendar = new TasksCalendarTasks(items);

        var monday = calendar.SetPlannedHoursAsync(task.Id, Monday, 2m, Ct);
        var tuesday = calendar.SetPlannedHoursAsync(task.Id, Monday.AddDays(1), 3m, Ct);
        gate.SetResult();

        Assert.Null(await monday);
        Assert.Null(await tuesday);
        Assert.Equal(
            [new PlannedHoursDto(Monday, 2m), new PlannedHoursDto(Monday.AddDays(1), 3m)],
            items.HoursWrites[^1].Blocks);
    }

    // --- No Tasks -------------------------------------------------------------------------

    [Fact]
    public async Task Without_tasks_the_calendar_reads_off_and_every_write_is_refused()
    {
        ICalendarTasks calendar = new NoCalendarTasks();
        var said = 0;
        calendar.Changed += () => said++;

        Assert.Same(CalendarTasksDto.Off, await calendar.ReadAsync(TheWeek, Ct));
        Assert.NotNull(await calendar.SetDueDateAsync(Guid.NewGuid(), Monday, Ct));
        Assert.NotNull(await calendar.SetPlannedHoursAsync(Guid.NewGuid(), Monday, 2m, Ct));
        Assert.Equal(0, said);
    }

    // --- The registration -----------------------------------------------------------------

    [Fact]
    public void AddCalendarCrossContextAdapters_answers_the_calendars_tasks_over_the_tasks_port_one_per_scope()
    {
        var services = new ServiceCollection()
            .AddScoped<ITaskItems>(_ => new FakeTaskItems())
            .AddSingleton<ITaskChangeSignal, TaskChangeSignal>()
            .AddCalendarCrossContextAdapters()
            .AddCalendarCrossContextAdapters();
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ICalendarTasks));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var port = first.ServiceProvider.GetRequiredService<ICalendarTasks>();

        Assert.IsType<TasksCalendarTasks>(port);
        Assert.Same(port, first.ServiceProvider.GetRequiredService<ICalendarTasks>());
        Assert.NotSame(port, second.ServiceProvider.GetRequiredService<ICalendarTasks>());
    }

    [Fact]
    public async Task AddCalendarCrossContextAdapters_without_the_tasks_module_offers_the_calendar_no_tasks()
    {
        using var provider = new ServiceCollection()
            .AddCalendarCrossContextAdapters()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var port = scope.ServiceProvider.GetRequiredService<ICalendarTasks>();

        Assert.IsType<NoCalendarTasks>(port);
        Assert.False((await port.ReadAsync(TheWeek, Ct)).Available);
    }

    private static TaskItemDto Entry(
        string title,
        DateOnly? dueOn = null,
        EntryStatus status = EntryStatus.Ready,
        DateOnly? completedOn = null,
        Recurrence? recurrence = null,
        IReadOnlyList<string>? repoIds = null,
        IReadOnlyList<PlannedHoursDto>? hours = null) =>
        new(
            Guid.NewGuid(), title, string.Empty, EntryType.Task, Priority.Medium, status, null, [], 0, 0, 0, [],
            DueOn: dueOn,
            Recurrence: recurrence,
            CompletedOn: completedOn,
            RepoIds: repoIds,
            PlannedHours: hours);

    private sealed class FakeTaskStore : ITaskStore
    {
        public event Action? RootChanged;

        public string DefaultRootDirectory => "default";

        public string RootDirectory => "root";

        public bool IsDefaultRoot => true;

        public string DatabasePath => "backlog.db";

        public void Move() => RootChanged?.Invoke();

        public string? TryUseRoot(string? path) => null;

        public string? ResetToDefault() => null;
    }

    /// <summary>The Tasks port as the adapter sees it: a list to read, and the two writes it
    /// makes, recorded. A refusal, when set, answers both writes. A saved list of blocks is
    /// kept, so the next read sees it; a read gate, when set, holds every read until it opens.</summary>
    // Differs from RecordingTaskItems: holds a task list, records the due and planned-hours writes, and can refuse them or gate every read.
    private sealed class FakeTaskItems(params TaskItemDto[] tasks) : ITaskItems
    {
        private readonly List<TaskItemDto> _tasks = [.. tasks];

        public Error? Refusal { get; init; }

        public Task? ReadGate { get; init; }

        public List<(Guid Id, DateOnly? DueOn)> DueWrites { get; } = [];

        public List<(Guid Id, IReadOnlyList<PlannedHoursDto> Blocks)> HoursWrites { get; } = [];

        public async Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default)
        {
            if (ReadGate is { } gate) await gate;
            lock (_tasks) return [.. _tasks];
        }

        public Task<Result<TaskItemDto>> SetDueAsync(Guid id, DateOnly? dueOn, CancellationToken cancellationToken = default)
        {
            DueWrites.Add((id, dueOn));
            return Answer(id);
        }

        public Task<Result<TaskItemDto>> SetPlannedHoursAsync(Guid id, IReadOnlyList<PlannedHoursDto> blocks, CancellationToken cancellationToken = default)
        {
            lock (_tasks)
            {
                HoursWrites.Add((id, blocks));
                if (Refusal is null && _tasks.FindIndex(task => task.Id == id) is var at and >= 0)
                    _tasks[at] = _tasks[at] with { PlannedHours = blocks };
            }

            return Answer(id);
        }

        private Task<Result<TaskItemDto>> Answer(Guid id)
        {
            lock (_tasks)
            {
                return Task.FromResult<Result<TaskItemDto>>(
                    Refusal is { } refusal ? (Result<TaskItemDto>)refusal : (_tasks.FirstOrDefault(task => task.Id == id) ?? Entry("Answered")));
            }
        }

        public Task<Result<SavedTaskDto>> SaveFromTextAsync(Guid? id, string rawText, int order, string? sourceInboxId = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<TaskItemDto>> LinkToIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<TaskItemDto>> UnlinkFromIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<TaskItemDto>> SetDevbookReferencesAsync(Guid id, IReadOnlyList<string> references, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<int>> RefileRepositoryTasksAsync(string repositoryId, string? moveToId, bool drop, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<ImportPlanResultDto>> ImportPlanAsync(
            string rawText,
            string? defaultRepo = null,
            IReadOnlyDictionary<string, string>? repoMatches = null,
            string? sourceInboxId = null,
            IReadOnlyDictionary<string, string>? sourceInboxIds = null,
            bool layOutOnRoadmap = false,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
