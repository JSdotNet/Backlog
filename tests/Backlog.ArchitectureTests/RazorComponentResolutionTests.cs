using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// Two ways a Razor file can be wrong that neither the compiler nor bUnit catches,
/// both of which shipped a broken pane before these rules existed.
/// <para>
/// Razor has no notion of "unknown component". A tag it cannot resolve is markup, so
/// a missing <c>@using</c> turns <c>&lt;SplitPane&gt;</c> into a literal
/// <c>&lt;splitpane&gt;</c> element — an inline element with no styles, which is how
/// a side-by-side layout silently became a vertical stack. It builds with no warning,
/// and bUnit renders the same nonsense without complaining, so a test that asserts on
/// CSS text or on inner content passes while the layout is broken.
/// </para>
/// <para>
/// A Razor comment written inside an attribute list is read as an attribute NAME. On a
/// component that captures unmatched values it travels all the way to the browser,
/// where <c>setAttribute</c> rejects it with InvalidCharacterError and takes the whole
/// Blazor circuit down with it — the app stops responding to everything. bUnit's DOM
/// accepts attribute names a browser never would, so again the tests stay green.
/// </para>
/// </summary>
public class RazorComponentResolutionTests
{
    [Fact]
    public void Every_library_component_a_screen_uses_is_imported()
    {
        var library = LibraryComponents();
        Assert.NotEmpty(library);

        var offenders = new List<string>();

        foreach (var file in RazorFiles())
        {
            var imported = ImportedNamespaces(file);
            var text = File.ReadAllText(file.FullName);

            foreach (var name in UsedTagNames(text))
            {
                if (!library.TryGetValue(name, out var required)) continue;
                if (imported.Contains(required)) continue;

                offenders.Add($"{Relative(file)}: <{name}> needs @using {required}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These components are not imported, so Razor renders them as unknown HTML "
            + "elements rather than components:\n  " + string.Join("\n  ", offenders.Distinct()));
    }

    [Fact]
    public void No_razor_comment_sits_inside_an_attribute_list()
    {
        var offenders = new List<string>();

        foreach (var file in RazorFiles())
        {
            foreach (var line in CommentsInsideAttributeLists(File.ReadAllText(file.FullName)))
            {
                offenders.Add($"{Relative(file)}:{line}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A Razor comment in an attribute list becomes an attribute name and throws "
            + "InvalidCharacterError in the browser. Move it above the tag:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>Component name to the namespace its folder implies, for everything the
    /// shared library ships. Underscore-prefixed files are Razor infrastructure
    /// (<c>_Imports</c>), never components.</summary>
    private static Dictionary<string, string> LibraryComponents()
    {
        var root = new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src", "Core", "Backlog.UI.Components"));
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in root.EnumerateFiles("*.razor", SearchOption.AllDirectories))
        {
            if (file.Name.StartsWith('_')) continue;
            if (IsGenerated(file)) continue;

            var folder = Path.GetRelativePath(root.FullName, file.Directory!.FullName);
            var suffix = folder == "." ? string.Empty : "." + folder.Replace(Path.DirectorySeparatorChar, '.');

            map[Path.GetFileNameWithoutExtension(file.Name)] = "Backlog.UI.Components" + suffix;
        }

        return map;
    }

    /// <summary>Every Razor file that composes a screen or a story — the app, the
    /// modules, and the harnesses that host them. The harnesses are included on
    /// purpose: a storybook page that renders a component as raw HTML is a page that
    /// reviews the wrong thing.</summary>
    private static IEnumerable<FileInfo> RazorFiles() =>
        new[] { "App", "Modules", "Harness" }
            .Select(area => new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src", area)))
            .Where(area => area.Exists)
            .SelectMany(area => area.EnumerateFiles("*.razor", SearchOption.AllDirectories))
            .Where(file => !IsGenerated(file) && !file.Name.StartsWith('_'));

    private static bool IsGenerated(FileInfo file) =>
        file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
        || file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");

    /// <summary>
    /// The namespaces in scope for a file: the ones it imports itself, plus every
    /// <c>_Imports.razor</c> from its own folder up to the repository root, because
    /// Razor applies those cumulatively.
    /// <para>
    /// The file's own directives count as much as an inherited one, and reading only
    /// the inherited half reported a page that imports what it needs as if it did
    /// not. The settings screen is the case that proves it: its folder cannot carry
    /// an <c>_Imports.razor</c> at all — the generated class would shadow the
    /// component the page declares, which does not compile — so the two lines such a
    /// file would have held are written on the page instead.
    /// </para>
    /// </summary>
    private static HashSet<string> ImportedNamespaces(FileInfo file)
    {
        var namespaces = new HashSet<string>(StringComparer.Ordinal);

        AddUsings(namespaces, file);

        for (var folder = file.Directory; folder is not null; folder = folder.Parent)
        {
            AddUsings(namespaces, new FileInfo(Path.Combine(folder.FullName, "_Imports.razor")));

            if (string.Equals(folder.FullName, Repository.Root.FullName, StringComparison.OrdinalIgnoreCase)) break;
        }

        return namespaces;
    }

    /// <summary>
    /// Every namespace one file imports.
    /// <para>
    /// Anchored to the start of a line because a Razor directive is only a directive
    /// there — a storybook page holding <c>@using</c> inside sample markup is showing
    /// the words, not importing anything, and counting those would quietly excuse a
    /// real omission.
    /// </para>
    /// <para>
    /// global:: is optional and carries no meaning for this rule. Several modules
    /// write every import as <c>@using global::Backlog.Something</c> so a bare
    /// "Backlog" cannot bind to Backlog.Modules; reading the name without stripping
    /// the prefix saw "global" and reported every one of those imports as missing.
    /// </para>
    /// </summary>
    private static void AddUsings(HashSet<string> namespaces, FileInfo file)
    {
        if (!file.Exists) return;

        foreach (Match match in Regex.Matches(
                     File.ReadAllText(file.FullName),
                     @"(?m)^\s*@using\s+(?:global::)?([A-Za-z0-9_.]+)"))
        {
            namespaces.Add(match.Groups[1].Value);
        }
    }

    /// <summary>Capitalised tag names, which is what a component usage looks like.
    /// Lowercase tags are HTML and resolve without an import.</summary>
    private static IEnumerable<string> UsedTagNames(string text) =>
        Regex.Matches(text, @"<([A-Z][A-Za-z0-9]*)").Select(match => match.Groups[1].Value).Distinct();

    /// <summary>
    /// The 1-based line of every <c>@*</c> that opens while a tag's attribute list is
    /// still open.
    /// <para>
    /// Quoted attribute values are skipped whole, so neither a lambda arrow nor a
    /// <c>&gt;</c> inside one reads as the end of the tag. C# raw string literals are
    /// skipped too: a storybook page holds sample markup as text, and a comment inside
    /// that sample is documentation rather than something Razor will ever parse.
    /// </para>
    /// </summary>
    private static IEnumerable<int> CommentsInsideAttributeLists(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var inTag = false;
        var inComment = false;
        var inRawString = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];

            for (var position = 0; position < line.Length;)
            {
                if (inComment)
                {
                    var close = line.IndexOf("*@", position, StringComparison.Ordinal);
                    if (close < 0) break;

                    inComment = false;
                    position = close + 2;
                    continue;
                }

                if (line.AsSpan(position).StartsWith("\"\"\""))
                {
                    inRawString = !inRawString;
                    position += 3;
                    continue;
                }

                if (inRawString) break;

                if (line.AsSpan(position).StartsWith("@*"))
                {
                    if (inTag) yield return index + 1;

                    inComment = true;
                    position += 2;
                    continue;
                }

                if (!inTag)
                {
                    var opening = Regex.Match(line[position..], @"^<([A-Za-z][A-Za-z0-9]*)");
                    if (opening.Success)
                    {
                        inTag = true;
                        position += opening.Length;
                        continue;
                    }

                    position++;
                    continue;
                }

                var character = line[position];

                if (character is '"' or '\'')
                {
                    var closingQuote = line.IndexOf(character, position + 1);
                    position = closingQuote < 0 ? line.Length : closingQuote + 1;
                    continue;
                }

                // => is a lambda, not the end of the tag.
                if (character == '>' && position > 0 && line[position - 1] == '=')
                {
                    position++;
                    continue;
                }

                if (character == '>') inTag = false;

                position++;
            }
        }
    }

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(Repository.Root.FullName, file.FullName).Replace('\\', '/');
}
