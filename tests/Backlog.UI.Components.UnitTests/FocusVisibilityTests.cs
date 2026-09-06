using System.Text.RegularExpressions;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Where the keyboard is, on the controls that had stopped saying.
///
/// <para><c>.design/accessibility.md#focus-visibility</c> is unconditional:
/// keyboard focus MUST always be visible, and <c>outline: none</c> is permitted
/// only where something compliant replaces it — an outline in
/// <c>--color-border-focus</c> at <c>--border-width-2</c> offset 2 px, drawn with
/// <c>outline</c> rather than a shadow so Windows High Contrast keeps it. A colour
/// change is not a replacement. It is the cue hover already spends, which costs
/// the other half of the same table as well: focus, hover and selection MUST be
/// distinguishable, and a control whose two states share one rule cannot be.</para>
///
/// <para>The rename pencil on a task row is the case that started this — one rule
/// for hover and focus together with the outline taken away inside it, while the
/// bin two controls along paid for a real ring. The last test is the rest of the
/// stylesheet held to the same line: whatever still removes an outline has to say
/// in the stylesheet why it may, so the next control written the pencil's way
/// fails here rather than shipping.</para>
///
/// <para>Asserted against the stylesheet rather than a render, for the reason
/// <c>TaskRenameFieldUnderlineTests</c> gives for the same shape of test: the
/// markup is not in question — <c>TaskItem</c> puts the classes where they
/// belong — and the whole of the defect is in what the cascade does with them,
/// which bUnit brings no layout engine to see.</para>
/// </summary>
public sealed class FocusVisibilityTests
{
    /// <summary>The phrase a rule that keeps <c>outline: none</c> has to carry above
    /// it. Deliberately a fixed form of words rather than "any comment at all": the
    /// point is that somebody wrote down which compliant thing draws the focus
    /// instead, and a passing remark above the rule is not that.</summary>
    private const string Exemption = "Focus-visibility exemption";

    /// <summary>
    /// The regression. The pencil is a control, so it draws the ring every other
    /// control here draws.
    /// </summary>
    [Fact]
    public void The_rename_pencil_shows_where_the_focus_is()
    {
        var focus = Rule(".task-item__edit:focus-visible");

        Assert.Matches(@"outline:\s*var\(--border-width-2\)\s+solid\s+var\(--color-border-focus\)", focus);
        Assert.Matches(@"outline-offset:\s*2px", focus);
    }

