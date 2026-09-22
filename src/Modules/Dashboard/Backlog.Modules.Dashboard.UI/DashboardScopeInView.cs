using Backlog.Modules.Dashboard.Abstractions;

namespace Backlog.Modules.Dashboard.UI;

/// <summary>
/// What the dashboard is looking at right now, for whoever needs to know
/// without being the pane.
/// </summary>
/// <remarks>
/// <para>
/// The pane keeps its scope as a field on purpose — closing the surface
/// forgets it, and that is the whole reason the dashboard has no settings — and
/// that stays exactly as it was. This is a mirror the pane writes whenever its
/// scope moves, so <see cref="DashboardAiContentSource"/> can fetch for the
/// same repositories and the same window the parts on screen are showing. A
/// source that read the repository directory itself would answer about every
/// repository while the reader was looking at one.
/// </para>
/// <para>
/// Whatever is left here after the pane goes is read by nobody: the shell
/// offers the Dashboard as an Ask AI scope only while the surface is up.
/// </para>
/// </remarks>
public sealed class DashboardScopeInView
{
    /// <summary>The scope the parts were last given. The dashboard's default
    /// until a pane has rendered.</summary>
    public DashboardScope Current { get; private set; } = DashboardScope.Default;

    /// <summary>The pane's scope moved. Called by the pane and nothing else.</summary>
    public void Set(DashboardScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        Current = scope;
    }
}
