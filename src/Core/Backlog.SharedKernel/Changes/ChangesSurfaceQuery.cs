namespace Backlog.SharedKernel.Changes;

/// <summary>
/// The Changes view's address (local ADR 0043 §6): its surface id and the query key a
/// jump narrows it with. Here rather than beside the view, so a module that cannot
/// reference the shell — the Sessions pane's Changes action — can name it.
/// </summary>
public static class ChangesSurfaceQuery
{
    /// <summary>The view's id: what a route, a tool and the shell's navigation file
    /// name.</summary>
    public const string SurfaceId = "changes";

    /// <summary>The agent session whose change sets alone the view lists.</summary>
    public const string SessionKey = "session";

    /// <summary>Every key the view accepts.</summary>
    public static IReadOnlyList<string> Keys { get; } = [SessionKey];
}
