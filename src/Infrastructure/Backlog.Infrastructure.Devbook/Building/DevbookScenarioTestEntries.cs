using System.Text;
using System.Text.RegularExpressions;

namespace Backlog.Infrastructure.Devbook.Building;

/// <summary>
/// The <c>tests</c> entries the generator derives rather than reads: one
/// <c>e2e:playwright:&lt;spec&gt;#&lt;part&gt;</c> per <c>#### Scenario:</c> case of a
/// <c>requirement</c> chapter whose <c>Proved by:</c> line names a part of a
/// journey that a spec in the repository implements, so nobody writes that
/// entry by hand.
///
/// <para>A port of the <c>Proved by:</c> half of <c>scenarioProblems</c>, with
/// <c>journeysOf</c>, <c>resolveFile</c> and <c>specIndex</c>, in the devbook
/// plugin's <c>scenario.mjs</c>, and of <c>provedByCases</c> and
/// <c>isScenariosFile</c> in its <c>metadata.mjs</c>. Only the derivation is
/// ported: the problems the same pass reports, and the register
/// <c>scenarios.json</c> is written from, reach no column the app writes
/// (local ADR 0015).</para>
///
/// <para>A journey is a <c>##</c> chapter typed <c>scenario</c> in a scenarios
/// file, addressed <c>&lt;path&gt;#&lt;slug&gt;</c>; its <c>###</c> headings are
/// its parts. A spec implements a journey by naming it in a
/// <c>// scenario: &lt;journey&gt;</c> line among its first ten. The repository is
/// walked for specs only once a pointer resolves to a part, as there, so a
/// corpus with no journeys pays nothing.</para>
/// </summary>
internal static partial class DevbookScenarioTestEntries
{
    private const string DevbookPrefix = ".devbook/";
    private const string DomainPrefix = DevbookPrefix + "domain/";
    private const string ScenarioType = "scenario";
    private const string DerivedRunner = "playwright";
    private const int SpecHeaderLines = 10;

    private static readonly HashSet<string> SpecSkipDirectories =
        new(["node_modules", "bin", "obj", "dist", "build", "out", "coverage"], StringComparer.Ordinal);

    /// <summary>JavaScript's line terminators, which its <c>^</c> and <c>$</c>
    /// stop at under the <c>m</c> flag.</summary>
    private const string JsLineTerminators = "\\n\\r\\u2028\\u2029";

    [GeneratedRegex(@"\.(?:spec|test)\.[cm]?[jt]sx?\z")]
    private static partial Regex SpecFile();

    // `/^\s*\/\/\s*scenario:\s*(\S+)\s*$/m`, with JavaScript's whitespace and
    // line boundaries.
    [GeneratedRegex(
        "(?:^|(?<=[" + JsLineTerminators + "]))[" + DevbookMarkdown.JsWhitespaceClass + "]*//[" + DevbookMarkdown.JsWhitespaceClass + "]*scenario:["
        + DevbookMarkdown.JsWhitespaceClass + "]*([^" + DevbookMarkdown.JsWhitespaceClass + "]+)[" + DevbookMarkdown.JsWhitespaceClass + "]*(?=[" + JsLineTerminators + "]|\\z)")]
    private static partial Regex SpecHeader();

    // `/^Scenario:/i`: JavaScript's case-insensitive match without the `u` flag
    // folds no non-ASCII letter onto these, so the classes are spelled out.
    [GeneratedRegex("^[Ss][Cc][Ee][Nn][Aa][Rr][Ii][Oo]:")]
    private static partial Regex ScenarioHeading();

    // `/^\s*Proved by:\s*(.*?)\s*$/`: `.` stops at a line terminator, and `$`
    // without the `m` flag is the end of the line alone.
    [GeneratedRegex(
        "^[" + DevbookMarkdown.JsWhitespaceClass + "]*Proved by:[" + DevbookMarkdown.JsWhitespaceClass + "]*([^" + JsLineTerminators + "]*?)["
        + DevbookMarkdown.JsWhitespaceClass + "]*\\z")]
    private static partial Regex ProvedByLine();

    /// <summary>
    /// The entries derived for each requirement, by its node id, each list in
    /// discovery order and without repeats. Empty when the corpus has no
    /// scenarios file, without reading anything.
    /// </summary>
    public static IReadOnlyDictionary<string, List<string>> Derive(
        string repositoryRoot,
        DevbookBuildLayout layout,
        IReadOnlyList<DevbookC4Graph.Document> documents)
    {
        var derived = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        var files = new Dictionary<string, List<Journey>>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            if (IsScenariosFile(document.RelativePath)) files[document.RelativePath] = JourneysOf(document);
        }

