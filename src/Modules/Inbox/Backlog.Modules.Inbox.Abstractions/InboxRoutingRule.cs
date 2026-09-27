namespace Backlog.Modules.Inbox.Abstractions;

/// <summary>What part of an item a routing rule's pattern is read against.</summary>
public enum InboxRoutingRuleTarget
{
    /// <summary><c>#pattern</c>: any of the item's tags.</summary>
    Tag,

    /// <summary><c>@pattern</c>: the person who shared it.</summary>
    Person,

    /// <summary>A bare pattern: the channel it arrived through, or its source link.</summary>
    Source
}

/// <summary>
/// One line of the reader's routing rules: items that match
/// <see cref="Pattern"/> are suggested for <see cref="Repository"/>.
/// <para>
/// The pattern says what it is read against by its first character, the same
/// sigils a chip draws: <c>#design</c> is a tag, <c>@alice</c> the person who
/// shared the item, and anything else — <c>youtube</c>,
/// <c>*github.com/JSdotNet/*</c> — the channel or the source link. <c>*</c>
/// stands for any run of characters, and the match ignores case, so a rule is
/// written the way the reader would say it rather than the way a regex would.
/// </para>
/// <para>
/// A rule only ever suggests. The reader accepts the repository with one key or
/// turns it down, which is why a rule that matches too much costs a rejected
/// chip rather than a mis-filed entry.
/// </para>
/// </summary>
public sealed record InboxRoutingRule(string Pattern, string Repository)
{
    /// <summary>The separator between the pattern and the repository on a line.</summary>
    public const string Arrow = "=>";

    public InboxRoutingRuleTarget Target =>
        Pattern.StartsWith('#') ? InboxRoutingRuleTarget.Tag
        : Pattern.StartsWith('@') ? InboxRoutingRuleTarget.Person
        : InboxRoutingRuleTarget.Source;

    /// <summary>The pattern without the sigil that says what it matches.</summary>
    public string Glob => Target == InboxRoutingRuleTarget.Source ? Pattern : Pattern[1..];

    /// <summary>The rule as the reader writes it, one line.</summary>
    public override string ToString() => $"{Pattern} {Arrow} {Repository}";

    /// <summary>Whether <paramref name="value"/> matches <see cref="Glob"/>:
    /// the whole value, ignoring case, with <c>*</c> for any run of characters.</summary>
    public bool Matches(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        return GlobMatch(Glob, value.Trim());
    }

    /// <summary>
    /// Reads the rules out of the text the reader typed, one per line, and
    /// answers the rules or the first line it could not read. Blank lines are
    /// skipped. All or nothing, like any other settings field: a half-read set
    /// would quietly drop the line the reader thought they had added.
    /// </summary>
    public static (IReadOnlyList<InboxRoutingRule> Rules, string? Error) Parse(string? text)
    {
        var rules = new List<InboxRoutingRule>();
        var lines = (text ?? string.Empty).Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0) continue;

            var number = index + 1;
            var arrow = line.IndexOf(Arrow, StringComparison.Ordinal);
            if (arrow < 0)
                return ([], $"Line {number} has no \"{Arrow}\": write it as pattern {Arrow} owner/name.");

            var pattern = line[..arrow].Trim();
            var repository = line[(arrow + Arrow.Length)..].Trim();

            if (pattern.Length == 0 || pattern is "#" or "@")
                return ([], $"Line {number} has no pattern before \"{Arrow}\".");

            if (!IsRepositoryId(repository))
                return ([], $"Line {number} names \"{repository}\", which is not a repository written as owner/name.");

            var rule = new InboxRoutingRule(pattern, repository);
            if (!rules.Contains(rule)) rules.Add(rule);
        }

        return (rules, null);
    }

    /// <summary>The rules back as the text the reader edits, one per line.</summary>
    public static string Format(IEnumerable<InboxRoutingRule> rules) =>
        string.Join('\n', rules.Select(rule => rule.ToString()));

    private static bool IsRepositoryId(string value)
    {
        var parts = value.Split('/');
        return parts.Length == 2
            && parts.All(part => part.Length > 0 && part.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.'));
    }

    /// <summary>A glob with <c>*</c> only, matched iteratively so a long link
    /// against a pattern of many stars cannot run away.</summary>
    private static bool GlobMatch(string pattern, string value)
    {
        int p = 0, v = 0, star = -1, mark = 0;

        while (v < value.Length)
        {
            if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = v;
            }
            else if (p < pattern.Length && char.ToLowerInvariant(pattern[p]) == char.ToLowerInvariant(value[v]))
            {
                p++;
                v++;
            }
            else if (star >= 0)
            {
                p = star + 1;
                v = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*') p++;

        return p == pattern.Length;
    }
}
