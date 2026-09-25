using Backlog.UI.Components.Devbook;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// A <c>.domain</c> chapter's decision and review state, as the short text the
/// domain panel draws beside its status.
///
/// <para>The nine fields contract 16 calls state rather than content —
/// <see cref="DevbookSchema.StateFields"/> — never render as rows in the body. What
/// they say still has to reach the reader, and it belongs where the status is,
/// because every one of them qualifies it: who approved it and when, who accepted
/// the built work, and whose move it is on the way there. So they are said once,
/// compactly, beside the status: <c>approved by jobsc · 2026-09-02</c>,
/// <c>review: requested · reviewer ann</c>.</para>
///
/// <para>The <c>-hash</c> fields are left out. A content fingerprint is what makes
/// a lapsed approval a check result; to a reader it is forty characters of
/// nothing, and the check is what reports the lapse.</para>
/// </summary>
internal static class DomainDevbookDecisionState
{
    /// <summary>The parts to show, approval first, then acceptance, then review —
    /// the order the rungs are climbed in, with the review triad last because it is
    /// what is still outstanding. Empty when the chapter states none of them.</summary>
    public static IReadOnlyList<string> Describe(IReadOnlyDictionary<string, string> metadata)
    {
        var parts = new List<string>();

        if (Record(metadata, "approved", "approved-by", "approved-at") is { } approval) parts.Add(approval);
        if (Record(metadata, "accepted", "accepted-by", "accepted-at") is { } acceptance) parts.Add(acceptance);

        var review = Value(metadata, "review");
        var reviewer = Value(metadata, "reviewer");
        var reviewAt = Value(metadata, "review-at");
        if (review is not null || reviewer is not null || reviewAt is not null)
        {
            parts.Add(Joined(
                $"review: {review ?? "unstated"}",
                reviewer is null ? null : $"reviewer {reviewer}",
                reviewAt));
        }

        return parts;
    }

    private static string? Record(IReadOnlyDictionary<string, string> metadata, string rung, string byField, string atField)
    {
        var by = Value(metadata, byField);
        var at = Value(metadata, atField);
        if (by is null && at is null) return null;

        return Joined(by is null ? rung : $"{rung} by {by}", at);
    }

    private static string Joined(params string?[] parts) => string.Join(" · ", parts.Where(part => part is not null));

    private static string? Value(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}
