using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// Rules for the design tokens.
///
/// <para>There is one palette, one type scale and one spacing scale, and they
/// live in the component library's <c>components.css</c>. Every host used to
/// repeat some or all of them in its own stylesheet at identical values, which
/// works right up until one copy is edited. These tests make the single
/// definition safe to rely on: the library's stylesheet has to be linked first
/// everywhere, and no host may redeclare a token the library already owns.</para>
/// </summary>
public class DesignTokenTests
{
    private const string LibraryStylesheet = "_content/Backlog.UI.Components/components.css";

    /// <summary>Tokens an application may declare for itself: names the library
    /// has no definition for, describing something about that app rather than
    /// about the design system.</summary>
    private static readonly HashSet<string> AppOwnedTokens =
    [
        "--workspace-min-width",
        "--ease-saved-flash",
        // Aliased onto the app's own name for the same measurement.
        "--pane-min-width"
    ];

    [Fact]
    public void Every_host_links_the_library_stylesheet_before_its_own()
    {
        var hosts = HostDocuments().ToList();

        Assert.NotEmpty(hosts);

        foreach (var host in hosts)
        {
            var markup = File.ReadAllText(host.FullName);

            var links = Regex.Matches(markup, @"<link[^>]*href=""([^""]+\.css)""")
                .Select(match => match.Groups[1].Value)
                .ToList();

            if (links.Count == 0) continue;

            var libraryAt = links.FindIndex(href => href.Contains(LibraryStylesheet, StringComparison.OrdinalIgnoreCase));

            Assert.True(
                libraryAt >= 0,
                $"{Relative(host)} does not link {LibraryStylesheet}. The tokens and the component "
                + "styling both come from there, so the page would render unstyled.");

            // A third-party base may legitimately load first — the mobile head
            // puts bootstrap ahead of everything so the design system overrides
            // it. What must not happen is one of ours loading first, because it
            // would be extending tokens that have not been declared yet.
            var ourStylesheetsBefore = links.Take(libraryAt)
                .Where(href => !IsVendored(href))
                .ToList();

            Assert.True(
                ourStylesheetsBefore.Count == 0,
                $"{Relative(host)} links {string.Join(", ", ourStylesheetsBefore)} before {LibraryStylesheet}. "
                + "The library has to come first: our stylesheets extend it, and cannot extend what has not "
                + "been declared yet.");
        }
    }

