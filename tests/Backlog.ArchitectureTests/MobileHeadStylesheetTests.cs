using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The mobile head styles itself from the design system and nothing else.
///
/// <para>It arrived from the MAUI Blazor template with Bootstrap linked ahead of
/// <c>components.css</c> and the template's own stylesheet rules in
/// <c>Backlog.Mobile.UI/wwwroot/app.css</c>. Bootstrap's resets outrank the
/// library wherever the selectors overlap, the template's <c>.btn:focus</c> ring
/// painted over the library's <c>.btn:focus-visible</c> outline on every shared
/// button, and the one template rule still in use drew the mobile error tone from
/// <c>#ff8080</c>, a colour outside the palette. The desktop head never linked
/// Bootstrap, and the mobile web harness never did either — so the harness showed
/// a phone the MAUI head did not render.</para>
///
/// <para>Asserted against the files because the defect is entirely in which
/// stylesheets load and what they say; bUnit renders the markup faithfully and
/// brings no cascade to resolve.</para>
/// </summary>
public class MobileHeadStylesheetTests
{
    private const string LibraryStylesheet = "_content/Backlog.UI.Components/components.css";

    private static readonly string MobileWebRoot =
        Path.Combine(Repository.Root.FullName, "src", "App", "Backlog.Mobile", "wwwroot");

    private static readonly string MobileStylesheet =
        Path.Combine(Repository.Root.FullName, "src", "App", "Backlog.Mobile.UI", "wwwroot", "app.css");

    /// <summary>The template's rules that style markup no mobile screen renders:
    /// Bootstrap's button and form classes and Blazor's validation and
    /// error-boundary output.</summary>
    private static readonly string[] TemplateSelectors =
    [
        ".btn-primary",
        ".btn:focus",
        ".valid.modified",
        ".invalid",
        ".validation-message",
        ".blazor-error-boundary",
        ".darker-border-checkbox",
        ".form-floating"
    ];

    /// <summary>The rules that style what the head does render: <c>#app</c>,
    /// <c>.status-bar-safe-area</c> and <c>#blazor-error-ui</c> from
    /// <c>index.html</c>, and <c>.content</c> from <c>MainLayout.razor</c>.</summary>
    private static readonly string[] RenderedSelectors =
    [
        "#app",
        ".content",
        ".status-bar-safe-area",
        "#blazor-error-ui"
    ];

    [Fact]
    public void The_mobile_head_links_the_library_stylesheet_first()
    {
        var links = Regex.Matches(
                File.ReadAllText(Path.Combine(MobileWebRoot, "index.html")),
                @"<link[^>]*href=""([^""]+\.css)""")
            .Select(match => match.Groups[1].Value)
            .ToList();

        Assert.NotEmpty(links);
        Assert.True(
            links[0] == LibraryStylesheet,
            $"src/App/Backlog.Mobile/wwwroot/index.html links {links[0]} first. {LibraryStylesheet} has to "
            + "be the first stylesheet the head loads: anything ahead of it is a second base the design "
            + "system has to fight, and Bootstrap's resets won wherever the selectors overlapped.");
    }

    [Fact]
    public void The_mobile_head_ships_no_vendored_bootstrap()
    {
        Assert.False(
            Directory.Exists(Path.Combine(MobileWebRoot, "lib", "bootstrap")),
            "src/App/Backlog.Mobile/wwwroot/lib/bootstrap is still in the head. Nothing links it, so it is "
            + "dead weight in the package — and one link away from outranking the design system again.");
    }

    [Fact]
    public void The_mobile_stylesheet_carries_no_template_rule()
    {
        var css = WithoutComments(File.ReadAllText(MobileStylesheet));
        var selectors = Selectors(css);

        var template = TemplateSelectors
            .Where(template => selectors.Any(selector => selector.Contains(template, StringComparison.Ordinal)))
            .ToList();

        Assert.True(
            template.Count == 0,
            "src/App/Backlog.Mobile.UI/wwwroot/app.css still carries the MAUI template's rules for "
            + $"{string.Join(", ", template)}. No mobile screen renders that markup, and the rules paint "
            + "colours from outside the palette.");

        Assert.DoesNotContain("--bs-", css, StringComparison.Ordinal);
    }

    [Fact]
    public void The_mobile_stylesheet_keeps_the_rules_for_what_the_head_renders()
    {
        var selectors = Selectors(WithoutComments(File.ReadAllText(MobileStylesheet)));

        var missing = RenderedSelectors
            .Where(rendered => !selectors.Any(selector => selector.Split(',')
                .Select(part => part.Trim())
                .Any(part => part == rendered || part.StartsWith(rendered + " ", StringComparison.Ordinal))))
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"src/App/Backlog.Mobile.UI/wwwroot/app.css has lost its rules for {string.Join(", ", missing)}, "
            + "which index.html and MainLayout.razor still render.");
    }

    /// <summary>Five mobile alerts wear <c>BaseClass="error"</c>, so the
    /// <c>.error</c> rule is the whole of the mobile error tone.
    /// <c>.devbook/design/color-scheme.md</c> names <c>color-error-text</c> as the
    /// one sanctioned foreground for error text with no surface behind it.</summary>
    [Fact]
    public void The_mobile_error_tone_comes_from_the_palette()
    {
        var css = WithoutComments(File.ReadAllText(MobileStylesheet));

        var rule = Regex.Match(css, @"(?:^|\})\s*\.error\s*\{(?<body>[^}]*)\}");

        Assert.True(rule.Success, "src/App/Backlog.Mobile.UI/wwwroot/app.css has no `.error` rule.");
        Assert.Matches(@"(?:^|;)\s*color\s*:\s*var\(--color-error-text\)\s*;?", rule.Groups["body"].Value.Trim());
        Assert.DoesNotContain("#ff8080", css, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> Selectors(string css) =>
        Regex.Matches(css, @"(?<selector>[^{}]+)\{")
            .Select(match => match.Groups["selector"].Value.Trim())
            .Where(selector => !selector.StartsWith('@'))
            .ToList();

    private static string WithoutComments(string css) =>
        Regex.Replace(css, @"/\*.*?\*/", " ", RegexOptions.Singleline);
}