        // No scenarios file, nothing a pointer could resolve to.
        if (files.Count == 0) return derived;

        IReadOnlyDictionary<string, string>? specs = null;
        foreach (var document in documents)
        {
            var folder = layout.ConventionFolderForPath(document.RelativePath);
            foreach (var (requirement, pointers) in ProvedByCases(document, folder))
            {
                foreach (var target in pointers)
                {
                    // A malformed pointer, an unresolved file or part: the
                    // checker reports each, and none derives an entry.
                    var hash = target.IndexOf('#', StringComparison.Ordinal);
                    if (hash <= 0) continue;

                    var filePart = target[..hash];
                    var part = target[(hash + 1)..];
                    if (!filePart.EndsWith(".md", StringComparison.Ordinal)) continue;

                    var resolved = filePart.StartsWith(DevbookPrefix, StringComparison.Ordinal)
                        ? filePart
                        : PosixJoin(PosixDirectoryName(document.RelativePath), filePart);
                    if (!files.TryGetValue(resolved, out var journeys)) continue;

                    var journey = journeys.Find(candidate => candidate.Parts.ContainsKey(part));
                    if (journey is null) continue;

                    specs ??= SpecIndex(repositoryRoot);
                    if (!specs.TryGetValue(journey.Id, out var spec)) continue;

                    var entry = $"e2e:{DerivedRunner}:{spec}#{journey.Parts[part]}";
                    if (!derived.TryGetValue(requirement, out var list)) derived[requirement] = list = [];
                    if (!list.Contains(entry, StringComparer.Ordinal)) list.Add(entry);
                }
            }
        }

