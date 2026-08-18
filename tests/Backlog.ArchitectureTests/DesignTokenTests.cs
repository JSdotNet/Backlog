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
            Path.Combine(Repository.Root.FullName, "src", "UI", "Backlog.UI.Components", "wwwroot", "components.css"));

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
    /// <c>var(--split-pane-start, 50%)</c> names a value set at runtime from a Razor
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

    /// <summary>The stylesheet and the document that specifies it. Every colour in
    /// the library is named in <c>.design/color-scheme.md</c>, and the two had
    /// drifted: the file carried a surface ramp a step darker than the one in the
    /// org style guide the stylesheet follows, so a reader of the design folder and
    /// a reader of the CSS were looking at different products.</summary>
    [Fact]
    public void Every_colour_the_library_declares_matches_the_value_in_dotdesign()
    {
        var declared = DeclaredColors(Path.Combine(
            Repository.Root.FullName, "src", "UI", "Backlog.UI.Components", "wwwroot", "components.css"));

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
    /// index.html and each harness's App.razor.</summary>
    private static IEnumerable<FileInfo> HostDocuments()
    {
        foreach (var folder in new[] { "App", "Harness" })
        {
            var root = new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src", folder));
            if (!root.Exists) continue;

            var candidates = root.EnumerateFiles("index.html", SearchOption.AllDirectories)
                .Concat(root.EnumerateFiles("App.razor", SearchOption.AllDirectories));

            foreach (var file in candidates.Where(NotBuildOutput))
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<FileInfo> ApplicationStylesheets()
    {
        var root = new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src", "App"));

        return root.Exists
            ? root.EnumerateFiles("*.css", SearchOption.AllDirectories).Where(NotBuildOutput)
            : [];
    }

    private static bool NotBuildOutput(FileInfo file) =>
        !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
        && !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}");

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(Repository.Root.FullName, file.FullName).Replace('\\', '/');
}
