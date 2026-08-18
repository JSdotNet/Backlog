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

    /// <summary>
    /// A rendered heading has to be given its weight, because it is not given one
    /// by the element it is drawn on.
    ///
    /// <para>MarkdownView draws every heading as a <c>p</c> carrying
    /// <c>role="heading"</c> — the semantics live on the role, which is a
    /// deliberate choice and not the thing under test here. The consequence is:
    /// the boldness a browser hands an <c>h1</c> never arrives, so if
    /// <c>.md-heading</c> does not state a weight then every heading in every
    /// document this product renders is the same weight as the paragraph under
    /// it. That is what had happened, and nothing caught it while headings were
    /// only ever read at a size the paragraph did not share.</para>
    ///
    /// <para>Asserted here rather than in bUnit: the markup was always correct —
    /// right class, right role, right level — and the defect was entirely in what
    /// the stylesheet did with it. A render test can only have confirmed the
    /// class was present, which it already was.</para>
    /// </summary>
    [Fact]
    public void A_heading_drawn_on_a_paragraph_is_given_the_weight_the_element_denies_it()
    {
        var view = new FileInfo(Path.Combine(
            Repository.Root.FullName,
            "src", "Core", "Backlog.UI.Components", "Markdown", "MarkdownView.razor"));

        var stylesheet = new FileInfo(Path.Combine(
            Repository.Root.FullName, "src", "Core", "Backlog.UI.Components", "wwwroot", "components.css"));

        // Asserted before the premise below, because the premise is read out of
        // these files: a path that no longer resolves would make the regex match
        // nothing, the premise read false, and this test pass while checking
        // nothing at all. A move has to fail here as a wrong path rather than
        // further down as a green run.
        Assert.True(view.Exists, $"{Relative(view)} is not where this test looks for it.");
        Assert.True(stylesheet.Exists, $"{Relative(stylesheet)} is not where this test looks for it.");

        // The premise. Were headings ever drawn as h1-h6, the browser would supply
        // the weight and this test would be asserting a rule nobody needs. Skipping
        // on that is deliberate; skipping because a file moved is not.
        var drawnOnAParagraph = Regex.IsMatch(
            File.ReadAllText(view.FullName), @"<p[^>]*class=""md-heading");

        if (!drawnOnAParagraph) return;

        var rule = Regex.Match(
            File.ReadAllText(stylesheet.FullName),
            @"^\.md-heading\s*\{(?<body>[^}]*)\}",
            RegexOptions.Multiline);

        Assert.True(rule.Success, "components.css has no .md-heading rule, so nothing styles a rendered heading.");

        Assert.True(
            Regex.IsMatch(rule.Groups["body"].Value, @"font-weight\s*:"),
            "MarkdownView draws headings as a p with role=\"heading\", so .md-heading has to declare its own "
            + "font-weight. Without one every heading renders at 400 — the same weight as the paragraph "
            + "beneath it — and a sub-header stops being findable by anyone skimming.");
    }

    /// <summary>The stylesheet and the document that specifies it. Every colour in
    /// the library is named in <c>.design/color-scheme.md</c>, and the two had
    /// drifted: the file carried a surface ramp a step darker than the one in the
    /// org style guide the stylesheet follows, so a reader of the design folder and
    /// a reader of the CSS were looking at different products.</summary>
    [Fact]
    public void Every_colour_the_library_declares_matches_the_value_in_dotdesign()
    {
        var declared = DeclaredColors(Path.Combine(
            Repository.Root.FullName, "src", "Core", "Backlog.UI.Components", "wwwroot", "components.css"));

        var specified = SpecifiedColors();

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

    /// <summary>Colour literals declared on <c>:root</c>, keyed by token name
    /// without the <c>--</c>. A token defined as <c>var(--other)</c> is skipped:
    /// it has no value of its own to disagree about.</summary>
    private static Dictionary<string, string> DeclaredColors(string path) =>
        Regex.Matches(File.ReadAllText(path), @"--((?:color|code)-[a-z0-9-]+)\s*:\s*([^;]+);")
            .Where(match => IsColorLiteral(match.Groups[2].Value))
            .ToDictionary(
                match => match.Groups[1].Value,
                match => Normalized(match.Groups[2].Value));

    /// <summary>The chapter that documents what the product deliberately is
    /// <em>not</em>: its table puts the org guide's superseded value beside the
    /// product's, so its second cell is a colour this file is declaring it does
    /// not use. Read as a declaration it would look like the file contradicting
    /// itself.</summary>
    private const string SupersededValuesChapter = "Surface and Border Deviation";

    /// <summary>The same colours as the tables in <c>color-scheme.md</c> give them.
    /// Every declaring table there puts the token in the first cell and its value
    /// in the second, so one pattern reads all of them — and a row whose second
    /// cell is not a literal (a token reference, or the per-stack mapping's CSS
    /// declaration) is not a value this can check.</summary>
    private static Dictionary<string, string> SpecifiedColors()
    {
        var markdown = File.ReadAllText(
            Path.Combine(Repository.Root.FullName, ".design", "color-scheme.md"));

        var declaring = string.Concat(
            Regex.Split(markdown, @"^(?=## )", RegexOptions.Multiline)
                .Where(chapter => !chapter.StartsWith($"## {SupersededValuesChapter}", StringComparison.Ordinal)));

        var rows = Regex.Matches(declaring, @"^\|\s*`((?:color|code)-[a-z0-9-]+)`\s*\|\s*`([^`]+)`\s*\|",
            RegexOptions.Multiline);

        var specified = new Dictionary<string, string>();

        foreach (var row in rows.Where(row => IsColorLiteral(row.Groups[2].Value)))
        {
            var token = row.Groups[1].Value;
            var value = Normalized(row.Groups[2].Value);

            // The file states most values twice, per-group and in the full
            // reference. Those two copies have to agree as well.
            Assert.True(
                !specified.TryGetValue(token, out var earlier) || earlier == value,
                $"color-scheme.md lists {token} as both {earlier} and {value}.");

            specified[token] = value;
        }

        return specified;
    }

    private static bool IsColorLiteral(string value) =>
        value.TrimStart().StartsWith('#') || value.TrimStart().StartsWith("rgb", StringComparison.OrdinalIgnoreCase);

    /// <summary>Case and the spaces inside <c>rgba(...)</c> are formatting, not
    /// value: the markdown writes the scrim tight and the CSS writes it spaced.</summary>
    private static string Normalized(string value) =>
        Regex.Replace(value, @"\s+", string.Empty).ToLowerInvariant();

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

    private static bool NotBuildOutput(FileInfo file) =>
        !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
        && !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}");

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(Repository.Root.FullName, file.FullName).Replace('\\', '/');
}
