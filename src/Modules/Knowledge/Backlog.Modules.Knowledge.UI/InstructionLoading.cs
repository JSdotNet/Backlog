using System.Text.RegularExpressions;

using Backlog.UI.Components.Markdown;

namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// How much of a host's attention one instruction file gets. Ordinal, and the
/// order is the point: the gap between the two hosts on one file is what the
/// comparison reads off, so these have to be comparable rather than merely
/// distinct.
/// </summary>
public enum InstructionReach
{
    /// <summary>The host has no path to this file at all.</summary>
    NotRead,

    /// <summary>Not loaded, but a file the host does load names its path in
    /// prose — so the host can reach it if it decides to open it. A link is not
    /// a load: whether it is followed is the model's choice on the day.</summary>
    Linked,

    /// <summary>Loaded when the path being worked on matches this file's stated
    /// scope.</summary>
    OnMatch,

    /// <summary>Loaded into every session, whatever the task.</summary>
    Always
}

/// <summary>One host's verdict on one file. <paramref name="Detail"/> is the
/// scope for <see cref="InstructionReach.OnMatch"/> and the reason for anything
/// else that has one — never a restatement of the reach itself, which the row
/// already shows.</summary>
/// <param name="Scope">The globs behind an <see cref="InstructionReach.OnMatch"/>,
/// kept as written so a path can be asked against them. <paramref name="Detail"/>
/// is the same set for a reader; this is the same set for the matcher.</param>
/// <param name="Loaded">Whether the host actually loads this file under the
/// selected paths. Always is loaded whatever is selected; On match is loaded when
/// a selected path falls in its scope; Linked and Not read never are. With
/// nothing selected nothing matches, so this reads as the baseline without a
/// nullable or a second code path.</param>
public sealed record InstructionHostReach(
    InstructionReach Reach,
    string? Detail = null,
    IReadOnlyList<string>? Scope = null,
    bool Loaded = false);

/// <summary>
/// How much of a context window one file is.
/// <para><paramref name="Bytes"/> is the file as it sits on disk, taken from the
/// discovery record rather than measured again. <paramref name="Lines"/> is
/// counted off the text.</para>
/// </summary>
public sealed record InstructionSize(int Lines, long Bytes)
{
    /// <summary>
    /// An estimate, and labelled as one everywhere it is shown. Four bytes to the
    /// token is the usual rule of thumb for English prose and it is the reason
    /// this is not called a token count: the real number depends on a tokenizer
    /// this repository does not carry, and rounding a guess does not make it a
    /// measurement.
    /// </summary>
    public long EstimatedTokens => (long)Math.Round(Bytes / 4d, MidpointRounding.AwayFromZero);

    public static InstructionSize Of(InstructionDocument document) =>
        new(CountLines(document.Content), document.SizeBytes);

    /// <summary>Newlines plus one, which is how an editor counts. An empty file
    /// is nought lines rather than one, because "one empty line" is a claim about
    /// content that is not there.</summary>
    private static int CountLines(string? content)
    {
        if (string.IsNullOrEmpty(content)) return 0;

        var lines = 1;

        foreach (var character in content)
        {
            if (character == '\n') lines++;
        }

        // A trailing newline ends the last line rather than starting another.
        return content[^1] == '\n' ? lines - 1 : lines;
    }
}

/// <summary>What one host carries, summed over the files it actually loads.</summary>
public sealed record InstructionLoadTotals(int Files, int Lines, long Bytes)
{
    public static InstructionLoadTotals None { get; } = new(0, 0, 0);

    public long EstimatedTokens => (long)Math.Round(Bytes / 4d, MidpointRounding.AwayFromZero);
}

/// <summary>
/// The whole folder read by both hosts, with what each of them ends up carrying.
/// <para>Totals rather than a column a reader adds up themselves: the question
/// behind this view is how much context an assistant spends before it has read a
/// line of code, and that is a sum, not a row.</para>
/// </summary>
public sealed record InstructionLoadingComparison(
    IReadOnlyList<InstructionLoadingRow> Rows,
    InstructionLoadTotals Claude,
    InstructionLoadTotals Copilot,
    IReadOnlyList<string> Paths)
{
    public static InstructionLoadingComparison Empty { get; } =
        new([], InstructionLoadTotals.None, InstructionLoadTotals.None, []);

    /// <summary>Whether a path selection is narrowing this reading.</summary>
    public bool IsScoped => Paths.Count > 0;
}

