namespace Backlog.UI.Components.Badges;

/// <summary>
/// Badge modifiers are class names, so anything that reaches one has to survive
/// a trip through CSS: lowercase, letters, digits and hyphens only. An empty
/// result would produce a dangling <c>badge--status-</c>, so callers pass a
/// fallback.
/// </summary>
internal static class BadgeSlug
{
    public static string Of(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;

        var slug = new string(value.Trim().ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch) || ch == '-').ToArray());

        return slug.Length == 0 ? fallback : slug;
    }
}
