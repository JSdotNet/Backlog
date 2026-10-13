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
/// <para>Capture and Roadmap are moved; the rest of the module rules are still
/// in <c>app.css</c> until their own moves.</para>
/// </summary>
public class ModuleStylesheetTests
{
    private const string LibraryStylesheet = "_content/Backlog.UI.Components/components.css";

    private const string DesktopStylesheet = "app.css";

    private const string App = "src/App/Backlog.Desktop.UI/wwwroot/app.css";

    /// <summary>A module's stylesheet: the file in its UI project, the path a host
    /// links it by, the class prefixes its rules own, the selectors headed by a
    /// library class that only reach into the module's own elements, the
    /// selectors naming a module class that are the shell's and stay in
    /// <c>app.css</c>, and the hosts that render the module.</summary>
    public sealed record ModuleStylesheet(
        string Module,
        string File,
        string Href,
        string[] Prefixes,
        string[] Scoped,
        string[] Shell,
        string[] Hosts);

    private static readonly ModuleStylesheet[] Modules =
    [
        new(
            "Capture",
            "src/Modules/Capture/Backlog.Modules.Capture.UI/wwwroot/capture.css",
            "_content/Backlog.Modules.Capture.UI/capture.css",
            [".capture-sources"],
            [],
            [],
            // The sources panel sits on the desktop Inbox pane only; the mobile
            // heads never render it.
            [
                "src/App/Backlog.Desktop/wwwroot/index.html",
                "src/Harness/Backlog.Desktop.WebHarness/Components/App.razor"
            ]),
        new(
            "Roadmap",
            "src/Modules/Roadmap/Backlog.Modules.Roadmap.UI/wwwroot/roadmap.css",
            "_content/Backlog.Modules.Roadmap.UI/roadmap.css",
            [".roadmap-band", ".roadmap-pace", ".roadmap-editor", ".roadmap-days-off", ".roadmap-hours", ".roadmap-order", ".roadmap-gathered"],
            // The timeline is the library's; these two reach into it only where a
            // band carries the module's pace control.
            [
                ".roadmap-timeline__group-content:has(> .roadmap-pace)",
                ".roadmap-timeline__group--slotted:has(.roadmap-pace__hint) .roadmap-timeline__group-name"
            ],
            // The view the band fills is the shell's (Home.razor, local ADR 0022).
            [".roadmap-view > .roadmap-band"],
            // Only the desktop UI references the Roadmap module; neither mobile
            // head renders the band.
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

        var css = File.ReadAllText(path);
        var selectors = CssRules.TopLevel(css)
            .Concat(Conditional(css))
            .SelectMany(rule => rule.Selectors)
            .ToList();

        Assert.NotEmpty(selectors);
        Assert.All(selectors, selector => Assert.True(
            stylesheet.Prefixes.Any(prefix => selector.StartsWith(prefix, StringComparison.Ordinal))
                || stylesheet.Scoped.Contains(selector, StringComparer.Ordinal),
            $"{stylesheet.File} declares {selector}, which is not one of {module}'s classes."));
    }

    /// <summary>The stylesheet is written the way the rest of the repository's
    /// stylesheets are: CRLF line endings and no byte-order mark.</summary>
    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void The_module_stylesheet_has_CRLF_line_endings_and_no_byte_order_mark(string module)
    {
        var stylesheet = Find(module);
        var bytes = File.ReadAllBytes(Path.Combine(Repository.Root.FullName, stylesheet.File));

        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..], $"{stylesheet.File} starts with a byte-order mark.");
        Assert.DoesNotMatch(@"(?<!\r)\n", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void The_desktop_stylesheet_no_longer_declares_the_module_classes(string module)
    {
        var stylesheet = Find(module);
        var css = File.ReadAllText(Path.Combine(Repository.Root.FullName, App));

        // A shell rule may name a module class; it stays, so it is no straggler.
        foreach (var shell in stylesheet.Shell)
        {
            Assert.True(
                css.Contains(shell, StringComparison.Ordinal),
                $"{App} no longer declares the shell rule {shell}; drop it from {module}'s shell list.");
            css = css.Replace(shell, string.Empty, StringComparison.Ordinal);
        }

        var stragglers = stylesheet.Prefixes
            .SelectMany(prefix => Regex.Matches(css, $@"{Regex.Escape(prefix)}[\w-]*"))
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
            var desktopAt = links.IndexOf(DesktopStylesheet);

            Assert.True(moduleAt >= 0, $"{host} does not link {stylesheet.Href}, so {module} renders unstyled there.");
            Assert.True(
                moduleAt > libraryAt,
                $"{host} links {stylesheet.Href} before {LibraryStylesheet}; the module's rules extend the library's tokens.");
            // Before app.css, so a shell rule left there still wins a tie with a
            // module rule it used to follow (the Roadmap view's `.roadmap-view >
            // .roadmap-band`, which wins on specificity besides).
            Assert.True(
                desktopAt < 0 || moduleAt < desktopAt,
                $"{host} links {stylesheet.Href} after {DesktopStylesheet}; a module's rules load between the library and the shell.");
        }
    }

