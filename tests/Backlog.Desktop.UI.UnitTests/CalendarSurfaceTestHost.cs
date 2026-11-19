using Backlog.Modules.Calendar.Abstractions.Services;
using Backlog.Modules.Calendar.Extensions;
using Backlog.Modules.Calendar.UI;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Calendar surface over a <see cref="TasksPaneHost"/>, the way the shell composes it
/// (local ADR 0056): the Calendar module's layers, the Tasks port they read — a
/// <see cref="FakeCalendarTasks"/> with no tasks unless the test registered its own — the
/// window's scope — a <see cref="FakeCalendarWindowScope"/> taking every task unless the
/// test registered its own — the window's navigator a chip opens Tasks through, and the
/// Calendar's view choices. Register before the first render, because bUnit freezes the
/// container then.
/// </summary>
internal static class CalendarSurfaceTestHost
{
    public static TasksPaneHost WithCalendarSurface(this TasksPaneHost host, ICalendarViewPreferences? preferences = null)
    {
        host.Context.Services.AddCalendarModule();
        host.Context.Services.TryAddSingleton<ICalendarTasks>(new FakeCalendarTasks());
        host.Context.Services.TryAddSingleton<ICalendarWindowScope>(new FakeCalendarWindowScope());
        host.Context.Services.TryAddScoped<ISurfaceNavigator, SurfaceNavigator>();
        host.Context.Services.AddSingleton<ICalendarViewPreferences>(preferences ?? new FakeCalendarViewPreferences());
        return host;
    }

    public static IRenderedComponent<CalendarSurface> RenderCalendarSurface(this TasksPaneHost host) =>
        host.Context.Render<CalendarSurface>();
}
