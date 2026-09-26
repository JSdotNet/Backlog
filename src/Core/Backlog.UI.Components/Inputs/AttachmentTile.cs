namespace Backlog.UI.Components.Inputs;

/// <summary>
/// One file on an <see cref="AttachmentStrip"/>, already described: the strip
/// draws what it is handed and knows nothing of caps, allowlists or where the
/// bytes are.
/// </summary>
/// <param name="Key">Stable across renders — the attachment's id.</param>
/// <param name="TypeLabel">What it is, as a person reads it: "PDF", "JPEG image".</param>
/// <param name="SizeLabel">How big: "2.4 MB".</param>
/// <param name="IsPicture">Drawn with a picture glyph rather than a page.</param>
/// <param name="Refusal">Why it will not be sent, or null. A refused tile says
/// so in words, not only in colour.</param>
public sealed record AttachmentTile(
    string Key,
    string Name,
    string TypeLabel,
    string SizeLabel,
    bool IsPicture = false,
    string? Refusal = null);
