namespace Backlog.Modules.Monitoring.UI;

/// <summary>
/// The feature keys Monitoring &amp; Dashboard owns.
/// <para>
/// These sit in the UI project rather than an <c>.Abstractions</c> one because
/// Monitoring has no domain module yet — there is a derived view and no signal
/// store behind it. Creating an abstractions project to hold one constant would
/// publish a contract for a module nobody has written; when the module arrives
/// and something below the shell wants to gate on this, the key moves with it.
/// </para>
/// </summary>
public static class MonitoringFeatures
{
    /// <summary>Open the dashboard from the app chrome.</summary>
    public const string Dashboard = "dashboard";
}
