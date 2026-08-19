namespace Backlog.Modules.Backlog.Abstractions.DataTransferObjects;

/// <summary>
/// What a save did: the entry as it now stands, and — when the save was the one
/// that completed a repeating occurrence — the id of the successor it created.
/// <para>
/// The successor is reported rather than left to be discovered. Completing a
/// recurring entry creates a second aggregate, so a caller showing a list has one
/// more entry than it knows about, and nothing in the saved entry says so:
/// <c>recurrence_source_id</c> is on the <em>new</em> entry, which is precisely
/// the one the caller has not got. The alternatives are all worse. Re-querying and
/// diffing counts is wrong the moment two saves interleave; polling for a
/// successor turns an ordinary edit into a second round trip; and having the host
/// work out for itself that a status went to done and a recurrence was set is the
/// spawn rule reimplemented in a screen, where it would drift from the one in the
/// use case that actually spawns.
/// </para>
/// <para>
/// Null is the ordinary answer. A save that changed a title, a save that completed
/// a non-repeating entry, and a save that re-saved an entry already finished all
/// report nothing here — the last of those deliberately, because a repeating entry
/// spawns once per occurrence rather than once per keystroke after it.
/// </para>
/// </summary>
/// <param name="Entry">The entry as it was written down.</param>
/// <param name="SpawnedOccurrenceId">The successor this save created, or null.</param>
public sealed record SavedEntryDto(BacklogEntryDto Entry, Guid? SpawnedOccurrenceId = null);
