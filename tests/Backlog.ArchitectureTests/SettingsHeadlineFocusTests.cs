using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// Every shell routes through <c>FocusOnNavigate Selector="h1"</c>, so after each page change
/// focus lands on the Settings headline — an <c>h1</c> the router gives <c>tabindex="-1"</c> so a
/// screen reader announces the new page. The browser then draws its focus outline around the
/// whole headline box, over the eyebrow, on every page load (QA follow-up F8 of the Settings
/// redesign). The headline is never a tab stop, so a ring on it tells a keyboard user nothing;
/// the focus stays, the outline goes.
///
/// <para>Suppressed for <c>:focus</c> outright rather than <c>:focus:not(:focus-visible)</c>:
/// a browser treats focus moved by script on a fresh load, or after a menu link was activated
/// from the keyboard, as focus-visible, so the narrower selector still draws the ring after an
/// address change — the case F8 reported.</para>
///
/// <para>Asserted against the stylesheet rather than in bUnit: the markup is right and the
/// focus is right; the whole defect is what the stylesheet does with them.</para>
/// </summary>
public class SettingsHeadlineFocusTests
{
    [Fact]
    public void The_Settings_headline_takes_navigation_focus_without_drawing_an_outline()
    {
        var library = new DirectoryInfo(Path.Combine(
            Repository.Root.FullName, "src", "Core", "Backlog.UI.Components"));
        var frame = new FileInfo(Path.Combine(library.FullName, "Settings", "SettingsFrame.razor"));
        var stylesheet = new FileInfo(Path.Combine(library.FullName, "wwwroot", "components.css"));
        var desktopRoutes = new FileInfo(Path.Combine(
            Repository.Root.FullName, "src", "App", "Backlog.Desktop.UI", "Shell", "Routes.razor"));

        Assert.True(frame.Exists, "SettingsFrame.razor is not where this test looks for it.");
        Assert.True(stylesheet.Exists, "The library's stylesheet is not where this test looks for it.");
        Assert.True(desktopRoutes.Exists, "The desktop shell's Routes.razor is not where this test looks for it.");

        // The premise: the router focuses an h1, and the headline is that h1. Should either
        // change, the rule below may be guarding nothing and has to be revisited, not left to pass.
        Assert.Matches(@"<FocusOnNavigate[^>]*Selector=""h1""", File.ReadAllText(desktopRoutes.FullName));
        Assert.Matches(@"<h1\s+class=""settings-frame__headline""", File.ReadAllText(frame.FullName));

        var rule = Regex.Match(
            File.ReadAllText(stylesheet.FullName),
            @"^\.settings-frame__headline:focus\s*\{(?<body>[^}]*)\}",
            RegexOptions.Multiline);

        Assert.True(
            rule.Success,
            "components.css has no .settings-frame__headline:focus rule, so the outline the browser draws "
            + "when navigation focuses the headline shows on every Settings page.");
        Assert.Matches(@"outline\s*:\s*none", rule.Groups["body"].Value);
    }
}