/// <summary>One file, read by both hosts.</summary>
public sealed record InstructionLoadingRow(
    InstructionDocument Document,
    InstructionHostReach Claude,
    InstructionHostReach Copilot,
    InstructionSize Size)
{
    /// <summary>
    /// The two hosts are more than one step apart on this file.
    /// <para>
    /// One step apart is ordinary and mostly uninteresting — a file scoped by
    /// glob for one host and read every session by the other is still read by
    /// both. Two or more is the case worth surfacing: a rule that governs the
    /// repository for one assistant and does not exist for the other. That is a
    /// drift nobody notices, because each host behaves correctly on its own
    /// side of it.
    /// </para>
    /// </summary>
    public bool IsOneSided => Math.Abs((int)Claude.Reach - (int)Copilot.Reach) > 1;
}

/// <summary>
/// Reads a discovered instruction set the way each host would.
///
/// <para>Pure, and deliberately: it is handed what
/// <see cref="InstructionSourceDiscovery"/> already read, so it opens nothing
/// itself and a test states a repository as a list of records rather than as a
/// folder on disk.</para>
///
/// <para><b>Every rule below is a claim about a product this repository does not
/// ship</b>, checked against each host's own documentation when it was written
/// and asserted in <c>InstructionLoadingTests</c>. When a host changes what it
/// loads, this class is the one place that is wrong, and the tests are what say
/// so.</para>
/// </summary>
public static class InstructionLoading
{
    /// <summary>Claude Code expands <c>@</c> imports recursively and stops at
    /// four hops. A file at the fifth is written down and not read.</summary>
    private const int MaxImportDepth = 4;

    private const string ClaudeRoot = "claude.md";
    private const string ClaudeLocalRoot = "claude.local.md";
    private const string ClaudeProjectFile = ".claude/claude.md";
    private const string ClaudeRules = ".claude/rules/";
    private const string CopilotRoot = ".github/copilot-instructions.md";
    private const string CopilotInstructions = ".github/instructions/";
    private const string InstructionSuffix = ".instructions.md";
    private const string AgentsFile = "agents.md";

    /// <summary>A fenced block, so an import written inside an example is an
    /// example.</summary>
    private static readonly Regex Fenced = new("^```.*?^```", RegexOptions.Multiline | RegexOptions.Singleline, TimeSpan.FromSeconds(1));

    /// <summary>An inline code span. <c>`@AGENTS.md`</c> in a sentence about
    /// imports is prose about an import, which is what most of the mentions in
    /// this repository's own instruction files are.</summary>
    private static readonly Regex Code = new("`[^`]*`", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static readonly Regex Import = new(@"(?<=^|\s)@([A-Za-z0-9._~\-]+(?:[/\\][A-Za-z0-9._~\-]+)*)", RegexOptions.Multiline, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Every discovered file with a verdict per host, in the order discovery
    /// returned them — which is the order the file list beside this view is in,
    /// and re-sorting it here would put the two out of step.
    /// </summary>
    public static InstructionLoadingComparison Compare(
        IReadOnlyList<InstructionDocument> documents,
        IReadOnlyList<string>? paths = null)
    {
        ArgumentNullException.ThrowIfNull(documents);

        if (documents.Count == 0) return InstructionLoadingComparison.Empty;

        var selected = Selected(paths);

        var byPath = new Dictionary<string, InstructionDocument>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in documents)
        {
            byPath[Normalize(document.RelativePath)] = document;
        }

        var claudeLoaded = ClaudeSessionFiles(documents);
        var imported = ExpandImports(claudeLoaded, byPath);
        var claudeProse = Prose(claudeLoaded, byPath);

        var copilotLoaded = CopilotSessionFiles(documents);
        var copilotProse = Prose(copilotLoaded, byPath);

        var rows = documents
            .Select(document => new InstructionLoadingRow(
                document,
                Resolve(ClaudeReach(document, claudeLoaded, imported, claudeProse), selected),
                Resolve(CopilotReach(document, copilotLoaded, copilotProse), selected),
                InstructionSize.Of(document)))
            .ToList();

        return new InstructionLoadingComparison(
            rows,
            Total(rows, row => row.Claude),
            Total(rows, row => row.Copilot),
            selected);
    }

    /// <summary>
    /// Settles whether a host loads this file under the selection.
    /// <para>An <see cref="InstructionReach.OnMatch"/> is the only verdict a
    /// selection can change, and it changes only in one direction: a path in its
    /// scope turns it on. Nothing selected leaves every conditional file off,
    /// which is exactly the baseline reading — the context a host carries before
    /// anybody opens anything.</para>
    /// </summary>
    private static InstructionHostReach Resolve(InstructionHostReach reach, IReadOnlyList<string> paths) => reach.Reach switch
    {
        InstructionReach.Always => reach with { Loaded = true },
        InstructionReach.OnMatch when reach.Scope is { Count: > 0 } scope =>
            reach with { Loaded = paths.Any(path => InstructionGlob.Matches(scope, path)) },
        _ => reach
    };

    private static InstructionLoadTotals Total(
        IReadOnlyList<InstructionLoadingRow> rows,
        Func<InstructionLoadingRow, InstructionHostReach> host)
    {
        var loaded = rows.Where(row => host(row).Loaded).ToList();

        return loaded.Count == 0
            ? InstructionLoadTotals.None
            : new InstructionLoadTotals(
                loaded.Count,
                loaded.Sum(row => row.Size.Lines),
                loaded.Sum(row => row.Size.Bytes));
    }

    /// <summary>The picked paths, normalised and deduplicated. A blank entry is
    /// not a path and would match nothing anyway; dropping it here keeps the
    /// scoped and unscoped readings from differing over whitespace.</summary>
    private static IReadOnlyList<string> Selected(IReadOnlyList<string>? paths) =>
    [
        .. (paths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
    ];

    /// <summary>The files Claude Code reads at session start before anything is
    /// imported: the project file wherever it is allowed to live, and the rules
    /// that state no <c>paths</c> to scope themselves by.</summary>
    private static HashSet<string> ClaudeSessionFiles(IReadOnlyList<InstructionDocument> documents)
    {
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in documents)
        {
            var path = Normalize(document.RelativePath);

            if (IsClaudeProjectFile(path) || (IsRule(path) && Paths(document).Count == 0))
            {
                loaded.Add(path);
            }
        }

        return loaded;
    }

    private static HashSet<string> CopilotSessionFiles(IReadOnlyList<InstructionDocument> documents)
    {
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in documents)
        {
            var path = Normalize(document.RelativePath);

            if (path.Equals(CopilotRoot, StringComparison.OrdinalIgnoreCase) ||
                (IsPathSpecific(path) && IsRepositoryWide(Globs(document))))
            {
                loaded.Add(path);
            }
        }

        return loaded;
    }

