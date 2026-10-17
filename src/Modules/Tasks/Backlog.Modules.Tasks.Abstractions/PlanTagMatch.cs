namespace Backlog.Modules.Tasks.Abstractions;

/// <summary>
/// Whether a task's stored tag carries a roadmap item's slug — the tag half of
/// Roadmap Item Gathering (<c>.devbook/domain/roadmap/domain.md#roadmap-item-gathering</c>).
/// <para>
/// One rule with two readers. The roadmap's rollup counts an item's work by it, and the
/// Tasks list's "Open in Tasks" lists that work by it; while each kept its own copy, the
/// roadmap counted entries the list could not find, and the list under a bar read
/// "Nothing matches these filters" beside a bar that said it had tasks.
/// </para>
/// <para>
/// A plan tag is stored as <c>+slug</c>, and an entry filed before the sigil existed
/// carries the bare <c>slug</c>; both carry it, or every entry already on disk would drop
/// off its item the day the sigil arrived (<c>.devbook/arc42/adr/0013</c>). What must not carry it is a person:
/// <c>@release-q4</c> names somebody, and only one leading <c>+</c> is lifted before the
/// compare, so it stays a mismatch.
/// </para>
/// <para>
/// In Abstractions because both readers can reach it there: the rollup builder through
/// the Tasks module it already reads entries from, and the Tasks list directly. The
/// Roadmap module references no Tasks project, and does not need to — it holds the slug,
/// not the entries.
/// </para>
/// </summary>
public static class PlanTagMatch
{
    /// <summary>Whether <paramref name="tag"/>, as an entry stores it, names the item
    /// whose bare slug is <paramref name="slug"/>: <c>+slug</c> or <c>slug</c>, in any
    /// case. Nothing carries a blank slug, and a blank tag carries nothing.</summary>
    public static bool Carries(string? tag, string? slug)
    {
        if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(slug)) return false;

        var bare = tag.StartsWith('+') ? tag[1..] : tag;

        return string.Equals(bare, slug, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every slug <paramref name="tags"/>, as an entry stores them, carry by
    /// <see cref="Carries"/>: each tag less one leading <c>+</c>, each once in any case, in
    /// the order the entry lists them. A person tag carries none — no item's slug starts
    /// with <c>@</c> — so <c>Carries(tag, slug)</c> holds exactly when the slug is here.
    /// <para>
    /// The third reader of the rule: a goal's attached plan counts what the roadmap item
    /// of that tag gathers (<c>.devbook/domain/progress/domain.md#goal-attachment</c>).
    /// Progress may not reach Tasks, so whoever hands Progress its tasks — the desktop's
    /// task source, the Goals view's candidates, the phone's task fold — names the slugs
    /// with this, and Progress compares slugs alone. While it read only <c>+slug</c>, a
    /// goal counted <c>0/0</c> for an item whose bar listed an entry tagged <c>#slug</c>.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> SlugsCarried(IEnumerable<string?> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        return
        [
            .. tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag) && !tag.StartsWith('@'))
                .Select(tag => tag!.StartsWith('+') ? tag[1..] : tag!)
                .Where(slug => !string.IsNullOrWhiteSpace(slug))
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];
    }
}
