using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// A module's screen rules ship with the module.
///
/// <para>Every module's class rules used to live in the desktop head's
/// <c>app.css</c>, so no module UI project carried a stylesheet: a module could
/// not be rendered by another host without copying its CSS, and the one shared
/// file is where the duplicate selectors piled up (issue #759). A module's UI
/// project now keeps its rules in <c>wwwroot/&lt;module&gt;.css</c>, a static web
/// asset served at <c>_content/&lt;project&gt;/&lt;module&gt;.css</c> the way the
/// component library serves <c>components.css</c>, and every host that renders
/// the module links it after the library.</para>
///
/// <para>Capture is the first module moved; the rest of the module rules are
/// still in <c>app.css</c> until their own moves.</para>
/// </summary>
public class ModuleStylesheetTests
{
    private const string LibraryStylesheet = "_content/Backlog.UI.Components/components.css";

    private const string App = "src/App/Backlog.Desktop.UI/wwwroot/app.css";

    /// <summary>A module's stylesheet: the file in its UI project, the path a host
    /// links it by, the class prefix its rules own, and the hosts that render
    /// the module.</summary>
    public sealed record ModuleStylesheet(string Module, string File, string Href, string Prefix, string[] Hosts);

    private static readonly ModuleStylesheet[] Modules =
    [
        new(
            "Capture",
            "src/Modules/Capture/Backlog.Modules.Capture.UI/wwwroot/capture.css",
            "_content/Backlog.Modules.Capture.UI/capture.css",
            ".capture-sources",
            // The sources panel sits on the desktop Inbox pane only; the mobile
            // heads never render it.
            [
                "src/App/Backlog.Desktop/wwwroot/index.html",
                "src/Harness/Backlog.Desktop.WebHarness/Components/App.razor"
            ])
    ];

    public static TheoryData<string> ModuleNames => [.. Modules.Select(module => module.Module)];

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void The_module_stylesheet_holds_the_rules_for_its_classes(string module)
    {
        var stylesheet = Find(module);
        var path = Path.Combine(Repository.Root.FullName, stylesheet.File);

        Assert.True(File.Exists(path), $"{stylesheet.File} does not exist. {module}'s screen rules ship with its UI project.");

        var selectors = CssRules.TopLevel(File.ReadAllText(path))
            .SelectMany(rule => rule.Selectors)
            .ToList();

        Assert.NotEmpty(selectors);
        Assert.All(selectors, selector => Assert.StartsWith(stylesheet.Prefix, selector, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void The_desktop_stylesheet_no_longer_declares_the_module_classes(string module)
    {
        var stylesheet = Find(module);
        var css = File.ReadAllText(Path.Combine(Repository.Root.FullName, App));

        var stragglers = Regex.Matches(css, $@"{Regex.Escape(stylesheet.Prefix)}[\w-]*")
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            stragglers.Count == 0,
            $"{App} still declares {string.Join(", ", stragglers)}. {module}'s rules live in {stylesheet.File}; "
            + "a second copy in the desktop stylesheet is the one an edit to the module does not find.");
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Every_host_that_renders_the_module_links_its_stylesheet_after_the_library(string module)
    {
        var stylesheet = Find(module);

        foreach (var host in stylesheet.Hosts)
        {
            var links = Regex.Matches(
                    File.ReadAllText(Path.Combine(Repository.Root.FullName, host)),
                    @"<link[^>]*href=""([^""]+\.css)""")
                .Select(match => match.Groups[1].Value)
                .ToList();

            var libraryAt = links.IndexOf(LibraryStylesheet);
            var moduleAt = links.IndexOf(stylesheet.Href);

            Assert.True(moduleAt >= 0, $"{host} does not link {stylesheet.Href}, so {module} renders unstyled there.");
            Assert.True(
                moduleAt > libraryAt,
                $"{host} links {stylesheet.Href} before {LibraryStylesheet}; the module's rules extend the library's tokens.");
        }
    }

    /// <summary>The move carries the rules over as they were; these are the
    /// panel's grid and log as <c>app.css</c> declared them.</summary>
    [Theory]
    [InlineData(".capture-sources__grid", "align-items: start; display: grid; gap: var(--spacing-md); grid-template-columns: repeat(auto-fill, minmax(min(100%, 24rem), 1fr))")]
    [InlineData(".capture-sources__log", "background: color-mix(in srgb, var(--color-text-primary) 3%, transparent); border-radius: var(--border-radius-sm); color: var(--color-text-secondary); display: grid; font-family: var(--font-family-mono); font-size: var(--font-size-xs); gap: var(--spacing-xs); list-style: none; margin: 0; max-height: 14rem; overflow-y: auto; padding: var(--spacing-sm)")]
    [InlineData(".capture-sources__source", "background: var(--color-background); border: var(--border-width) solid var(--color-border); border-radius: var(--border-radius-sm); display: grid; gap: var(--spacing-sm); padding: var(--spacing-md)")]
    public void A_moved_rule_keeps_the_declarations_it_had(string selector, string declarations)
    {
        var css = File.ReadAllText(Path.Combine(Repository.Root.FullName, Find("Capture").File));

        Assert.Equal(
            declarations,
            string.Join("; ", CssRules.Effective(css, selector)
                .OrderBy(declaration => declaration.Key, StringComparer.Ordinal)
                .Select(declaration => $"{declaration.Key}: {declaration.Value}")));
    }

    /// <summary>The desktop shell's scoped stylesheet was emptied and still
    /// produced a scoped-CSS bundle entry; the shell's rules are in
    /// <c>app.css</c>. It was the desktop's only scoped stylesheet, so with it
    /// gone the build writes no <c>Backlog.Desktop.styles.css</c> and the head
    /// links none. The head's bundle would also <c>@import</c> the scoped bundle
    /// of every referenced library, so the link has to come back the moment the
    /// desktop UI, the shared component library or any module UI gains a
    /// <c>.razor.css</c>.</summary>
    [Fact]
    public void The_desktop_head_links_the_scoped_bundle_exactly_when_one_is_built()
    {
        Assert.False(
            File.Exists(Path.Combine(Repository.Root.FullName, "src", "App", "Backlog.Desktop.UI", "Shell", "MainLayout.razor.css")),
            "src/App/Backlog.Desktop.UI/Shell/MainLayout.razor.css is back. It is empty and only adds a scoped-CSS bundle entry.");

        var roots = new[]
        {
            Path.Combine(Repository.Root.FullName, "src", "App", "Backlog.Desktop.UI"),
            Path.Combine(Repository.Root.FullName, "src", "App", "Backlog.Desktop"),
            Path.Combine(Repository.Root.FullName, "src", "Core"),
            Path.Combine(Repository.Root.FullName, "src", "Modules"),
        };
        var scoped = roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.razor.css", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        var linked = File.ReadAllText(Path.Combine(Repository.Root.FullName, "src", "App", "Backlog.Desktop", "wwwroot", "index.html"))
            .Contains("Backlog.Desktop.styles.css", StringComparison.Ordinal);

        Assert.True(
            linked == (scoped.Count > 0),
            scoped.Count > 0
                ? $"Scoped stylesheets exist ({string.Join(", ", scoped.Select(path => Path.GetRelativePath(Repository.Root.FullName, path)))}), so src/App/Backlog.Desktop/wwwroot/index.html must link Backlog.Desktop.styles.css."
                : "No project the desktop head references has a .razor.css, so the build writes no Backlog.Desktop.styles.css and index.html must not link it.");
    }

    private static ModuleStylesheet Find(string module) =>
        Modules.Single(candidate => candidate.Module == module);
}
