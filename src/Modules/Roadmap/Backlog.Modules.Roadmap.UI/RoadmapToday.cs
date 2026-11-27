namespace Backlog.Modules.Roadmap.UI;

/// <summary>
/// The date the roadmap's components treat as today when their host passes none: the
/// host's <see cref="TimeProvider"/> (app-defaults ADR 0005), so a test pins it with a
/// <c>FakeTimeProvider</c>, or the system clock where none is registered, as in Storybook.
/// </summary>
internal static class RoadmapToday
{
    public static DateOnly From(IServiceProvider services) =>
        DateOnly.FromDateTime((services.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System).GetLocalNow().DateTime);
}
