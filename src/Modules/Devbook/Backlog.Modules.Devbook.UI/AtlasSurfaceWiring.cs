namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// What the Lobes atlas surface reads from the window it is shown in (local ADR 0043
/// §1, §5; <c>.devbook/domain/brain/features.md#lobes-atlas</c>): the window's
/// repository scope, which the atlas draws, and the way to narrow it to one member.
/// <para>
/// One per window, with the window's view state. A window whose host fills nothing
/// draws everything, and its rows and card ask for no scope.
/// </para>
/// </summary>
public sealed class AtlasSurfaceWiring
{
    /// <summary>The window's repository scope, in its order, <c>BrainScope.Value</c>
    /// for the Brain; empty for every repository.</summary>
    public Func<IReadOnlyList<string>>? RepositoryScope { get; set; }

    /// <summary>Scopes the window to one member alone: an alias, or
    /// <c>BrainScope.Value</c> for the Brain.</summary>
    public Action<string>? ScopeTo { get; set; }
}
