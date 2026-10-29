namespace Backlog.Modules.Progress.UI.Goals;

/// <summary>
/// The Goals view's id in the surface catalog (local ADR 0041 §1), for the shell to
/// name the view by without naming its component. It accepts no query keys.
/// </summary>
public static class GoalsSurfaceQuery
{
    /// <summary>The Goals view's surface id.</summary>
    public const string SurfaceId = "goals";
}
