using Backlog.Modules.Calendar.Abstractions.DataTransferObjects;
using Backlog.Modules.Calendar.Abstractions.Services;
using Backlog.Modules.Calendar.Extensions;
using Backlog.Modules.Calendar.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Calendar.UnitTests;

/// <summary>
/// The Tasks layer's read policy (local ADR 0056 §2, after local ADR 0055 §3): the Calendar
/// reads the tasks in its window only when a read is owed — on arriving, after a task was
/// written, on a new day, and when the grid shows days the last read did not cover — and
/// fails closed: no Show tasks box before the first read lands, and a read that fails
/// leaves the plans and events on the Calendar with no chips.
/// </summary>
public sealed class CalendarTasksLayerTests
{
    private static readonly DateOnly Today = new(2026, 11, 18);
    private static readonly CalendarWindow November = new(new DateOnly(2026, 10, 26), new DateOnly(2026, 12, 6));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_a_port_nothing_is_offered_read_or_written()
    {
        using var layer = new CalendarTasksLayer(null);

        Assert.False(layer.Offered);
        Assert.False(layer.Available);
        await layer.ReadIfOwedAsync(Today, November, Ct);
        await layer.ReadAsync(Today, November, Ct);
        Assert.Same(CalendarTasksDto.Off, layer.Tasks);
        Assert.NotNull(await layer.SetDueDateAsync(Guid.NewGuid(), Today, Ct));
        Assert.NotNull(await layer.SetPlannedHoursAsync(Guid.NewGuid(), Today, 2m, Ct));
    }

    [Fact]
    public void Before_the_first_read_the_layer_is_off()
    {
        using var layer = new CalendarTasksLayer(new FakeTasks());

        Assert.True(layer.Offered);
        Assert.Same(CalendarTasksDto.Off, layer.Tasks);
        Assert.False(layer.Available);
    }

    [Fact]
    public async Task The_first_read_is_owed_and_the_same_day_and_window_read_once()
    {
        var port = new FakeTasks();
        using var layer = new CalendarTasksLayer(port);

        await layer.ReadIfOwedAsync(Today, November, Ct);
        await layer.ReadIfOwedAsync(Today, November, Ct);

        Assert.Equal(1, port.Reads);
        Assert.Equal("Refactor sync", Assert.Single(layer.Tasks.Tasks).Title);
        Assert.True(layer.Available);
    }

    [Fact]
    public async Task The_read_asks_for_the_window_the_grid_shows()
    {
        var port = new FakeTasks();
        using var layer = new CalendarTasksLayer(port);

        await layer.ReadIfOwedAsync(Today, November, Ct);

        Assert.Equal(November, port.LastWindow);
    }

    [Fact]
    public async Task A_new_day_reads_again()
    {
        var port = new FakeTasks();
        using var layer = new CalendarTasksLayer(port);
        await layer.ReadIfOwedAsync(Today, November, Ct);

        await layer.ReadIfOwedAsync(Today.AddDays(1), November, Ct);

        Assert.Equal(2, port.Reads);
    }

    /// <summary>Paging to the next month shows days the last read never asked about.</summary>
    [Fact]
    public async Task A_window_with_days_the_last_read_did_not_cover_reads_again()
    {
        var port = new FakeTasks();
        using var layer = new CalendarTasksLayer(port);
        await layer.ReadIfOwedAsync(Today, November, Ct);

        var december = new CalendarWindow(new DateOnly(2026, 11, 30), new DateOnly(2027, 1, 10));
        await layer.ReadIfOwedAsync(Today, december, Ct);

        Assert.Equal(2, port.Reads);
        Assert.Equal(december, port.LastWindow);
    }

    /// <summary>A week inside the month just read is already known.</summary>
    [Fact]
    public async Task A_window_inside_the_last_read_reads_nothing()
    {
        var port = new FakeTasks();
        using var layer = new CalendarTasksLayer(port);
        await layer.ReadIfOwedAsync(Today, November, Ct);

        await layer.ReadIfOwedAsync(Today, new CalendarWindow(new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 22)), Ct);

