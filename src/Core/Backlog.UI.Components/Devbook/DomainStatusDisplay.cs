namespace Backlog.UI.Components.Devbook;

/// <summary>
/// Whether the Domain view's pages show the status their chapters state — in a
/// unit's or a file's kicker, as the context's badge and its How settled bar, and
/// as a feature's Proposed or Deprecated mark. A host cascades it from the Devbook
/// status switch; with none cascaded, as in the storybook, the status shows.
/// </summary>
/// <param name="Shown">Whether the status is drawn.</param>
public sealed record DomainStatusDisplay(bool Shown)
{
    /// <summary>The status drawn.</summary>
    public static DomainStatusDisplay On { get; } = new(true);

    /// <summary>No status drawn.</summary>
    public static DomainStatusDisplay Off { get; } = new(false);

    /// <summary>Whether <paramref name="display"/> shows the status; no display
    /// cascaded shows it.</summary>
    public static bool IsShown(DomainStatusDisplay? display) => display?.Shown ?? true;
}
