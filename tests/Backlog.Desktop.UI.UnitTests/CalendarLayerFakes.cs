using Backlog.Modules.Calendar.Abstractions.DataTransferObjects;
using Backlog.Modules.Calendar.Abstractions.Services;
using Backlog.Modules.Calendar.UI;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.UI.Components.Tasks;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>A roadmap with one plan through today, a milestone today, and one plan on the
/// shelf that a start moves onto the plan — what the Calendar's Plans port answers.</summary>
internal sealed class FakeCalendarPlans(DateOnly today) : ICalendarPlans
{
    public static readonly Guid PlanId = Guid.Parse("6fc104a3-c9cb-47c7-96a8-64cf629121f7");

    public static readonly Guid StartedId = Guid.Parse("5122f5d7-743a-4deb-a9c3-77cbf0cac5b8");

    private bool _started;

    public event Action? Changed;

    public int Reads { get; private set; }

    public string? Refusal { get; init; }

    /// <summary>When given, every read throws it — as a plan that cannot be read does.</summary>
    public Exception? Failure { get; init; }

    public List<(string Tag, DateOnly Start)> Started { get; } = [];

    public void RaiseChanged() => Changed?.Invoke();

    public Task<CalendarPlansDto> ReadAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        Reads++;
        if (Failure is not null) return Task.FromException<CalendarPlansDto>(Failure);

        List<CalendarPlanWindowDto> windows =
            [new(PlanId, "Calendar plans", "task-views", today.AddDays(-1), today.AddDays(1), ["backlog"], 3, 8)];
        List<CalendarShelfPlanDto> shelf = [];

        if (_started) windows.Add(new(StartedId, "Release q4", "release-q4", Started[0].Start, Started[0].Start, [], 0, 5));
        else shelf.Add(new("release-q4", "Release q4", [], 3, 5, 0));

        return Task.FromResult(new CalendarPlansDto(windows, [new(Guid.NewGuid(), "Release", today)], shelf));
    }

    public Task<string?> StartAsync(string tag, DateOnly start, CancellationToken cancellationToken = default)
    {
        Started.Add((tag, start));
        if (Refusal is not null) return Task.FromResult<string?>(Refusal);

        _started = true;
        Changed?.Invoke();
        return Task.FromResult<string?>(null);
    }
}

/// <summary>A Going event across yesterday to tomorrow, a Considering one today, and an
/// early-bird key date today. What the adapter answers: Spotted and Skipped never reach the
/// port, so the fake hands none in.</summary>
internal sealed class FakeCalendarEvents(DateOnly today, ShellNavigationStore? store = null) : ICalendarEvents
{
    private bool _shown;

    /// <summary>Off, unless ticked — or the store's answer when one is given, as the real
    /// adapter reads it.</summary>
    public bool Shown => store?.CalendarEventsShown ?? _shown;

    public bool Available { get; set; } = true;

    public bool Fails { get; init; }

    /// <summary>When given, a read does not answer until it is released — as a read over
    /// the database does not.</summary>
    public TaskCompletionSource? Gate { get; init; }

    public int Reads { get; private set; }

    public void SetShown(bool shown)
    {
        if (store is null) _shown = shown;
        else store.SetCalendarEventsShown(shown);
    }

    public async Task<CalendarEventsDto> ReadAsync(CancellationToken cancellationToken = default)
    {
        Reads++;
        if (Gate is not null) await Gate.Task;
        if (Fails) throw new IOException("The events could not be read.");
        if (!Available) return CalendarEventsDto.Off;

        return new CalendarEventsDto(
            [
                new("Techorama", "Going", "Antwerp", today.AddDays(-1), today.AddDays(1), Tentative: false),
                new("NDC Oslo", "Considering", "Online", today, today, Tentative: true)
            ],
            [new("Early bird ends", "Techorama", today)]);
    }
}

/// <summary>The working week at a fixed number of hours every day.</summary>
internal sealed class FakeCalendarCapacity(decimal hours) : ICalendarCapacity
{
    public event Action? Changed;

    public decimal Hours { get; set; } = hours;

    public decimal HoursOn(DateOnly day) => Hours;

    public void RaiseChanged() => Changed?.Invoke();
}

/// <summary>The Calendar's view choices, kept in memory. Show plans starts ticked, so a
/// test about the plans sees them drawn without ticking the box first.</summary>
internal sealed class FakeCalendarViewPreferences : ICalendarViewPreferences
{
    public CalendarRange Range { get; set; } = CalendarRange.Month;

    public bool PlansShown { get; set; } = true;

    public void SetRange(CalendarRange range) => Range = range;

    public void SetPlansShown(bool shown) => PlansShown = shown;
}

/// <summary>A shell navigation file path of its own, removed again afterwards.</summary>
internal sealed class TempShellNavigationFile : IDisposable
{
    private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "backlog-calendar-plans-tests", Guid.NewGuid().ToString("n"));

    public TempShellNavigationFile() => Directory.CreateDirectory(_directory);

    public string Path => System.IO.Path.Combine(_directory, "shell-navigation.json");

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }
}