        Assert.Equal(1, port.Reads);
    }

    [Fact]
    public async Task Arriving_offers_no_tasks_until_its_own_read_lands()
    {
        var port = new FakeTasks();
        using var layer = new CalendarTasksLayer(port);
        await layer.ReadIfOwedAsync(Today, November, Ct);
        Assert.True(layer.Available);

        layer.Arrive();

        Assert.Same(CalendarTasksDto.Off, layer.Tasks);
        Assert.False(layer.Available);
        await layer.ReadIfOwedAsync(Today, November, Ct);
        Assert.Equal(2, port.Reads);
        Assert.True(layer.Available);
    }

    /// <summary>A task written anywhere — the Tasks pane, the MCP server, this Calendar —
    /// owes a read, and the layer says so.</summary>
    [Fact]
    public async Task A_task_written_owes_a_read_and_says_so()
    {
        var port = new FakeTasks();
        using var layer = new CalendarTasksLayer(port);
        await layer.ReadIfOwedAsync(Today, November, Ct);
        var said = 0;
        layer.Changed += () => said++;

        port.RaiseChanged();

        Assert.Equal(1, said);
        await layer.ReadIfOwedAsync(Today, November, Ct);
        Assert.Equal(2, port.Reads);
    }

    [Fact]
    public async Task Reading_now_reads_whether_or_not_one_is_owed()
    {
        var port = new FakeTasks();
        using var layer = new CalendarTasksLayer(port);
        await layer.ReadIfOwedAsync(Today, November, Ct);

        await layer.ReadAsync(Today, November, Ct);

        Assert.Equal(2, port.Reads);
    }

    [Fact]
    public async Task With_no_tasks_to_read_the_layer_offers_no_tasks()
    {
        var port = new FakeTasks { Answer = CalendarTasksDto.Off };
        using var layer = new CalendarTasksLayer(port);

        await layer.ReadIfOwedAsync(Today, November, Ct);

        Assert.True(layer.Offered);
        Assert.False(layer.Available);
    }

    /// <summary>A layer that cannot be read leaves the others: the chips go missing, not the
    /// grid, and the box stays, since the tasks are there and only this read failed.</summary>
    [Fact]
    public async Task A_read_that_fails_leaves_the_tasks_empty_and_still_available()
    {
        var port = new FakeTasks();
        using var layer = new CalendarTasksLayer(port);
        await layer.ReadIfOwedAsync(Today, November, Ct);

        port.Failure = new IOException("The tasks could not be read.");
        await layer.ReadAsync(Today, November, Ct);

        Assert.Same(CalendarTasksDto.Empty, layer.Tasks);
        Assert.True(layer.Available);
    }

    [Fact]
    public async Task A_cancelled_read_is_not_swallowed()
    {
        var port = new FakeTasks { Failure = new OperationCanceledException() };
        using var layer = new CalendarTasksLayer(port);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => layer.ReadAsync(Today, November, Ct));
    }

    /// <summary>Paging on while the arrival read is still out: the month the grid shows is
    /// the one whose tasks it keeps, whichever read lands last.</summary>
    [Fact]
    public async Task A_read_that_lands_after_a_later_one_is_dropped()
    {
        var december = new CalendarWindow(new DateOnly(2026, 11, 30), new DateOnly(2027, 1, 10));
        var novemberAnswer = new TaskCompletionSource<CalendarTasksDto>();
        var decemberAnswer = new TaskCompletionSource<CalendarTasksDto>();
        var port = new FakeTasks { Reading = window => window == November ? novemberAnswer.Task : decemberAnswer.Task };
        using var layer = new CalendarTasksLayer(port);

        var first = layer.ReadIfOwedAsync(Today, November, Ct);
        var second = layer.ReadIfOwedAsync(Today, december, Ct);
        var inDecember = new CalendarTasksDto([new CalendarTaskDto(Guid.NewGuid(), "Release", new DateOnly(2026, 12, 4), CalendarTaskStatus.Ready, Repeats: false, [])], []);
        decemberAnswer.SetResult(inDecember);
        await second;
        novemberAnswer.SetResult(new CalendarTasksDto([], []));
        await first;

        Assert.Same(inDecember, layer.Tasks);
    }

    /// <summary>A read cancelled after it was asked for leaves its window unknown, so the
    /// next render reads it again instead of trusting tasks that never came.</summary>
    [Fact]
    public async Task A_cancelled_read_leaves_its_window_owed()
    {
        var port = new FakeTasks { Failure = new OperationCanceledException() };
        using var layer = new CalendarTasksLayer(port);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => layer.ReadIfOwedAsync(Today, November, Ct));

        port.Failure = null;
        await layer.ReadIfOwedAsync(Today, November, Ct);

        Assert.Equal(2, port.Reads);
        Assert.True(layer.Available);
    }

    [Fact]
    public async Task A_due_date_write_goes_through_the_port_and_answers_its_refusal()
    {
        var port = new FakeTasks { Refusal = "That task no longer exists." };
        using var layer = new CalendarTasksLayer(port);
        var id = Guid.NewGuid();

        var refusal = await layer.SetDueDateAsync(id, Today, Ct);

        Assert.Equal("That task no longer exists.", refusal);
        Assert.Equal($"due {id} {Today}", Assert.Single(port.Writes));
    }

    [Fact]
    public async Task An_hours_write_goes_through_the_port_and_answers_its_refusal()
    {
        var port = new FakeTasks();
        using var layer = new CalendarTasksLayer(port);
        var id = Guid.NewGuid();

        Assert.Null(await layer.SetPlannedHoursAsync(id, Today, 3m, Ct));
        port.Refusal = "No.";
        Assert.Equal("No.", await layer.SetPlannedHoursAsync(id, Today, null, Ct));

        Assert.Equal([$"hours {id} {Today} 3", $"hours {id} {Today} -"], port.Writes);
    }

    [Fact]
    public void Disposing_stops_listening()
    {
        var port = new FakeTasks();
        var layer = new CalendarTasksLayer(port);

        layer.Dispose();

        Assert.Equal(0, port.Listeners);
    }

    [Fact]
    public void AddCalendarModule_resolves_one_tasks_layer_per_scope_and_is_safe_to_call_twice()
    {
        var services = new ServiceCollection().AddCalendarModule();
        var once = services.Count;
        services.AddCalendarModule();
        Assert.Equal(once, services.Count);

        services.AddSingleton<ICalendarTasks, FakeTasks>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var layer = first.ServiceProvider.GetRequiredService<ICalendarTasksLayer>();

        Assert.True(layer.Offered);
        Assert.Same(layer, first.ServiceProvider.GetRequiredService<ICalendarTasksLayer>());
        Assert.NotSame(layer, second.ServiceProvider.GetRequiredService<ICalendarTasksLayer>());
    }

    [Fact]
    public void AddCalendarModule_without_a_port_resolves_a_tasks_layer_that_offers_no_tasks()
    {
        using var provider = new ServiceCollection().AddCalendarModule()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        Assert.False(scope.ServiceProvider.GetRequiredService<ICalendarTasksLayer>().Offered);
    }

    private sealed class FakeTasks : ICalendarTasks
    {
        public event Action? Changed;

        public int Reads { get; private set; }

        public CalendarWindow? LastWindow { get; private set; }

        public CalendarTasksDto Answer { get; set; } = new(
            [new CalendarTaskDto(Guid.NewGuid(), "Refactor sync", Today, CalendarTaskStatus.Ready, Repeats: false, [])],
            []);

        public Exception? Failure { get; set; }

        /// <summary>When set, answers each read in place of <see cref="Answer"/>.</summary>
        public Func<CalendarWindow, Task<CalendarTasksDto>>? Reading { get; init; }

        public string? Refusal { get; set; }

        public List<string> Writes { get; } = [];

        public int Listeners => Changed?.GetInvocationList().Length ?? 0;

        public void RaiseChanged() => Changed?.Invoke();

        public Task<CalendarTasksDto> ReadAsync(CalendarWindow window, CancellationToken cancellationToken = default)
        {
            Reads++;
            LastWindow = window;
            if (Failure is { } failure) return Task.FromException<CalendarTasksDto>(failure);
            return Reading is { } reading ? reading(window) : Task.FromResult(Answer);
        }

        public Task<string?> SetDueDateAsync(Guid taskId, DateOnly dueOn, CancellationToken cancellationToken = default)
        {
            Writes.Add($"due {taskId} {dueOn}");
            return Task.FromResult(Refusal);
        }

        public Task<string?> SetPlannedHoursAsync(Guid taskId, DateOnly on, decimal? hours, CancellationToken cancellationToken = default)
        {
            Writes.Add($"hours {taskId} {on} {(hours is { } figure ? figure.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")}");
            return Task.FromResult(Refusal);
        }
    }
}
