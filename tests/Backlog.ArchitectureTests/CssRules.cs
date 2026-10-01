using System.Text;
using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The top-level style rules of a stylesheet, read just far enough to answer which
/// selectors are declared more than once and what one selector's own rules give
/// it.
///
/// <para>Top level means outside every at-rule. A rule inside <c>@media</c>,
/// <c>@supports</c> or <c>@container</c> applies under a condition, so the same
/// selector there and outside it are two scopes rather than one selector written
/// twice; <c>@keyframes</c> steps are not selectors at all. None of them is read
/// here.</para>
/// </summary>
internal static class CssRules
{
    /// <summary>One top-level rule: the line its block opens on, each selector in
    /// its list, and its declarations in source order.</summary>
    public sealed record Rule(int Line, IReadOnlyList<string> Selectors, IReadOnlyList<KeyValuePair<string, string>> Declarations);

    public static IReadOnlyList<Rule> TopLevel(string css)
    {
        var text = WithoutComments(css);
        var rules = new List<Rule>();
        var open = new Stack<(int Brace, string Prelude, bool TopLevel)>();
        var preludeStart = 0;
        var line = 1;
        var lineAt = new Dictionary<int, int>();

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '\n')
            {
                line++;
            }
            else if (c is '"' or '\'')
            {
                var close = text.IndexOf(c, i + 1);
                i = close < 0 ? text.Length - 1 : close;
            }
            else if (c == '{')
            {
                var prelude = Collapse(text[preludeStart..i]);
                open.Push((i, prelude, open.Count == 0 && !prelude.StartsWith('@')));
                lineAt[i] = line;
                preludeStart = i + 1;
            }
            else if (c == '}')
            {
                if (open.Count > 0)
                {
                    var block = open.Pop();
                    if (block.TopLevel)
                    {
                        rules.Add(new Rule(
                            lineAt[block.Brace],
                            Selectors(block.Prelude),
                            Declarations(text[(block.Brace + 1)..i])));
                    }
                }

                preludeStart = i + 1;
            }
            else if (c == ';')
            {
                preludeStart = i + 1;
            }
        }

        return rules;
    }

    /// <summary>Every selector that more than one top-level rule lists, with the
    /// lines of those rules. A selector shared through a grouped list counts as
    /// declared there: editing it means finding that list too.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<int>> Duplicates(string css) =>
        TopLevel(css)
            .SelectMany(rule => rule.Selectors.Distinct().Select(selector => (selector, rule.Line)))
            .GroupBy(entry => entry.selector, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<int>)group.Select(entry => entry.Line).ToList(),
                StringComparer.Ordinal);

    /// <summary>What the top-level rules naming <paramref name="selector"/> leave
    /// it with, the later declaration of a property replacing the earlier one —
    /// the cascade between rules of equal specificity. It reads names, not
    /// shorthands: a <c>border</c> after a <c>border-left</c> keeps both
    /// here.</summary>
    public static IReadOnlyDictionary<string, string> Effective(string css, string selector)
    {
        var effective = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var rule in TopLevel(css).Where(rule => rule.Selectors.Contains(selector, StringComparer.Ordinal)))
        {
            foreach (var (property, value) in rule.Declarations)
            {
                effective[property] = value;
            }
        }

        return effective;
    }

    /// <summary>A selector list split at its top-level commas, so the commas
    /// inside <c>:is()</c>, <c>:where()</c> and <c>:has()</c> stay where they
    /// are.</summary>
    private static List<string> Selectors(string prelude)
    {
        var selectors = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < prelude.Length; i++)
        {
            switch (prelude[i])
            {
                case '(' or '[':
                    depth++;
                    break;
                case ')' or ']':
                    depth--;
                    break;
                case ',' when depth == 0:
                    selectors.Add(Collapse(prelude[start..i]));
                    start = i + 1;
                    break;
            }
        }

        selectors.Add(Collapse(prelude[start..]));
        return selectors;
    }

    private static List<KeyValuePair<string, string>> Declarations(string body)
    {
        var declarations = new List<KeyValuePair<string, string>>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i <= body.Length; i++)
        {
            var c = i < body.Length ? body[i] : ';';

            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ';' && depth == 0)
            {
                var declaration = body[start..i];
                var colon = declaration.IndexOf(':');
                if (colon > 0)
                {
                    declarations.Add(new(
                        Collapse(declaration[..colon]),
                        Collapse(declaration[(colon + 1)..])));
                }

                start = i + 1;
            }
        }

        return declarations;
    }

    /// <summary>Comments blanked rather than removed, so the line numbers the
    /// failures report are the file's own.</summary>
    private static string WithoutComments(string css) =>
        Regex.Replace(css, @"/\*.*?\*/", comment =>
        {
            var blank = new StringBuilder(comment.Length);
            foreach (var c in comment.Value) blank.Append(c == '\n' ? '\n' : ' ');
            return blank.ToString();
        }, RegexOptions.Singleline);

    private static string Collapse(string text) => Regex.Replace(text.Trim(), @"\s+", " ");
}
