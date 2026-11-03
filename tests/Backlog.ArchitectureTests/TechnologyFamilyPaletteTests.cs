using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The Technology page colours a technology by its type family, and every view takes
/// that hue from one palette
/// (<c>.devbook/domain/devbook/requirements.md#requirement-colour-follows-the-type-family</c>).
///
/// <para>The palette is declared once, on the pane root <c>.tech-devbook-pane</c>, as
/// app-local aliases of the chart roles; a family rule only points
/// <c>--tech-family</c> at one of them. Two things drift without this: a view growing
/// its own family hues beside the shared ones — the Complexity board had a second set
/// — and a family hue taken from a semantic token, which then says error or primary
/// about a framework.</para>
/// </summary>
public class TechnologyFamilyPaletteTests
{
    private const string App = "src/App/Backlog.Desktop.UI/wwwroot/app.css";

    private const string PaneRoot = ".tech-devbook-pane";

    /// <summary>The slugs <c>TechnologyTypeFamilies.Slug</c> answers, one per family.</summary>
    private static readonly string[] Slugs =
    [
        "framework",
        "platform",
        "runtime",
        "language-package",
        "library-tool-plugin",
        "skill",
        "agent-service-format",
        "protocol",
        "other",
    ];

    private static readonly Regex FamilyMark = new(@"\.tech-family--(?<slug>[a-z-]+)", RegexOptions.Compiled);

    private static IReadOnlyList<CssRules.Rule> Rules() =>
        CssRules.TopLevel(File.ReadAllText(RepositoryRoot.File(App.Split('/'))));

    [Fact]
    public void The_family_hues_are_declared_once_on_the_pane_root_from_chart_roles_only()
    {
        var declarations = Rules()
            .SelectMany(rule => rule.Declarations
                .Where(declaration => declaration.Key.StartsWith("--tech-family-", StringComparison.Ordinal))
                .Select(declaration => (Rule: rule, declaration.Key, declaration.Value)))
            .ToList();

        Assert.Equal(Slugs.Select(slug => "--tech-family-" + slug).Order(), declarations.Select(entry => entry.Key).Order());

        foreach (var (rule, name, value) in declarations)
        {
            Assert.True(
                rule.Selectors.SequenceEqual([PaneRoot]),
                $"{App}:{rule.Line} declares {name} on {string.Join(", ", rule.Selectors)}; the family palette lives on {PaneRoot} alone.");

            var tokens = Regex.Matches(value, @"var\(\s*(--[a-z0-9-]+)").Select(match => match.Groups[1].Value).ToList();
            Assert.NotEmpty(tokens);
            Assert.All(tokens, token => Assert.True(
                token.StartsWith("--chart-", StringComparison.Ordinal),
                $"{App}:{rule.Line} derives {name} from {token}; a family hue derives from the chart roles only, never a semantic token."));
        }
    }

    [Fact]
    public void Every_family_rule_resolves_to_its_own_family_hue_under_the_pane_root()
    {
        var rules = Rules()
            .Where(rule => rule.Selectors.Any(selector => FamilyMark.IsMatch(selector)))
            .ToList();

        var marked = new List<string>();
        foreach (var rule in rules)
        {
            var selector = Assert.Single(rule.Selectors);
            var slug = FamilyMark.Match(selector).Groups["slug"].Value;
            marked.Add(slug);

            Assert.Equal($"{PaneRoot} .tech-family--{slug}", selector);
            var declaration = Assert.Single(rule.Declarations);
            Assert.Equal("--tech-family", declaration.Key);
            Assert.Equal($"var(--tech-family-{slug})", declaration.Value);
        }

        Assert.Equal(Slugs.Order(), marked.Order());
    }

    /// <summary>A view colours by family through <c>--tech-family</c>, the variable the
    /// family mark sets — never by a family set of its own.</summary>
    [Fact]
    public void A_view_takes_a_family_colour_from_the_family_mark_only()
    {
        foreach (var rule in Rules().Where(rule => rule.Selectors.Any(selector => Regex.IsMatch(selector, @"--family\b"))))
        {
            Assert.DoesNotContain(rule.Selectors, selector => Regex.IsMatch(selector, @"--family-[a-z]"));
            Assert.All(rule.Declarations, declaration => Assert.StartsWith("var(--tech-family", declaration.Value, StringComparison.Ordinal));
        }

        Assert.DoesNotContain(Rules().SelectMany(rule => rule.Selectors), selector => Regex.IsMatch(selector, @"__[a-z-]+--family-[a-z]"));
    }
}
