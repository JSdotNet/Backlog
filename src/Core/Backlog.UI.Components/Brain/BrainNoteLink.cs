namespace Backlog.UI.Components.Brain;

/// <summary>
/// One Brain note as a task row's <see cref="BrainNotesBadge"/> lists it: the note's
/// path from the Brain's root, which is what opening it asks for, and its title.
/// Handed in by the host, which read it from the owner's own Brain.
/// </summary>
public sealed record BrainNoteLink(string Path, string Title);