    /// <summary>
    /// Follows <c>@</c> imports out of the files already loaded, four hops deep,
    /// adding what they reach to that same set.
    /// <para>Returns the files that were pulled in this way rather than stated
    /// outright, because the two are worth telling apart on screen even though
    /// the host reads both the same: one is a decision somebody made about this
    /// repository, and the other is a consequence of it.</para>
    /// </summary>
    private static HashSet<string> ExpandImports(HashSet<string> loaded, IReadOnlyDictionary<string, InstructionDocument> byPath)
    {
        var imported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frontier = loaded.ToList();

        for (var hop = 0; hop < MaxImportDepth && frontier.Count > 0; hop++)
        {
            var next = new List<string>();

            foreach (var path in frontier)
            {
                if (!byPath.TryGetValue(path, out var document)) continue;

                foreach (var target in ImportsFrom(document, byPath))
                {
                    if (!loaded.Add(target)) continue;

                    imported.Add(target);
                    next.Add(target);
                }
            }

            frontier = next;
        }

        return imported;
    }

    private static IEnumerable<string> ImportsFrom(InstructionDocument document, IReadOnlyDictionary<string, InstructionDocument> byPath)
    {
        var prose = Strip(document.Content);
        var directory = Directory(Normalize(document.RelativePath));

        foreach (Match match in Import.Matches(prose))
        {
            var reference = Normalize(match.Groups[1].Value);

            // Relative to the importing file first, which is the rule, then as
            // written — a root-relative import from a root file is both, and a
            // home-relative one is neither and is somebody else's repository.
            var candidates = directory.Length == 0
                ? new[] { reference }
                : [$"{directory}/{reference}", reference];

            foreach (var candidate in candidates)
            {
                if (byPath.ContainsKey(candidate))
                {
                    yield return candidate;
                    break;
                }
            }
        }
    }

    /// <summary>The text of every file the host loads, as one body to search for
    /// mentions of the files it does not.</summary>
    private static string Prose(IEnumerable<string> loaded, IReadOnlyDictionary<string, InstructionDocument> byPath) =>
        string.Join('\n', loaded.Select(path => byPath.TryGetValue(path, out var document) ? document.Content : string.Empty));

    private static InstructionHostReach ClaudeReach(
        InstructionDocument document,
        HashSet<string> loaded,
        HashSet<string> imported,
        string prose)
    {
        var path = Normalize(document.RelativePath);

        if (loaded.Contains(path))
        {
            return new InstructionHostReach(
                InstructionReach.Always,
                imported.Contains(path) ? "imported" : null);
        }

        if (IsRule(path) && Paths(document) is { Count: > 0 } scopes)
        {
            return new InstructionHostReach(InstructionReach.OnMatch, string.Join(", ", scopes), scopes);
        }

        // A project file below the root is read when something in its subtree is,
        // which is the same shape as a glob and reads better as one.
        if (FileName(path).Equals(ClaudeRoot, StringComparison.OrdinalIgnoreCase))
        {
            var subtree = Subtree(path);

            return new InstructionHostReach(InstructionReach.OnMatch, subtree, [subtree]);
        }

        return Mentioned(path, prose)
            ? new InstructionHostReach(InstructionReach.Linked)
            : new InstructionHostReach(InstructionReach.NotRead);
    }

