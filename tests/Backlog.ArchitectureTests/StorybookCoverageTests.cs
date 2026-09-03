using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// Rules that keep the storybook honest about the library it documents.
///
/// <para>The dependency rules in <see cref="UiLibraryBoundaryTests"/> prove the
/// components <em>can</em> render without the application. These prove they are
/// actually rendered: a component with no story is one nobody can look at before
/// it ships, which is the whole reason the harness exists.</para>
/// </summary>
public class StorybookCoverageTests
{
    private static readonly DirectoryInfo Library =
        new(Path.Combine(Repository.Root.FullName, "src", "Core", "Backlog.UI.Components"));

    private static readonly DirectoryInfo StorybookPages =
        new(Path.Combine(Repository.Root.FullName, "src", "Harness", "Backlog.UI.Storybook", "Components", "Pages"));

    [Fact]
    public void Every_component_in_the_library_is_rendered_by_a_story()
    {
        var stories = StoryMarkup();
        var components = ComponentNames().ToList();

        // Both guards are against the test passing because it found nothing to
        // check — a moved folder would otherwise make it silently vacuous.
        Assert.NotEmpty(stories);
        Assert.NotEmpty(components);

        var missing = components
            // A component is covered when a page actually instantiates it, not
            // merely mentions it in prose: <Card> or <Card ... /> but not "Card".
            .Where(name => !Regex.IsMatch(stories, $@"<{Regex.Escape(name)}(\s|/|>)"))
            .OrderBy(name => name)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "Every component needs a story, or it ships without anyone having seen it. "
            + $"No storybook page renders: {string.Join(", ", missing)}");
    }

    /// <summary>Routes that exist to be landed on rather than navigated to, so
    /// nothing links to them on purpose.</summary>
    private static readonly HashSet<string> UnlistedByDesign = ["not-found"];

    [Fact]
    public void Every_storybook_page_is_reachable_from_the_index()
    {
        // A page nobody links to is a page nobody reviews. StorybookIndex is the
        // single list the sidebar and the introduction both render from, so a new
        // page has exactly one place to register itself.
        var index = File.ReadAllText(Path.Combine(
            Repository.Root.FullName,
            "src", "Harness", "Backlog.UI.Storybook", "Components", "Shared", "StorybookIndex.cs"));

        var unlisted = StorybookPages.EnumerateFiles("*.razor")
            .Select(page => Regex.Match(File.ReadAllText(page.FullName), @"@page\s+""/([^""]*)"""))
            .Where(route => route.Success)
            .Select(route => route.Groups[1].Value)
            .Where(route => !UnlistedByDesign.Contains(route))
            .Where(route => !Regex.IsMatch(index, $@"new\(""{Regex.Escape(route)}"""))
            .OrderBy(route => route)
            .ToList();

        Assert.True(
            unlisted.Count == 0,
            "These storybook routes are not in StorybookIndex, so nothing links to them: "
            + string.Join(", ", unlisted.Select(route => "/" + route)));
    }

    /// <summary>
    /// Every rule a storybook page claims to be governed by still exists.
    ///
    /// <para>The restructure moved the rules out of the story prose and into
    /// <c>.design</c>, so a page now names its chapter and anchor and
    /// <c>DesignGuideline</c> renders that section beside the component. The
    /// failure mode came with it: rename a heading in <c>.design</c> and the page
    /// silently draws a sentence saying the rule cannot be found, in a host that
    /// nothing in CI opens. Every other convention in this library is enforced by a
    /// test; this makes that one too.</para>
    ///
    /// <para>The slug rule has to match <c>DesignGuideline.Slug</c>, which is
    /// GitHub's, because these same anchors are written into <c>.design</c>'s own
    /// cross-links and into pull-request comments.</para>
    /// </summary>
    [Fact]
    public void Every_design_rule_a_story_names_resolves_to_a_heading()
    {
        var design = new DirectoryInfo(Path.Combine(Repository.Root.FullName, ".design"));

        var headings = design.EnumerateFiles("*.md")
            .ToDictionary(
                chapter => chapter.Name,
                chapter => File.ReadLines(chapter.FullName)
                    .Select(line => Regex.Match(line, @"^(#{1,6}) (.+)$"))
                    .Where(heading => heading.Success)
                    .Select(heading => Slug(heading.Groups[2].Value.Trim()))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        // A DesignGuideline is constructed target-typed inside a page's Rules
        // array, so the reference is a bare new("chapter.md#anchor", ...).
        var references = StorybookPages.EnumerateFiles("*.razor")
            .SelectMany(page => Regex
                .Matches(File.ReadAllText(page.FullName), @"new\(""([a-z0-9-]+\.md(?:#[^""]*)?)""")
                .Select(reference => (Page: page.Name, Reference: reference.Groups[1].Value)))
            .ToList();

        // Against the test passing because the pattern stopped matching anything.
        Assert.NotEmpty(headings);
        Assert.NotEmpty(references);

        var broken = references
            .Where(named =>
            {
                var parts = named.Reference.Split('#', 2);

                return !headings.TryGetValue(parts[0], out var anchors)
                    || (parts.Length == 2 && !anchors.Contains(parts[1]));
            })
            .Select(named => $"{named.Page} names .design/{named.Reference}")
            .Distinct()
            .Order()
            .ToList();

        Assert.True(
            broken.Count == 0,
            "A storybook page is governed by a rule that is not there any more, so the page renders an "
            + "apology instead of the rule. Either the heading moved and the reference needs updating, or "
            + "the rule was dropped and the page's claim to follow it goes with it: "
            + string.Join("; ", broken));
    }

    /// <summary>
    /// The Foundations page measures every palette colour, and nothing else.
    ///
    /// <para><c>storybook.js</c> reads token <em>values</em> off the live document,
    /// so the measurements can never be stale — but the list of what to measure is
    /// a hardcoded array, and nothing until now read it back. It drifts in both
    /// directions and both are silent. A token added to <c>components.css</c> and
    /// not to the array simply never appears on the page, so the one place that
    /// would have shown it failing its contrast pair does not know it exists;
    /// <c>color-error-text</c> reached the stylesheet and the design folder before
    /// it reached the array. A token dropped from the stylesheet and left in the
    /// array is worse than absent: <c>getPropertyValue</c> answers an empty string
    /// for an undeclared property, and the row reports a measurement of a colour
    /// nobody declared.</para>
    ///
    /// <para>Scoped to the palette. The syntax and band tokens are themes rather
    /// than palette colours and the page scores them where they are used — a run of
    /// code on a code block — and the role tokens are derived, so they hold no value
    /// to disagree about. <see cref="DesignPalette.PaletteTokens"/> draws that line
    /// once, for this and for the value comparison in
    /// <see cref="DesignTokenTests"/>.</para>
    /// </summary>
    [Fact]
    public void The_storybook_measures_exactly_the_palette_the_library_declares()
    {
        var declared = DesignPalette.PaletteTokens();
        var measured = MeasuredColorTokens();

        // Neither side may go quiet: a moved file or a renamed array would
        // otherwise make this pass by comparing two empty sets.
        Assert.NotEmpty(declared);
        Assert.NotEmpty(measured);

        var unmeasured = declared.Except(measured, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var undeclared = measured.Except(declared, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            unmeasured.Count == 0,
            "components.css declares palette colours the storybook's COLOR_GROUPS does not list, so the "
            + "Foundations page never measures them and nothing shows them failing a contrast pair: "
            + string.Join(", ", unmeasured));

        Assert.True(
            undeclared.Count == 0,
            "The storybook's COLOR_GROUPS lists colours components.css does not declare. The page reads an "
            + "undeclared property as an empty string, so it reports a measurement of nothing: "
            + string.Join(", ", undeclared));
    }

    /// <summary>The token names inside <c>storybook.js</c>'s <c>COLOR_GROUPS</c>.
    /// Only the <c>name</c> of each entry — an <c>against</c> names a token the
    /// group is scored on rather than one it measures, and every one of those is
    /// somebody's <c>name</c> elsewhere in the array.</summary>
    private static HashSet<string> MeasuredColorTokens()
    {
        var script = File.ReadAllText(Path.Combine(
            Repository.Root.FullName, "src", "Harness", "Backlog.UI.Storybook", "wwwroot", "storybook.js"));

        var array = Regex.Match(script, @"const COLOR_GROUPS = \[(?<body>.*?)^    \];",
            RegexOptions.Singleline | RegexOptions.Multiline);

        Assert.True(
            array.Success,
            "storybook.js has no COLOR_GROUPS array. It was renamed or reshaped, and the rule reading it "
            + "needs updating rather than silently checking nothing.");

        return
        [
            .. Regex.Matches(array.Groups["body"].Value, @"name:\s*'(--[a-z0-9-]+)'")
                .Select(match => match.Groups[1].Value)
        ];
    }

    /// <summary>GitHub's heading slug, and the same one
    /// <c>DesignGuideline.Slug</c> applies: lower-cased, letters and digits kept,
    /// spaces and hyphens to hyphens, every other character dropped.</summary>
    private static string Slug(string heading) =>
        string.Concat(heading.ToLowerInvariant().Select(character =>
            char.IsLetterOrDigit(character) ? character.ToString()
            : character is ' ' or '-' ? "-"
            : string.Empty));

    /// <summary>Every component the library offers a host: one .razor file each,
    /// minus _Imports, which is compiler plumbing rather than a component.</summary>
    private static IEnumerable<string> ComponentNames() =>
        Library.EnumerateFiles("*.razor", SearchOption.AllDirectories)
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(file => Path.GetFileNameWithoutExtension(file.Name))
            .Where(name => !name.StartsWith('_'));

    private static string StoryMarkup() =>
        string.Join('\n', StorybookPages.EnumerateFiles("*.razor").Select(file => File.ReadAllText(file.FullName)));
}
