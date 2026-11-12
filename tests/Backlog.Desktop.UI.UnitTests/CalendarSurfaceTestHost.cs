using Backlog.Modules.Calendar.Extensions;
using Backlog.Modules.Calendar.UI;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Calendar surface over a <see cref="TasksPaneHost"/>, the way the shell composes it:
/// the Calendar module's Plans layer, the Tasks part it hosts, the window's navigator the
/// part jumps through, and the Calendar's view choices. Register before the first render,
/// because bUnit freezes the container then.
/// </summary>
internal static class CalendarSurfaceTestHost
{
    public static TasksPaneHost WithCalendarSurface(this TasksPaneHost host, ICalendarViewPreferences? preferences = null)
    {
        host.Context.Services.AddCalendarModule();
        host.Context.Services.AddTasksCalendarLayer();
        host.Context.Services.AddScoped<ISurfaceNavigator, SurfaceNavigator>();
        host.Context.Services.AddSingleton<ICalendarViewPreferences>(preferences ?? new FakeCalendarViewPreferences());
        return host;
    }

    public static IRenderedComponent<CalendarSurface> RenderCalendarSurface(this TasksPaneHost host) =>
        host.Context.Render<CalendarSurface>();
}
