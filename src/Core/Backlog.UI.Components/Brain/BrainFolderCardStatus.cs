namespace Backlog.UI.Components.Brain;

/// <summary>
/// What the Brain card says about the folder it is set to, handed in by the host
/// that looked: which sync product carries it, and whether its two convention
/// files are there.
/// </summary>
/// <param name="SyncedBy">The sync product the folder sits in, by the name a person
/// would recognise — "OneDrive" — or null when it sits in none the host
/// recognised.</param>
/// <param name="HasReadme">Whether <c>README.md</c> is in the folder.</param>
/// <param name="HasConventions">Whether <c>CONVENTIONS.md</c> is in the folder.</param>
public sealed record BrainFolderCardStatus(string? SyncedBy, bool HasReadme, bool HasConventions)
{
    /// <summary>Whether <b>Create conventions</b> still has something to write,
    /// which is what offers the button.</summary>
    public bool NeedsConventions => !HasReadme || !HasConventions;
}
