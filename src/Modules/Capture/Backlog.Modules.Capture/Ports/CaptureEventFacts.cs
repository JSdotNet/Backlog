namespace Backlog.Modules.Capture.Ports;

/// <summary>
/// One end of an edition as the source wrote it: a date, and the time of day
/// and its UTC offset only when the source gave them — the <c>start</c> and
/// <c>end</c> of <c>.devbook/domain/capture/domain.md#event-facts</c>.
/// <para>
/// Not a <see cref="DateTimeOffset"/>, because a page that says "26 - 28 OCT
/// 2026" said nothing about a time or a zone, and inventing midnight UTC would
/// put the edition on the wrong day for half the world. schema.org's
/// <c>2026-11-27T16:30:00+01:00</c> is all three parts;
/// <c>2026-11-27T16:30</c> is a date and a time with no offset;
/// <c>2026-11-27</c> is the date alone.
/// </para>
/// </summary>
/// <param name="Date">The day, always known.</param>
/// <param name="Time">The time of day, or null when the source gave none.</param>
/// <param name="Offset">The UTC offset the time was written in, or null when
/// the source gave none — which it never does without a time.</param>
public sealed record CaptureEventDate(DateOnly Date, TimeOnly? Time = null, TimeSpan? Offset = null);

/// <summary>
/// What an event page capture read about the edition beyond its text
/// (<c>.devbook/domain/capture/domain.md#event-facts</c>): when, where, and the
/// watched page it was read from. Each part is optional — a page with no date
/// still arrives, with only its page — and equality is by value.
/// <para>
/// Carried on <see cref="CaptureFacts.Event"/> through the run untouched, like
/// the import's facts: the run decides none of it, and what the receiving side
/// keeps of it is the receiving side's business.
/// </para>
/// </summary>
/// <param name="Start">When the edition starts, or null when no date was found.</param>
/// <param name="End">When it ends, or null for a one-day edition the source
/// gave no end for, or for no date at all.</param>
/// <param name="Place">Where, as the page wrote it — a town, a venue, or
/// <c>Online</c> — or null when it said nothing.</param>
/// <param name="PageUrl">The watched page the edition was read from, which is
/// not always the entry's own link: a registration page one hop away is.</param>
/// <param name="PageTitle">That page's title.</param>
public sealed record CaptureEventFacts(
    CaptureEventDate? Start,
    CaptureEventDate? End,
    string? Place,
    string? PageUrl,
    string? PageTitle);