    /// <summary>Third-party CSS, which lands under <c>lib/</c> by convention.</summary>
    private static bool IsVendored(string href) =>
        href.Contains("lib/", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void No_application_stylesheet_redeclares_a_library_token()
    {
        var libraryTokens = DeclaredTokens(
            Path.Combine(Repository.Root.FullName, "src", "Core", "Backlog.UI.Components", "wwwroot", "components.css"));

        Assert.NotEmpty(libraryTokens);

        foreach (var stylesheet in ApplicationStylesheets())
        {
            var repeated = DeclaredTokens(stylesheet.FullName)
                .Where(libraryTokens.Contains)
                .Where(token => !AppOwnedTokens.Contains(token))
                .OrderBy(token => token)
                .ToList();

            Assert.True(
                repeated.Count == 0,
                $"{Relative(stylesheet)} redeclares tokens the library already owns, so the two can drift "
                + $"apart silently: {string.Join(", ", repeated)}");
        }
    }

    /// <summary>Bootstrap's own tokens, which the mobile head inherits from the
    /// vendored copy under <c>lib/</c>. That file is deliberately not read: keeping
    /// up with whatever a third party declares is not this suite's job. Exempting
    /// the prefix is narrower than it looks, because <c>--bs-</c> is Bootstrap's
    /// reserved namespace and nothing of ours can hide behind it.</summary>
    private const string VendorTokenPrefix = "--bs-";

    /// <summary>A token reference with no fallback has to resolve to a declaration
    /// somewhere, or the declaration holding it is invalid at computed-value time.
    /// That failure is silent in a way that makes it hard to catch by eye: nothing
    /// reaches the console, the property quietly drops to its inherited value, and
    /// where the reference sits inside <c>color-mix()</c> the entire declaration is
    /// discarded rather than just that one value. An error banner styled that way
    /// loses its border and background altogether and reads as ordinary text.
    ///
    /// <para>The comments in the library's token block record two earlier rounds of
    /// exactly this bug — a spacing step that "resolved to nothing and collapsed to
    /// zero", and three radius tokens that "had never been declared" and so lived on
    /// as raw literals. It kept recurring because nothing checked, which is what
    /// this test is for.</para>
    ///
    /// <para>Absence of a fallback is the precise rule, not merely a convenient one.
    /// <c>var(--split-pane-fixed, 50%)</c> names a value set at runtime from a Razor
    /// inline <c>style</c> or from <c>components.js</c>, so no stylesheet can declare
    /// it and the fallback is what makes the reference legal. Every deliberately
    /// undeclared token here carries one, so requiring a fallback separates the
    /// runtime-supplied tokens from the genuine typos without listing either.</para></summary>
    [Fact]
    public void No_stylesheet_uses_a_token_that_is_never_declared()
    {
        var stylesheets = ProductStylesheets().ToList();

        Assert.NotEmpty(stylesheets);

        var sources = stylesheets.ToDictionary(
            file => file,
            file => WithoutComments(File.ReadAllText(file.FullName)));

        // Anchoring on the '{' or ';' that precedes it is what separates a
        // declaration from a selector: `.button--danger:hover` also reads as
        // `--danger:` to a looser pattern, and treating that as a declaration would
        // let a misspelled token pass by matching a BEM modifier of the same name.
        var declared = sources.Values
            .SelectMany(css => Regex.Matches(css, @"[{;]\s*(--[a-z0-9-]+)\s*:")
                .Select(match => match.Groups[1].Value))
            .ToHashSet();

        Assert.NotEmpty(declared);

        // Declarations are collected from every stylesheet rather than from `:root`
        // alone, because a rule may legitimately set a token for its own subtree —
        // `--graph-explorer-status-color` is declared that way and read back by a
        // descendant.
        foreach (var (stylesheet, css) in sources)
        {
            var unresolved = Regex.Matches(css, @"var\(\s*(--[a-z0-9-]+)\s*(?<fallback>,)?")
                .Where(match => !match.Groups["fallback"].Success)
                .Select(match => match.Groups[1].Value)
                .Where(token => !token.StartsWith(VendorTokenPrefix, StringComparison.Ordinal))
                .Where(token => !declared.Contains(token))
                .Distinct()
                .OrderBy(token => token)
                .ToList();

            Assert.True(
                unresolved.Count == 0,
                $"{Relative(stylesheet)} references tokens no stylesheet declares, and gives them no "
                + "fallback: " + string.Join(", ", unresolved)
                + ". Every declaration using one is invalid at computed-value time, so it silently "
                + "loses its value — declare the token, or correct the reference to the name the "
                + "design system actually uses.");
        }
    }

    /// <summary>Tokens written at runtime under a name that is composed rather than
    /// written out, so no literal search can find the writer.
    ///
    /// <para><c>TreeView</c> builds its indent property from its own class prefix
    /// (<c>"--" + ClassPrefix + "-depth"</c>, <c>TreeView.razor</c>), so the strings
    /// <c>--folder-tree-depth</c> and <c>--knowledge-menu-depth</c> appear nowhere in
    /// the sources even though both are set on every row. These two are listed
    /// because they are undetectable in principle, not merely inconvenient to
    /// detect — anything a search *can* find must be found rather than added here.</para></summary>
    private static readonly HashSet<string> RuntimeComposedTokens =
    [
        "--folder-tree-depth",
        "--knowledge-menu-depth"
    ];

    /// <summary>A fallback makes a reference to an undeclared token legal, and
    /// <see cref="No_stylesheet_uses_a_token_that_is_never_declared"/> stops there on
    /// purpose: the fallback marks a token supplied at runtime, which no stylesheet
    /// can declare. That reasoning holds only while something actually supplies it.
    /// Where nothing does, the fallback is not a contract with the runtime but the
    /// only thing the declaration has ever used, and the token name is decoration.
    ///
    /// <para><c>--duration-fast</c> is why this test exists. It was referenced three
    /// times by <c>.task-item</c> with a <c>120ms</c> fallback and declared nowhere,
    /// so the rows animated at a duration that is not on the motion scale — the scale
    /// is <c>--transition-fast|base|slow</c>, each bundling a duration and an easing,
    /// and no <c>--duration-*</c> scale exists for it to have come from. The fallback
    /// made it read as deliberate, which is exactly how it survived review.</para>
    ///
    /// <para>This checks the premise rather than listing the exceptions, so a new
    /// runtime-supplied token needs no change here: set it from Razor or from
    /// <c>components.js</c> and the reference is legal because the writer is
    /// findable. Only <see cref="RuntimeComposedTokens"/> is listed, and only because
    /// those names are assembled at runtime.</para></summary>
    [Fact]
    public void No_stylesheet_falls_back_to_a_token_nothing_ever_sets()
    {
        var stylesheets = ProductStylesheets().ToList();

        Assert.NotEmpty(stylesheets);

        var sources = stylesheets.ToDictionary(
            file => file,
            file => WithoutComments(File.ReadAllText(file.FullName)));

        var declared = sources.Values
            .SelectMany(css => Regex.Matches(css, @"[{;]\s*(--[a-z0-9-]+)\s*:")
                .Select(match => match.Groups[1].Value))
            .ToHashSet();

        // The writers: an inline `style` attribute in Razor, `setProperty` from
        // components.js, or any other place our own code names the property. Read as
        // one body of text because the question is only whether the name occurs at
        // all — a token some C# helper assembles into a style string counts, and
        // pinning the shape of the write would reject it for no reason.
        var written = RuntimeWriters();

        Assert.Contains("--split-pane-fixed", written, StringComparer.Ordinal);

        foreach (var (stylesheet, css) in sources)
        {
            var unsettable = Regex.Matches(css, @"var\(\s*(--[a-z0-9-]+)\s*,")
                .Select(match => match.Groups[1].Value)
                .Where(token => !token.StartsWith(VendorTokenPrefix, StringComparison.Ordinal))
                .Where(token => !declared.Contains(token))
                .Where(token => !RuntimeComposedTokens.Contains(token))
                .Where(token => !written.Contains(token))
                .Distinct()
                .OrderBy(token => token, StringComparer.Ordinal)
                .ToList();

            Assert.True(
                unsettable.Count == 0,
                $"{Relative(stylesheet)} falls back on tokens that no stylesheet declares and no "
                + "code sets: " + string.Join(", ", unsettable)
                + ". The fallback is the whole value, so the token name promises a design-system "
                + "reference the product never makes — point the reference at a declared token, or "
                + "set the property at runtime.");
        }
    }

    /// <summary>Every custom property our own non-CSS sources name, which is where a
    /// runtime-supplied token is set from. Build output is skipped; so is the
    /// vendored library folder, whose scripts are not ours.</summary>
    private static HashSet<string> RuntimeWriters()
    {
        var root = new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src"));

        if (!root.Exists)
        {
            return [];
        }

        var sources = new[] { "*.razor", "*.js", "*.cs", "*.html" }
            .SelectMany(pattern => root.EnumerateFiles(pattern, SearchOption.AllDirectories))
            .Where(NotBuildOutput)
            .Where(file => !IsVendored(Relative(file)));

        return sources
            .SelectMany(file => Regex.Matches(File.ReadAllText(file.FullName), @"--[a-z0-9-]+")
                .Select(match => match.Value))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Comments are stripped before anything is read out of a stylesheet.
    /// The storybook's foundations section explains in prose that each specimen
    /// "sets the property from var(--token)", and that sentence is not a reference
    /// to a token named <c>token</c>.</summary>
    private static string WithoutComments(string css) =>
        Regex.Replace(css, @"/\*.*?\*/", " ", RegexOptions.Singleline);

    /// <summary>Every stylesheet the product ships: the component library's, and
    /// each host's own. Vendored CSS is left out — see <see cref="VendorTokenPrefix"/>.</summary>
    private static IEnumerable<FileInfo> ProductStylesheets()
    {
        var root = new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src"));

        return root.Exists
            ? root.EnumerateFiles("*.css", SearchOption.AllDirectories)
                .Where(NotBuildOutput)
                .Where(file => !IsVendored(Relative(file)))
            : [];
    }

    // The heading-weight rule used to live here, reading MarkdownView.razor for the
    // markup it is premised on. The heading moved into MarkdownBlockView when the
    // block half of the read view became its own component, and the premise went
    // with it: this fact would have kept passing while checking nothing. It now
    // lives in MarkdownHeadingWeightTests, which asks the library rather than one
    // file and fails loudly when the premise stops being readable.

    /// <summary>The stylesheet and the document that specifies it. Every colour in
    /// the library is named in <c>.design/color-scheme.md</c>, and the two had
    /// drifted: the file carried a surface ramp a step darker than the one in the
    /// org style guide the stylesheet follows, so a reader of the design folder and
    /// a reader of the CSS were looking at different products.</summary>
    [Fact]
    public void Every_colour_the_library_declares_matches_the_value_in_dotdesign()
    {
        var declared = DesignPalette.DeclaredColors(DesignPalette.LibraryStylesheet);

        var specified = DesignPalette.SpecifiedColors();

        Assert.NotEmpty(declared);
        Assert.NotEmpty(specified);

        var mismatched = specified
            .Where(entry => declared.TryGetValue(entry.Key, out var value) && value != entry.Value)
            .Select(entry => $"{entry.Key} is {declared[entry.Key]} in components.css, {entry.Value} in .design")
            .OrderBy(line => line)
            .ToList();

        var undocumented = declared.Keys
            .Where(token => !specified.ContainsKey(token))
            .OrderBy(token => token)
            .ToList();

        Assert.True(
            mismatched.Count == 0,
            "The stylesheet and .design/color-scheme.md disagree about a colour: "
            + string.Join("; ", mismatched));

        Assert.True(
            undocumented.Count == 0,
            "components.css declares colours .design/color-scheme.md does not name, so nothing says what "
            + $"they mean or what they have to contrast against: {string.Join(", ", undocumented)}");
    }

    /// <summary>Raw colour literals that were already sitting in these stylesheets
    /// when the rule below was written. This is a backlog to retire, not a standing
    /// exemption: every entry is a violation that happens to predate the test, and
    /// the list is meant to shrink to nothing. Do not add to it to make a new colour
    /// pass — declare a token instead.
    ///
    /// <para><c>Backlog.Mobile.UI</c> is the default Blazor template's stylesheet and
    /// arrives with the template's own blue-and-red palette. The mobile harness
    /// hand-rolls a dark shell instead of using the tokens, and the Storybook layout
    /// carries a red pair that predates <c>color-error-text</c>.</para></summary>
    private static readonly Dictionary<string, string[]> ToleratedColorLiterals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["src/App/Backlog.Desktop.UI/wwwroot/app.css"] = ["#020617"],
        ["src/App/Backlog.Mobile.UI/wwwroot/app.css"] =
        [
            "#fff", "#1b6ec2", "#1861ac", "#258cfb", "#26b050",
            "#e50000", "#b32121", "#929292", "#ff8080"
        ],
        ["src/Harness/Backlog.Mobile.WebHarness/wwwroot/app.css"] =
        [
            "#0e0e11", "#e6e6e6", "#16161a", "#2a2a32"
        ],
        ["src/Harness/Backlog.UI.Storybook/Components/Layout/MainLayout.razor.css"] =
        [
            "#E5484D", "#FF6369"
        ]
    };

    /// <summary>A hex colour written out in full: 3, 4, 6 or 8 digits, longest first
    /// so a six-digit value is not read as two three-digit ones.</summary>
    private static readonly Regex ColorLiteral = new(
        @"#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{4}|[0-9a-fA-F]{3})\b",
        RegexOptions.Compiled);

    /// <summary>The gap the other two tests leave open.
    /// <see cref="Every_colour_the_library_declares_matches_the_value_in_dotdesign"/>
    /// only ever reads <c>components.css</c>, so a literal written straight into a
    /// host stylesheet is in neither side of its comparison and stays invisible: the
    /// error red <c>#E4626F</c> lived in four places in the desktop stylesheet
    /// without ever being a token or appearing in <c>.design</c>. A colour in one of
    /// our own stylesheets has to be a token reference.</summary>
    [Fact]
    public void No_application_stylesheet_uses_a_raw_colour_literal()
    {
        var stylesheets = OurStylesheets().ToList();

        Assert.NotEmpty(stylesheets);

        foreach (var stylesheet in stylesheets)
        {
            var path = Relative(stylesheet);
            string[] tolerated = ToleratedColorLiterals.TryGetValue(path, out var known) ? known : [];

            var offenders = ColorLiteral.Matches(File.ReadAllText(stylesheet.FullName))
                .Select(match => match.Value)
                .Where(literal => !tolerated.Contains(literal, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(literal => literal, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"{path} paints colours as raw literals: {string.Join(", ", offenders)}. A colour has to "
                + "be a token, or nothing records what it means and nothing checks what it contrasts "
                + "against: declare it on :root in components.css, give it a row and a measured contrast "
                + "pair in .design/color-scheme.md, then reference it by name here.");
        }
    }

    /// <summary>The semantic meanings whose token is a surface and only a surface.
    /// <c>.design/color-scheme.md#semantic-soft-surface-tokens</c> is explicit that
    /// these are background tokens: something readable is rendered <em>on</em> one,
    /// and painting one as ink puts a near-black value on a near-black page.</summary>
    private static readonly string[] SemanticSurfaces = ["success", "warning", "error", "info"];

    /// <summary>A <c>color</c> declaration — the ink, not
    /// <c>background-color</c> or <c>border-color</c>, which the hyphen before the
    /// property name rules out — set to one of the semantic surface tokens. The
    /// sanctioned foregrounds escape it by name: <c>--color-error-text</c> does not
    /// end the token where <c>--color-error</c> does.
    ///
    /// <para>The token name may be followed by a comma as well as by the closing
    /// paren, because <c>var(--color-success, #1A3A22)</c> is the same defect wearing
    /// a fallback: it renders the identical 1.03:1 near-black ink, and the fallback
    /// form is already in live use in these stylesheets for other tokens, so a guard
    /// that only matched <c>)</c> would wave the regression straight through.</para></summary>
    private static readonly Regex SurfacePaintedAsInk = new(
        $@"(?<!-)\bcolor:\s*var\(--color-(?:{string.Join('|', SemanticSurfaces)})\s*[,)]",
        RegexOptions.Compiled);

    /// <summary>The counterpart to
    /// <see cref="No_application_stylesheet_uses_a_raw_colour_literal"/>: that rule
    /// catches a colour with no token, and this one catches a colour with the wrong
    /// token. Both come out of the same hole — the palette declares a semantic
    /// meaning as a surface and nothing sanctioned as its ink — so a rule that needs
    /// a legible success or error string either invents a literal or reaches for the
    /// surface token, and the second is the quieter failure: it passes every other
    /// test here while rendering at 1.49:1.
    ///
    /// <para>Reads the library's own stylesheet as well as the applications'. A
    /// component's rule has exactly the same hole under it, and the library is where
    /// a mistake would be repeated across every host at once.</para></summary>
    [Fact]
    public void No_stylesheet_paints_a_semantic_surface_token_as_ink()
    {
        var stylesheets = OurStylesheets()
            .Append(new FileInfo(DesignPalette.LibraryStylesheet))
            .ToList();

        Assert.NotEmpty(stylesheets);

        var offenders = stylesheets
            .SelectMany(stylesheet => SurfacePaintedAsInk
                .Matches(File.ReadAllText(stylesheet.FullName))
                .Select(match => $"{Relative(stylesheet)}: {match.Value}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "A semantic colour is a surface, so painting it as text puts a near-black value on a "
            + "near-black page and the line disappears. Use the sanctioned foreground for that meaning "
            + $"— --color-<meaning>-text — or add one the way color-error-text was added: {string.Join("; ", offenders)}");
    }

    /// <summary>Custom properties declared on <c>:root</c>.</summary>
    private static HashSet<string> DeclaredTokens(string path)
    {
        if (!File.Exists(path)) return [];

        var css = File.ReadAllText(path);

        return
        [
            .. Regex.Matches(css, @":root\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline)
                .SelectMany(match => Regex.Matches(match.Groups["body"].Value, @"(--[a-z0-9-]+)\s*:")
                    .Select(token => token.Groups[1].Value))
        ];
    }

    /// <summary>The root documents that decide stylesheet order: the MAUI heads'
    /// index.html and each harness's App.razor.
    ///
    /// <para>Searched across the UI folders rather than <c>src/App</c>, for the
    /// same reason as <see cref="ApplicationStylesheets"/>: a module UI project
    /// carries no host document today, and a rule that could not see one if it
    /// appeared would be relying on that staying true.</para></summary>
    private static IEnumerable<FileInfo> HostDocuments()
    {
        var roots = Repository.UserInterfaceFolders()
            .Append(new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src", "Harness")));

        foreach (var root in roots.Where(root => root.Exists))
        {
            var candidates = root.EnumerateFiles("index.html", SearchOption.AllDirectories)
                .Concat(root.EnumerateFiles("App.razor", SearchOption.AllDirectories));

            foreach (var file in candidates.Where(NotBuildOutput))
            {
                yield return file;
            }
        }
    }

    /// <summary>Every stylesheet the application side owns: all of
    /// <c>src/App</c>, plus each module's own <c>.UI</c> project.
    ///
    /// <para>This used to read <c>src/App</c> alone and covered the same files,
    /// because the desktop's contexts were folders inside
    /// <c>Backlog.Desktop.UI</c>. They are projects under <c>src/Modules</c>
    /// now, and a context that redeclared the palette in a scoped stylesheet of
    /// its own would have gone unread — which is exactly the drift this class
    /// exists to prevent, arriving through the door the split opened.</para></summary>
    private static IEnumerable<FileInfo> ApplicationStylesheets() =>
        Repository.UserInterfaceFolders()
            .SelectMany(root => root.EnumerateFiles("*.css", SearchOption.AllDirectories))
            .Where(NotBuildOutput);

    /// <summary>Every stylesheet we wrote ourselves: everything
    /// <see cref="ApplicationStylesheets"/> covers, plus the development-time
    /// harnesses, which are ours and are read by the same eyes.
    ///
    /// <para>Scoped through <see cref="Repository.UserInterfaceFolders"/> rather
    /// than by naming <c>src/App</c>, for the reason that helper exists: a
    /// module's <c>.UI</c> project is where a screen's stylesheet lives now, and
    /// a rule that reads only <c>src/App</c> would keep passing while never
    /// looking at one. A literal is easiest to write in exactly the scoped
    /// stylesheet this would not have read.</para>
    ///
    /// <para>Vendored CSS is excluded — bootstrap is full of literals and none of
    /// them are ours to fix. The library's own <c>components.css</c> is out of
    /// scope by living under <c>src/Core</c> rather than in a UI folder: it is
    /// where the literals are supposed to be.</para></summary>
    private static IEnumerable<FileInfo> OurStylesheets() =>
        Repository.UserInterfaceFolders()
            .Append(new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src", "Harness")))
            .Where(root => root.Exists)
            .SelectMany(root => root.EnumerateFiles("*.css", SearchOption.AllDirectories))
            .Where(NotBuildOutput)
            .Where(file => !IsVendored(Relative(file)));

    private static bool NotBuildOutput(FileInfo file) =>
        !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
        && !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}");

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(Repository.Root.FullName, file.FullName).Replace('\\', '/');
}