    /// <summary>The move carries the rules over as they were; these are the
    /// Capture panel's grid and log, and the Roadmap band, its pace and the two
    /// rules that reach into the library's timeline, as <c>app.css</c> declared
    /// them.</summary>
    [Theory]
    [InlineData("Capture", ".capture-sources__grid", "align-items: start; display: grid; gap: var(--spacing-md); grid-template-columns: repeat(auto-fill, minmax(min(100%, 24rem), 1fr))")]
    [InlineData("Capture", ".capture-sources__log", "background: color-mix(in srgb, var(--color-text-primary) 3%, transparent); border-radius: var(--border-radius-sm); color: var(--color-text-secondary); display: grid; font-family: var(--font-family-mono); font-size: var(--font-size-xs); gap: var(--spacing-xs); list-style: none; margin: 0; max-height: 14rem; overflow-y: auto; padding: var(--spacing-sm)")]
    [InlineData("Capture", ".capture-sources__source", "background: var(--color-background); border: var(--border-width) solid var(--color-border); border-radius: var(--border-radius-sm); display: grid; gap: var(--spacing-sm); padding: var(--spacing-md)")]
    [InlineData("Roadmap", ".roadmap-band", "background: color-mix(in srgb, var(--color-background-alt) 82%, var(--color-background)); border: var(--border-width) solid var(--color-border); border-radius: var(--border-radius-lg); box-shadow: var(--shadow-md); display: flex; flex-direction: column; gap: var(--spacing-sm); min-height: 0; min-width: 0; overflow: hidden; padding: var(--spacing-md) var(--spacing-lg)")]
    [InlineData("Roadmap", ".roadmap-pace__value", "color: var(--color-text-secondary); flex: 0 0 auto; font-size: var(--font-size-xs); font-variant-numeric: tabular-nums; text-align: right; white-space: nowrap; width: 9ch")]
    [InlineData("Roadmap", ".roadmap-pace .roadmap-pace__measured.btn", "border-radius: var(--border-radius-sm); flex: 0 0 auto; padding: 1px var(--spacing-2xs); white-space: nowrap")]
    [InlineData("Roadmap", ".roadmap-pace__hint .info-hint__trigger", "position: absolute; right: var(--spacing-sm); top: -0.75rem; transform: translateY(-50%)")]
    [InlineData("Roadmap", ".roadmap-timeline__group-content:has(> .roadmap-pace)", "overflow: visible")]
    [InlineData("Roadmap", ".roadmap-timeline__group--slotted:has(.roadmap-pace__hint) .roadmap-timeline__group-name", "margin-right: 1.25rem")]
    [InlineData("Roadmap", ".roadmap-band__shelf .roadmap-shelf__list", "grid-template-columns: minmax(0, 1fr)")]
    public void A_moved_rule_keeps_the_declarations_it_had(string module, string selector, string declarations)
    {
        var css = File.ReadAllText(Path.Combine(Repository.Root.FullName, Find(module).File));

        Assert.Equal(declarations, Canonical(CssRules.Effective(css, selector)));
    }

    /// <summary>The narrow-window rules move too, under the same query: the
    /// Roadmap's four rules of the desktop's <c>@media (max-width: 72rem)</c>
    /// block, as it declared them.</summary>
    [Theory]
    [InlineData("Roadmap", ".roadmap-band", "padding: var(--spacing-sm) var(--spacing-md)")]
    [InlineData("Roadmap", ".roadmap-band__body--with-shelf", "grid-template-columns: minmax(0, 1fr); grid-template-rows: minmax(0, 1fr) auto")]
    [InlineData("Roadmap", ".roadmap-band__shelf", "border-left: none; border-top: var(--border-width) solid var(--color-border); max-height: 40vh; padding-left: 0; padding-top: var(--spacing-sm)")]
    [InlineData("Roadmap", ".roadmap-band__shelf .roadmap-shelf__list", "grid-template-columns: repeat(auto-fill, minmax(16rem, 1fr))")]
    public void A_moved_narrow_window_rule_keeps_the_declarations_it_had(string module, string selector, string declarations)
    {
        var css = File.ReadAllText(Path.Combine(Repository.Root.FullName, Find(module).File));
        var narrow = Regex.Match(css, @"@media \(max-width: 72rem\) \{(?<body>.*)\}\s*$", RegexOptions.Singleline);

        Assert.True(narrow.Success, $"{Find(module).File} has no @media (max-width: 72rem) block at its end.");
        Assert.Equal(declarations, Canonical(CssRules.Effective(narrow.Groups["body"].Value, selector)));
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

    private static string Canonical(IReadOnlyDictionary<string, string> declarations) =>
        string.Join("; ", declarations
            .OrderBy(declaration => declaration.Key, StringComparer.Ordinal)
            .Select(declaration => $"{declaration.Key}: {declaration.Value}"));

    /// <summary>The style rules inside each <c>@media</c> block, which
    /// <see cref="CssRules.TopLevel"/> leaves out: a module's narrow-window rules
    /// are its rules too.</summary>
    private static IEnumerable<CssRules.Rule> Conditional(string css) =>
        Regex.Matches(css, @"@media[^{]*\{")
            .SelectMany(match =>
            {
                var depth = 1;
                var end = match.Index + match.Length;
                for (; end < css.Length && depth > 0; end++)
                {
                    depth += css[end] switch { '{' => 1, '}' => -1, _ => 0 };
                }

                return CssRules.TopLevel(css[(match.Index + match.Length)..(end - 1)]);
            });
}
