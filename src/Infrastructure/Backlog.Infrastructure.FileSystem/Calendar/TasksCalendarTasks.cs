using Backlog.Modules.Calendar.Abstractions.DataTransferObjects;
using Backlog.Modules.Calendar.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Calendar;

/// <summary>
/// ADAPTER — answers the Calendar's <see cref="ICalendarTasks"/> (local ADR 0056 §2) from
/// Tasks' published port, <see cref="ITaskItems"/>, the way <see cref="RoadmapCalendarPlans"/>
/// answers the plans from Roadmap's.
/// <para>
/// It reads the whole list and keeps the window — acceptable at today's backlog sizes: the
/// live, unarchived tasks due on a day of the window, and every block of planned hours on
/// such a day from any of them, dated or not, since an undated task's hours still draw.
/// Which tasks the repository scope keeps is the window's, not this adapter's.
/// </para>
/// <para>
/// Writes go through the module's port rather than the pane's self-attributed one, so the
/// repository raises the task signal and the Tasks pane reloads; the Calendar hears the
/// same signal and reads again. A pane and a Calendar write racing on one entry are not
/// gated against each other — a known, low-risk gap.
/// </para>
/// </summary>
public sealed class TasksCalendarTasks : ICalendarTasks, IDisposable
{
    private const string Gone = "That task no longer exists.";

    private readonly ITaskItems _items;
    private readonly ITaskChangeSignal? _signal;
    private readonly ITaskStore? _store;
    private readonly SemaphoreSlim _hoursWrites = new(1, 1);

    public TasksCalendarTasks(ITaskItems items, ITaskChangeSignal? signal = null, ITaskStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items = items;
        _signal = signal;
        _store = store;
        if (_signal is not null) _signal.Changed += OnChanged;
        if (_store is not null) _store.RootChanged += OnChanged;
    }

    public event Action? Changed;

    public async Task<CalendarTasksDto> ReadAsync(CalendarWindow window, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);

        var live = (await _items.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(task => task.Status != EntryStatus.Archived)
            .ToList();

        return new CalendarTasksDto(
            [
                .. live
                    .Where(task => task.DueOn is { } due && window.Contains(due))
                    .Select(task => new CalendarTaskDto(
                        task.Id,
                        task.Title,
                        task.DueOn!.Value,
                        StatusOf(task),
                        Repeats: task.Recurrence is not null,
                        task.RepoIds ?? []))
            ],
            [
                .. live.SelectMany(task => task.PlannedHours
                    .Where(block => window.Contains(block.On))
                    .Select(block => new CalendarHoursBlockDto(task.Id, task.Title, block.On, block.Hours, task.RepoIds ?? [])))
            ]);
    }

    public async Task<string?> SetDueDateAsync(Guid taskId, DateOnly dueOn, CancellationToken cancellationToken = default)
    {
        var saved = await _items.SetDueAsync(taskId, dueOn, cancellationToken).ConfigureAwait(false);
        return saved.IsFailure ? saved.Error.Message : null;
    }

    /// <summary>Merges the one day into the task's blocks by <see cref="PlannedHoursMerge"/>,
    /// the rule the Tasks pane writes by, and writes nothing when the day already reads so.
    /// <para>
    /// A day edit loads and saves the whole list of blocks, so two in flight — Monday, then
    /// Tuesday, in quick succession — would each save the other's day as it was before.
    /// They take turns: the adapter is one per window, as the Calendar's edits are.
    /// </para></summary>
    public async Task<string?> SetPlannedHoursAsync(Guid taskId, DateOnly on, decimal? hours, CancellationToken cancellationToken = default)
    {
        await _hoursWrites.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await _items.GetAsync(taskId, cancellationToken).ConfigureAwait(false) is not { } task) return Gone;
            if (PlannedHoursMerge.WithDay(task.PlannedHours, on, hours) is not { } next) return null;

            var saved = await _items.SetPlannedHoursAsync(taskId, next, cancellationToken).ConfigureAwait(false);
            return saved.IsFailure ? saved.Error.Message : null;
        }
        finally
        {
            _hoursWrites.Release();
        }
    }

    private static CalendarTaskStatus StatusOf(TaskItemDto task) =>
        task.CompletedOn is not null
            ? CalendarTaskStatus.Done
            : task.Status switch
            {
                EntryStatus.Ready => CalendarTaskStatus.Ready,
                EntryStatus.InProgress => CalendarTaskStatus.InProgress,
                EntryStatus.Done => CalendarTaskStatus.Done,
                _ => CalendarTaskStatus.Draft,
            };

    private void OnChanged() => Changed?.Invoke();

    public void Dispose()
    {
        if (_signal is not null) _signal.Changed -= OnChanged;
        if (_store is not null) _store.RootChanged -= OnChanged;
        // _hoursWrites is left undisposed: a write still holding it as the window closes
        // would throw from its Release, and a SemaphoreSlim whose AvailableWaitHandle is
        // never read holds nothing unmanaged.
    }
}
