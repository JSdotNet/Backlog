using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// A rendered heading is a <c>p</c> carrying <c>role="heading"</c>, so the weight
/// a browser hands an <c>h1</c> never arrives and <c>.md-heading</c> has to state
/// one itself. Without it every heading in every document this product renders is
/// the same weight as the paragraph under it, which is what had happened once
/// already.
///
/// <para>This is <c>DesignTokenTests.A_heading_drawn_on_a_paragraph_is_given_the_weight_the_element_denies_it</c>
/// asked of the library rather than of one file. That fact reads
/// <c>MarkdownView.razor</c> and treats "no heading markup here" as "the rule does
/// not apply", which was true while the markup was there and became a silent pass
/// the moment the heading moved into <c>MarkdownBlockView</c>. The rule is worth
/// keeping and the premise is worth reading from wherever the markup actually is;
/// the older fact can go.</para>
///
/// <para>Asserted here rather than in bUnit for the reason it always was: the
/// markup was never wrong — right class, right role, right level — and the whole
/// defect was in what the stylesheet did with it.</para>
/// </summary>
public class MarkdownHeadingWeightTests
{
    [Fact]
    public void A_heading_drawn_on_a_paragraph_is_given_the_weight_the_element_denies_it()
    {
        var library = new DirectoryInfo(Path.Combine(
            Repository.Root.FullName, "src", "Core", "Backlog.UI.Components"));

        var stylesheet = new FileInfo(Path.Combine(library.FullName, "wwwroot", "components.css"));

        // Asserted before the premise, because the premise is read out of these
        // files: a path that no longer resolves would make the search match
        // nothing, the premise read false, and this test pass while checking
        // nothing at all.
        Assert.True(library.Exists, "The component library is not where this test looks for it.");
        Assert.True(stylesheet.Exists, "The library's stylesheet is not where this test looks for it.");

        var drawnOnAParagraph = library
            .EnumerateFiles("*.razor", SearchOption.AllDirectories)
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Any(file => Regex.IsMatch(File.ReadAllText(file.FullName), @"<p[^>]*class=""md-heading"));

        // Were headings ever drawn as h1-h6, the browser would supply the weight
        // and this would be asserting a rule nobody needs. Skipping on that is
        // deliberate; skipping because a file moved is what this rewrite is for.
        Assert.True(
            drawnOnAParagraph,
            "No component in the library draws a heading as a p carrying md-heading. Either headings became "
            + "real heading elements — in which case delete this rule — or the markup is composed from a "
            + "parameter now and the premise can no longer be read, in which case this rule has to change "
            + "with it rather than quietly pass.");

        var rule = Regex.Match(
            File.ReadAllText(stylesheet.FullName),
            @"^\.md-heading\s*\{(?<body>[^}]*)\}",
            RegexOptions.Multiline);

        Assert.True(rule.Success, "components.css has no .md-heading rule, so nothing styles a rendered heading.");

        Assert.True(
            Regex.IsMatch(rule.Groups["body"].Value, @"font-weight\s*:"),
            "A heading is drawn as a p with role=\"heading\", so .md-heading has to declare its own "
            + "font-weight. Without one every heading renders at 400 — the same weight as the paragraph "
            + "beneath it — and a sub-header stops being findable by anyone skimming.");
    }
}