    /// <summary>
    /// And nothing else takes it away again. The defect was never a missing rule —
    /// it was a rule the pencil shares with its hover state removing the outline
    /// inside it, which is exactly the shape a hurried fix puts back.
    /// </summary>
    [Fact]
    public void Nothing_that_reaches_the_rename_pencil_takes_its_outline_away()
    {
        var offenders = Rules()
            .Where(rule => Regex.IsMatch(rule.Selectors, @"\.task-item__edit(?![\w-])"))
            .Where(rule => Regex.IsMatch(rule.Body, @"outline\s*:\s*none"))
            .Select(rule => rule.Selectors)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "outline: none on the rename pencil needs a compliant replacement, and a colour change is not "
            + "one — .design/accessibility.md#focus-visibility. Rules found:\n"
            + string.Join("\n\n", offenders));
    }

    /// <summary>
    /// The same ring as the bin beside it, declaration for declaration. Two icon
    /// buttons a few pixels apart on one row that ringed differently would read as
    /// two kinds of control, and the bin's is the rule this library already agreed
    /// on.
    /// </summary>
    [Fact]
    public void The_rename_pencil_wears_the_same_ring_as_the_bin_beside_it()
    {
        Assert.Equal(
            OutlineDeclarations(Rule(".task-item__delete:focus-visible")),
            OutlineDeclarations(Rule(".task-item__edit:focus-visible")));
    }

    /// <summary>
    /// What the fix must not cost. The pencil is quiet until the row is under the
    /// pointer, and lighting it is the whole of what the pointer does — a ring
    /// under the mouse would announce a keyboard position nobody is at.
    /// </summary>
    [Fact]
    public void The_pointer_still_lights_the_pencil_and_still_draws_no_ring()
    {
        var lit = Rule(
            ".task-item:hover .task-item__edit,\n.task-item__edit:hover,\n.task-item__edit:focus-visible");

        Assert.Contains("color: var(--color-text-primary);", lit);
        Assert.DoesNotContain("outline", lit);
    }

    /// <summary>
    /// The comment above the bin used to argue that the pencil's colour change was
    /// a survivable non-compliance, and that the ring was the one place the bin
    /// parted company with it. Both halves are false once the pencil has a ring,
    /// and a stylesheet explaining a difference it no longer has is worse than one
    /// explaining nothing.
    /// </summary>
    [Fact]
    public void The_bin_no_longer_explains_itself_as_the_only_one_with_a_ring()
    {
        var comment = CommentAbove(".task-item__delete");

        Assert.DoesNotContain("parts company", comment);
        Assert.DoesNotContain("survivable", comment);
    }

    /// <summary>
    /// The sweep. Every <c>outline: none</c> left in the library stylesheet carries
    /// <see cref="Exemption"/> in the comment directly above its rule, naming what
    /// draws the focus instead — a wrapper's <c>:focus-within</c>, a border that is
    /// already focus-coloured, or an element that is not a control at all. A rule
    /// that cannot name one is a rule with no replacement.
    /// </summary>
    [Fact]
    public void Every_outline_the_library_still_removes_says_why_above_the_rule()
    {
        var css = Css();

        // Searched in a copy whose comments are blanked out but whose length is
        // untouched, so prose quoting `outline: none` — the exemptions themselves
        // do — is not mistaken for a declaration, and every index still points at
        // the same character of the real stylesheet.
        var unexplained = Regex.Matches(BlankComments(css), @"outline\s*:\s*none")
            .Select(removal => AboveTheRule(css, removal.Index))
            .Where(segment => !segment.Contains(Exemption, StringComparison.Ordinal))
            .Select(SelectorsOf)
            .ToArray();

        Assert.True(
            unexplained.Length == 0,
            "outline: none needs a compliant replacement, and a rule that keeps one has to say above itself "
            + $"which — a comment starting \"{Exemption}\". See "
            + ".design/accessibility.md#focus-visibility. Unexplained:\n"
            + string.Join("\n", unexplained));
    }

    /// <summary>Everything between the end of the previous rule and the declaration:
    /// the comment block above this rule, where it has one, and its selector list.
    /// The end of the previous rule is looked for in the blanked copy, because one
    /// comment in this stylesheet quotes markup with braces in it.</summary>
    private static string AboveTheRule(string css, int declaration)
    {
        var previous = Regex.Matches(BlankComments(css)[..declaration], @"\}[ \t]*\n").LastOrDefault();
        var start = previous is null ? 0 : previous.Index + previous.Length;

        return css[start..declaration];
    }

    /// <summary>The selector list out of such a segment, comments dropped and put on
    /// one line, so a failure names the rule rather than reprinting its prose.</summary>
    private static string SelectorsOf(string segment)
    {
        var brace = segment.LastIndexOf('{');
        var selectors = brace < 0 ? segment : segment[..brace];

        return Regex.Replace(StripComments(selectors), @"\s+", " ").Trim();
    }

    /// <summary>Only the outline declarations of a rule, in order, so two rules can
    /// be compared on the ring alone rather than on everything else they set.</summary>
    private static string[] OutlineDeclarations(string rule) =>
        Regex.Matches(rule, @"outline[\w-]*\s*:[^;]+;")
            .Select(declaration => Regex.Replace(declaration.Value, @"\s+", " "))
            .ToArray();

    /// <summary>The comment block sitting immediately above a rule, or the empty
    /// string where nothing does.</summary>
    private static string CommentAbove(string selector)
    {
        var css = Css();

        var start = css.IndexOf("\n" + selector + " {", StringComparison.Ordinal);
        Assert.True(start >= 0, $"components.css has no rule for {selector}.");

        var close = css.LastIndexOf("*/", start, StringComparison.Ordinal);
        if (close < 0 || css[(close + 2)..start].Trim().Length != 0)
        {
            return string.Empty;
        }

        var open = css.LastIndexOf("/*", close, StringComparison.Ordinal);

        return open < 0 ? string.Empty : css[open..(close + 2)];
    }

    /// <summary>The rule whose selector list opens with <paramref name="selector"/>
    /// and ends there, braces matched so a nested block cannot close it early.
    /// Anchored to the start of a line for the reason
    /// <c>TaskRenameFieldUnderlineTests</c> gives: every selector here is also the
    /// tail of a longer one, and a plain substring search would return that rule's
    /// body instead. A line ending in a comma is skipped as well, because the
    /// pencil's ring and the last line of the pair above it are the same text and
    /// only the comma before them tells the two rules apart.</summary>
    private static string Rule(string selector)
    {
        var css = Css();

        var start = 0;

        while (true)
        {
            start = css.IndexOf("\n" + selector + " {", start, StringComparison.Ordinal) + 1;
            Assert.True(start > 0, $"components.css has no rule opening with {selector}.");

            if (start == 1 || css[start - 2] != ',')
            {
                break;
            }
        }

        var open = css.IndexOf('{', start);
        var close = MatchingBrace(css, open);

        Assert.True(close > 0, $"The rule for {selector} in components.css is never closed.");

        return css[start..(close + 1)];
    }

    /// <summary>Every rule in the stylesheet, comments removed and at-rules unwrapped,
    /// for the questions that are about a class rather than about one known rule.</summary>
    private static IEnumerable<(string Selectors, string Body)> Rules() => RulesIn(StripComments(Css()));

    private static IEnumerable<(string Selectors, string Body)> RulesIn(string css)
    {
        var index = 0;

        while (true)
        {
            var open = css.IndexOf('{', index);
            if (open < 0)
            {
                yield break;
            }

            var close = MatchingBrace(css, open);
            if (close < 0)
            {
                yield break;
            }

            var selectors = css[index..open].Trim();
            var body = css[(open + 1)..close];

            if (selectors.StartsWith('@') && body.Contains('{'))
            {
                foreach (var nested in RulesIn(body))
                {
                    yield return nested;
                }
            }
            else
            {
                yield return (selectors, body);
            }

            index = close + 1;
        }
    }

    private static int MatchingBrace(string css, int open)
    {
        var depth = 0;

        for (var index = open; index >= 0 && index < css.Length; index++)
        {
            if (css[index] == '{')
            {
                depth++;
            }
            else if (css[index] == '}' && --depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static string StripComments(string css) =>
        Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    /// <summary>The same stylesheet with every comment turned to blanks and every
    /// line kept where it was, for the searches that must not read the prose but
    /// still want to report a position in the real file.</summary>
    private static string BlankComments(string css) =>
        Regex.Replace(
            css,
            @"/\*.*?\*/",
            comment => new string(comment.Value.Select(character => character == '\n' ? '\n' : ' ').ToArray()),
            RegexOptions.Singleline);

    private static string Css() => File.ReadAllText(RepositoryRoot.File(
        "src", "Core", "Backlog.UI.Components", "wwwroot", "components.css")).Replace("\r\n", "\n");
}
