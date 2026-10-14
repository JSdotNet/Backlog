using System.Text.RegularExpressions;

namespace Backlog.Modules.Inbox.Services;

/// <summary>
/// The backlog's tags as <see cref="InboxClassifier"/> matches them: each one
/// trimmed of spaces and a leading <c>#</c>, kept only when it opens with a
/// letter and is longer than one character, listed once whatever its casing,
/// and in the order the backlog gave them.
/// <para>
/// Prepared once for a batch of items rather than once per item, because the
/// pattern each tag is matched with depends on the tag alone: a list of fifty
/// rows against thirty tags is thirty patterns built, not fifteen hundred.
/// </para>
/// </summary>
internal sealed class InboxTagVocabulary
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>One per known tag, in the same order; null for a tag with no
    /// word in it, which no text mentions.</summary>
    private readonly Regex?[] _patterns;

    private InboxTagVocabulary(IReadOnlyList<string> known, Regex?[] patterns)
    {
        Known = known;
        _patterns = patterns;
    }

    /// <summary>The tags in the backlog's spelling, in its order, each once.</summary>
    public IReadOnlyList<string> Known { get; }

    public static InboxTagVocabulary From(IReadOnlyList<string> backlogTags)
    {
        ArgumentNullException.ThrowIfNull(backlogTags);

        var known = backlogTags
            .Select(tag => (tag ?? string.Empty).Trim().TrimStart('#'))
            .Where(tag => tag.Length > 1 && char.IsLetter(tag[0]))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new InboxTagVocabulary(known, [.. known.Select(PatternFor)]);
    }

    /// <summary>Whether the text holds the known tag at <paramref name="index"/>
    /// as a whole word — or, for a tag of several words (<c>release-notes</c>),
    /// those words in order with any run of spaces, hyphens or underscores
    /// between them.</summary>
    public bool Mentions(int index, string text) => _patterns[index]?.IsMatch(text) ?? false;

    private static Regex? PatternFor(string tag)
    {
        var words = tag.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return null;

        var pattern = @"(?<![\p{L}\p{N}_])" + string.Join(@"[\s_-]+", words.Select(Regex.Escape)) + @"(?![\p{L}\p{N}_])";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
    }
}