        return derived;
    }

    /// <summary>
    /// <c>isScenariosFile</c>: <c>scenarios.md</c>, a split
    /// <c>scenarios.&lt;name&gt;.md</c>, or its subpage
    /// <c>scenarios.&lt;name&gt;.&lt;page&gt;.md</c>, directly in a bounded context
    /// folder of <c>.devbook/domain/</c>.
    /// </summary>
    private static bool IsScenariosFile(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        if (!normalized.StartsWith(DomainPrefix, StringComparison.Ordinal)) return false;

        var segments = normalized[DomainPrefix.Length..].Split('/');
        if (segments.Length != 2) return false;

        // domainFileName: an `.invariants` subpage has a page, and so is none.
        var name = segments[1].EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? segments[1][..^3] : segments[1];
        var parts = name.Split('.');
        var invariantsPage = parts.Length > 1 && parts[^1] == "invariants";

        return !invariantsPage && parts[0] == "scenarios" && segments[1].Split('.').Length <= 4;
    }

    /// <summary><c>journeysOf</c>: each <c>##</c> typed <c>scenario</c>, with the
    /// <c>###</c> parts under it by slug, the first heading of a slug kept.</summary>
    private static List<Journey> JourneysOf(DevbookC4Graph.Document document)
    {
        var journeys = new List<Journey>();
        Journey? journey = null;

        foreach (var chapter in document.Chapters)
        {
            if (chapter.Level == 2)
            {
                journey = chapter.Meta is not null && DevbookBuildLayout.ResolveType("domain", chapter.Meta) is ScenarioType
                    ? new Journey($"{document.RelativePath}#{chapter.Slug}")
                    : null;
                if (journey is not null) journeys.Add(journey);
                continue;
            }

            if (chapter.Level == 3 && journey is not null) journey.Parts.TryAdd(chapter.Slug, chapter.Text);
        }

        return journeys;
    }

    /// <summary>
    /// <c>provedByCases</c>, the pointers alone: for each <c>#### Scenario:</c> one
    /// level below a <c>requirement</c> chapter, the requirement's node id and the
    /// target of every <c>Proved by:</c> line in the case's body outside a fence —
    /// the generator's looser fence rule, as <see cref="DevbookMarkdown"/> reads
    /// headings by, not <see cref="DevbookChapterText.FenceMask"/>'s.
    /// </summary>
    private static IEnumerable<(string Requirement, List<string> Pointers)> ProvedByCases(DevbookC4Graph.Document document, string? folder)
    {
        var chapters = document.Chapters;
        string[]? lines = null;

        for (var index = 0; index < chapters.Count; index++)
        {
            var rule = chapters[index];
            if (rule.Meta is null || DevbookBuildLayout.ResolveType(folder, rule.Meta) is not "requirement") continue;

            lines ??= DevbookMarkdown.Lines(document.Markdown);
            for (var i = index + 1; i < chapters.Count && chapters[i].Level > rule.Level; i++)
            {
                var heading = chapters[i];
                if (heading.Level != rule.Level + 1 || !ScenarioHeading().IsMatch(heading.Text)) continue;

                var end = i + 1 < chapters.Count ? chapters[i + 1].Line : lines.Length + 1;
                var pointers = new List<string>();
                string? fence = null;

                for (var n = heading.Line; n < end - 1; n++)
                {
                    var marker = DevbookMarkdown.FenceMarker().Match(lines[n]);
                    if (fence is not null)
                    {
                        var closer = marker.Groups[1].Value;
                        if (marker.Success && closer[0] == fence[0] && closer.Length >= fence.Length) fence = null;
                        continue;
                    }

                    if (marker.Success)
                    {
                        fence = marker.Groups[1].Value;
                        continue;
                    }

                    var proved = ProvedByLine().Match(lines[n]);
                    if (proved.Success) pointers.Add(proved.Groups[1].Value);
                }

                yield return ($"{document.RelativePath}#{rule.Slug}", pointers);
            }
        }
    }

    /// <summary>
    /// <c>specIndex</c>: every spec in the repository that names the journey it
    /// implements in its first lines, by the journey's id; where two name one,
    /// the first in path order. Dot-folders and build output are not walked, nor
    /// is any link, as Node's <c>Dirent</c> sees none as a file or a folder.
    /// </summary>
    private static Dictionary<string, string> SpecIndex(string repositoryRoot)
    {
        var found = new List<string>();
        var pending = new Stack<string>();
        pending.Push(string.Empty);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var (name, isDirectory) in DevbookBuildFiles.Entries(repositoryRoot, current))
            {
                var child = current.Length == 0 ? name : $"{current}/{name}";
                if (isDirectory)
                {
                    if (!name.StartsWith('.') && !SpecSkipDirectories.Contains(name)) pending.Push(child);
                }
                else if (SpecFile().IsMatch(name))
                {
                    found.Add(child);
                }
            }
        }

        // JavaScript's default sort: by UTF-16 code unit.
        found.Sort(StringComparer.Ordinal);

        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var spec in found)
        {
            string text;
            try
            {
                text = DevbookBuildFiles.ReadText(repositoryRoot, spec);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var head = string.Join('\n', DevbookMarkdown.Lines(text).Take(SpecHeaderLines));
            var match = SpecHeader().Match(head);
            if (match.Success) index.TryAdd(match.Groups[1].Value, spec);
        }

        return index;
    }

    /// <summary><c>path.posix.dirname</c> of a document's repository path, which
    /// has no trailing slash.</summary>
    private static string PosixDirectoryName(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash switch
        {
            < 0 => ".",
            0 => "/",
            _ => path[..slash]
        };
    }

    /// <summary><c>path.posix.join</c> of a folder and a file name ending
    /// <c>.md</c>: joined with a slash, then normalized — empty and <c>.</c>
    /// segments dropped, <c>..</c> taking the one before it, or kept above a
    /// relative root.</summary>
    private static string PosixJoin(string directory, string file)
    {
        var joined = $"{directory}/{file}";
        var absolute = joined.StartsWith('/');

        var kept = new List<string>();
        foreach (var segment in joined.Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..")
            {
                if (kept.Count > 0 && kept[^1] != "..") kept.RemoveAt(kept.Count - 1);
                else if (!absolute) kept.Add("..");
                continue;
            }

            kept.Add(segment);
        }

        var normalized = new StringBuilder(absolute ? "/" : string.Empty).AppendJoin('/', kept);
        if (normalized.Length == 0) normalized.Append('.');
        return normalized.ToString();
    }

    /// <summary>One journey: its <c>&lt;path&gt;#&lt;slug&gt;</c> id and its parts,
    /// heading text by slug.</summary>
    private sealed class Journey(string id)
    {
        public string Id { get; } = id;

        public Dictionary<string, string> Parts { get; } = new(StringComparer.Ordinal);
    }
}
