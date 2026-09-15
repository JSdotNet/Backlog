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
public sealed record CapturedEntry(
    string ExternalId,
    string Title,
    string? Url,
    string? BodyMd,
    DateTimeOffset? PublishedAt);
