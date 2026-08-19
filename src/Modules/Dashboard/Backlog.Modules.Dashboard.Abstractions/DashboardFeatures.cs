namespace Backlog.Modules.Dashboard.Abstractions;

/// <summary>
/// The feature keys the Dashboard context owns.
/// <para>
/// The key itself is unchanged — <c>"dashboard"</c> — and that is deliberate.
/// It used to live on <c>MonitoringFeatures</c> because the dashboard was a
/// derived view inside Monitoring with no module behind it. It has a module now,
/// so the key moved to the context that owns it, exactly as that type's own
/// remark said it should. Keeping the string means nobody's settings file
/// forgets that they had switched the dashboard off.
/// </para>
/// </summary>
public static class DashboardFeatures
{
    /// <summary>Open the dashboard from the app chrome.</summary>
    public const string Dashboard = "dashboard";
}
