namespace Backlog.Modules.Capture.Ports;

/// <summary>
/// What an adapter has to say after looking at one source's targets: the
/// entries it found, and the remarks the reader should see beside them.
/// </summary>
/// <param name="Entries">Every entry currently at every target, new or not.
/// The adapter does not know which ones the Inbox already holds — that is the
/// run's question, answered by id — so it hands over all of them.</param>
/// <param name="Notes">Per-target remarks worth a line on screen: a page with
/// no feed, a channel whose id could not be found, a fetch that timed out. A
/// target that went fine says nothing. Kept apart from the entries so a target
/// that failed is reported without hiding the ones that did not.</param>
public sealed record CaptureSourceFindings(IReadOnlyList<CapturedEntry> Entries, IReadOnlyList<string> Notes)
{
    /// <summary>Nothing found and nothing to say.</summary>
    public static CaptureSourceFindings Empty { get; } = new([], []);
}
