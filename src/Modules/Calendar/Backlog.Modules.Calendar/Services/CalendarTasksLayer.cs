using Backlog.Modules.Calendar.Abstractions.DataTransferObjects;
using Backlog.Modules.Calendar.Abstractions.Services;

namespace Backlog.Modules.Calendar.Services;

/// <summary>
/// The Tasks layer's read policy (local ADR 0056 §2, after local ADR 0055 §3): the last
/// read of <see cref="ICalendarTasks"/>, and when the next one is owed.
/// <para>
/// A read lists the backlog, so it is never taken on every render: only on arriving at the
/// Calendar, after the port said a task was written or the store moved, on a new day, and
/// when the grid shows a day the last read did not cover — paging to another month. A week
/// inside the month last read is already known.
/// </para>
/// <para>
/// Each arrival is Off until its own read lands, as the Events layer is: the layer lives
/// as long as the window, so a Calendar it arrives at again must not offer the box from
/// the read it left with. Without a port — a host composed without Tasks — nothing is
/// offered or read, and every write is refused.
/// </para>
/// </summary>
internal sealed class CalendarTasksLayer : ICalendarTasksLayer, IDisposable
{
    private const string NoTasks = "There are no tasks on this Calendar.";

    private readonly ICalendarTasks? _tasks;
    private bool _owed = true;
    private DateOnly? _readOn;
    private CalendarWindow? _readWindow;
    private int _reads;

    public CalendarTasksLayer(ICalendarTasks? tasks)
    {
        _tasks = tasks;
        if (_tasks is not null) _tasks.Changed += OnOwed;
    }

    public event Action? Changed;

    public bool Offered => _tasks is not null;

    public CalendarTasksDto Tasks { get; private set; } = CalendarTasksDto.Off;

    public bool Available => Offered && Tasks.Available;

    public void Arrive()
    {
        _owed = true;
        Tasks = CalendarTasksDto.Off;

        // A read still out from the last visit is not this arrival's own.
        _reads++;
    }

    public Task ReadIfOwedAsync(DateOnly today, CalendarWindow window, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);

        var known = !_owed && _readOn == today && _readWindow is { } read && read.Covers(window);
        return _tasks is null || known ? Task.CompletedTask : ReadAsync(today, window, cancellationToken);
    }

    public async Task ReadAsync(DateOnly today, CalendarWindow window, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_tasks is null) return;

        // Settled before the await, so a second caller arriving while this read is out
        // does not read again. Each read is numbered: paging on while one is out starts
        // another, and only the latest may say what the grid's window holds.
        _owed = false;
        _readOn = today;
        _readWindow = window;
        var read = ++_reads;

        try
        {
            var tasks = await _tasks.ReadAsync(window, cancellationToken);
            if (read == _reads) Tasks = tasks;
        }
        catch (OperationCanceledException)
        {
            // Nothing landed for this window, so it is still owed.
            if (read == _reads) _owed = true;
            throw;
        }
        catch (Exception)
        {
            // A layer that cannot be read leaves the others: the chips go missing, not
            // the grid. Empty still says the tasks are there, so the box stays.
            if (read == _reads) Tasks = CalendarTasksDto.Empty;
        }
    }

    public Task<string?> SetDueDateAsync(Guid taskId, DateOnly dueOn, CancellationToken cancellationToken = default) =>
        _tasks?.SetDueDateAsync(taskId, dueOn, cancellationToken) ?? Task.FromResult<string?>(NoTasks);

    public Task<string?> SetPlannedHoursAsync(Guid taskId, DateOnly on, decimal? hours, CancellationToken cancellationToken = default) =>
        _tasks?.SetPlannedHoursAsync(taskId, on, hours, cancellationToken) ?? Task.FromResult<string?>(NoTasks);

    private void OnOwed()
    {
        _owed = true;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_tasks is not null) _tasks.Changed -= OnOwed;
    }
}
