namespace Backlog.Modules.Capture.Ports;

/// <summary>
/// One thing an adapter found at a source: a video on a channel, a post on a
/// page. What the adapter fetched and normalised, and nothing it decided — the
/// run decides what to do with it.
/// </summary>
/// <param name="ExternalId">The entry's own identity at the source — an Atom
/// <c>&lt;id&gt;</c>, an RSS <c>&lt;guid&gt;</c>, the link when the feed
/// offers nothing better. It is what keeps a re-run from adding the same entry
/// twice, so it has to be the one string the source keeps stable across
/// fetches, not the title.</param>
/// <param name="Title">What the reader sees on the Inbox row. An entry with no
/// title is one the run cannot make an item of, so an adapter may drop it or
/// hand it over and let the run drop it.</param>
/// <param name="Url">Where the entry lives, for the item's source link.</param>
/// <param name="BodyMd">Whatever the source offered beneath the title — a
/// description, a summary — or null when it offered nothing.</param>
/// <param name="PublishedAt">When the source says the entry appeared. Null when
/// the feed did not say, in which case the run stamps the capture with now.</param>
/// <param name="Facts">What the source said about the entry beyond its text.
/// Null for a feed, which says nothing of the kind; an import manifest says
/// all of it.</param>
public sealed record CapturedEntry(
    string ExternalId,
    string Title,
    string? Url,
    string? BodyMd,
    DateTimeOffset? PublishedAt,
    CaptureFacts? Facts = null);

/// <summary>
/// The capture facts an import manifest carries beside an item's text (local
/// ADR 0017): what it is, how it was labelled, who shared it, and where it is
/// to be filed. Carried through the run untouched — the run decides none of
/// them, and the receiving side is the one that knows what a content kind or a
/// list is.
/// </summary>
/// <param name="ContentKind">The content kind the source stated, as its slug.
/// Null to let the receiving side read the kind off the capture itself.</param>
/// <param name="Tags">Tag names, bare. Empty for none.</param>
/// <param name="Person">Who shared it, bare or with its <c>@</c>, or null.</param>
/// <param name="List">The name of the list to file it in, or null for
/// unfiled. A name, not an id: the source has never seen the receiving side's
/// lists, and one it names that does not exist lands the item unfiled.</param>
public sealed record CaptureFacts(
    string? ContentKind,
    IReadOnlyList<string> Tags,
    string? Person,
    string? List);