    private static InstructionHostReach CopilotReach(InstructionDocument document, HashSet<string> loaded, string prose)
    {
        var path = Normalize(document.RelativePath);

        if (loaded.Contains(path)) return new InstructionHostReach(InstructionReach.Always);

        if (IsPathSpecific(path))
        {
            var globs = Globs(document);

            return new InstructionHostReach(InstructionReach.OnMatch, string.Join(", ", globs), globs);
        }

        if (FileName(path).Equals(AgentsFile, StringComparison.OrdinalIgnoreCase))
        {
            var subtree = Subtree(path);

            return new InstructionHostReach(InstructionReach.OnMatch, subtree, [subtree]);
        }

        return Mentioned(path, prose)
            ? new InstructionHostReach(InstructionReach.Linked)
            : new InstructionHostReach(InstructionReach.NotRead);
    }

    /// <summary>
    /// Whether a loaded file names this path in its text, in either separator
    /// spelling. A substring rather than a parse: the mentions in this
    /// repository are in prose, in backticks, in link targets and in list items,
    /// and every one of them is a reader being pointed at the file.
    /// </summary>
    private static bool Mentioned(string path, string prose) =>
        prose.Contains(path, StringComparison.OrdinalIgnoreCase) ||
        prose.Contains(path.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);

    private static bool IsClaudeProjectFile(string path) =>
        path.Equals(ClaudeRoot, StringComparison.OrdinalIgnoreCase) ||
        path.Equals(ClaudeLocalRoot, StringComparison.OrdinalIgnoreCase) ||
        path.Equals(ClaudeProjectFile, StringComparison.OrdinalIgnoreCase);

    private static bool IsRule(string path) =>
        path.StartsWith(ClaudeRules, StringComparison.OrdinalIgnoreCase);

    private static bool IsPathSpecific(string path) =>
        path.StartsWith(CopilotInstructions, StringComparison.OrdinalIgnoreCase) &&
        path.EndsWith(InstructionSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A path-specific file that states no scope, or states the everything glob,
    /// is read every time. Both spellings mean the same thing to the host and
    /// this repository writes the second one.
    /// </summary>
    private static bool IsRepositoryWide(IReadOnlyList<string> globs) =>
        globs.Count == 0 || globs.Any(glob => glob is "**" or "**/*");

    private static IReadOnlyList<string> Globs(InstructionDocument document) =>
        MarkdownFrontmatter.Read(document.Content).ApplyTo;

    /// <summary>
    /// The <c>paths</c> a rule scopes itself by. Read out of the frontmatter's
    /// remaining fields rather than through a second parser — the shared reader
    /// lifts only <c>description</c>, <c>applyTo</c> and <c>tools</c> by name
    /// and hands everything else back as it was written. The comma split is this
    /// field's own, for the same reason <c>applyTo</c> has one.
    /// </summary>
    private static IReadOnlyList<string> Paths(InstructionDocument document) =>
    [
        .. MarkdownFrontmatter.Read(document.Content).Other
            .Where(field => field.Key.Equals("paths", StringComparison.OrdinalIgnoreCase))
            .SelectMany(field => field.Values)
            .SelectMany(value => value.Split(','))
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
    ];

    /// <summary>Removes what markdown says is not to be read as markup, so an
    /// import inside an example stays an example.</summary>
    private static string Strip(string? content) =>
        content is null ? string.Empty : Code.Replace(Fenced.Replace(content, " "), " ");

    /// <summary>One separator and one spelling of "here". The rules are written
    /// in forward slashes; discovery hands back whichever separator the file
    /// system uses, which on Windows is the other one.</summary>
    private static string Normalize(string path)
    {
        var normalized = path.Replace('\\', '/');

        return normalized.StartsWith("./", StringComparison.Ordinal) ? normalized[2..] : normalized;
    }

    private static string Directory(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? string.Empty : path[..separator];
    }

    private static string FileName(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? path : path[(separator + 1)..];
    }

    /// <summary>The subtree a directory-scoped file governs, written as the glob
    /// it amounts to so it reads beside the stated ones.</summary>
    private static string Subtree(string path)
    {
        var directory = Directory(path);
        return directory.Length == 0 ? "**" : $"{directory}/**";
    }
}
