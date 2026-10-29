namespace Backlog.Modules.Dashboard.UI;

/// <summary>
/// What the Dashboard surface reads from the window it is shown in (local ADR 0040 §1,
/// §5): the repositories in the window's scope and the hue each wears, and the main
/// window's Ask AI.
/// <para>
/// One per window, with the window's view state. The host fills the scope from the
/// window's own state; the main window's shell fills <see cref="AskAi"/>, because Ask
/// AI stays in the main window only (§4). A window whose host fills nothing shows every
/// repository, without hues and without Ask AI on its parts.
/// </para>
/// </summary>
public sealed class DashboardSurfaceWiring
{
    /// <summary>The repositories in the window's scope, by alias; empty for all.</summary>
    public Func<IReadOnlyList<string>>? RepositoryAliases { get; set; }

    /// <summary>Which identity hue a repository alias wears, or null while hues are off.</summary>
    public Func<string?, int?>? RepositoryColour { get; set; }

    /// <summary>A part's Ask AI, with the part's title; null draws no Ask AI.</summary>
    public Func<string, Task>? AskAi { get; set; }
}
