using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Knowledge.Abstractions;
using Backlog.SharedKernel;
using Backlog.UI.Components.Diagrams;

namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// Answers <see cref="IDiagramArtifactSource"/> from the repository clone the
/// knowledge chapters are read out of.
/// <para>
/// Everything the library deliberately does not know is here: whether the feature
/// is switched on, which clone the chapters came from, where <c>_archify/</c> sits
/// beside each chapter, and whether an agent CLI is installed. The library asks a
/// diagram's source and gets back what exists for it; this is where that becomes
/// a question about files.
/// </para>
/// <para>
/// It also rebuilds, in C#, the half of <c>tools/diagrams/archify-artifacts.mjs</c>
/// that reads chapters — the fence scan and the hash. Not because duplicating it
/// is pleasant, but because a running app cannot shell out to Node to answer a
/// question asked once per diagram per render. The two must agree exactly, and
/// the unit tests pin the hash on both sides.
/// </para>
/// </summary>
public sealed class ArchifyDiagramArtifacts : IDiagramArtifactSource, IDisposable
{
    /// <summary>The folder beside a chapter that holds its specifications, its
    /// artifacts and the index tying them to fences.</summary>
    private const string ArtifactDirectory = "_archify";

    private const string IndexFile = "index.json";

    /// <summary>The knowledge folders whose chapters carry diagrams. The
    /// instructions folder is left out on purpose: it is a set of rules for
    /// agents rather than a set of chapters, and nothing in it is drawn.</summary>
    private static readonly string[] DiagramFolderKeys = [".domain", ".arc42", ".tech", ".design"];

    /// <summary>Mirrors the fence scanner in the generator: an opening fence of
    /// three or more backticks or tildes, its indent, and its info string.</summary>
    private static readonly Regex FencePattern = new(@"^(\s*)(`{3,}|~{3,})\s*([^\s`~]*)", RegexOptions.Compiled);

    /// <summary>A specification filename, <c>&lt;chapter&gt;.&lt;ordinal&gt;.&lt;type&gt;.json</c>
    /// or <c>&lt;chapter&gt;.&lt;ordinal&gt;.&lt;type&gt;.standard.json</c> — chapter, ordinal,
    /// type and the optional quality opt-out. The generator's <c>SPEC_NAME</c> is
    /// the same expression; everything the render step needs is in the name, so
    /// this is the only place either side has to read.</summary>
    private static readonly Regex SpecNamePattern = new(@"^(.+)\.(\d+)\.([a-z]+)(?:\.([a-z]+))?\.json$", RegexOptions.Compiled);

    /// <summary>The Archify quality profile a specification renders at when its
    /// filename says nothing. The other one, <c>standard</c>, is named in the
    /// filename by the handful of diagrams that provably cannot pass showcase —
    /// a non-planar graph forces an edge crossing in every possible drawing, and
    /// showcase treats a crossing as an error.</summary>
    private const string ShowcaseQuality = "showcase";

    private const string StandardQuality = "standard";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IAppFeatureSettings _features;
    private readonly IKnowledgeFolderSource _folders;
    private readonly GitHubSettingsStore _repositories;
    private readonly ICopilotCliLauncher _launcher;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    private Dictionary<string, ChapterDiagram>? _diagrams;
    private DateTimeOffset _checkedAt;
    private (int Files, long Newest) _signature;

    public ArchifyDiagramArtifacts(
        IAppFeatureSettings features,
        IKnowledgeFolderSource folders,
        GitHubSettingsStore repositories,
        ICopilotCliLauncher launcher)
        : this(features, folders, repositories, launcher, TimeProvider.System)
    {
    }

    /// <summary>The same adapter against a clock the caller names, so the interval
    /// the fence map is trusted for can be stood either side of without a test
    /// waiting out two real seconds.</summary>
    internal ArchifyDiagramArtifacts(
        IAppFeatureSettings features,
        IKnowledgeFolderSource folders,
        GitHubSettingsStore repositories,
        ICopilotCliLauncher launcher,
        TimeProvider clock)
    {
        _features = features;
        _folders = folders;
        _repositories = repositories;
        _launcher = launcher;
        _clock = clock;

        // A moved folder, a new clone or a switched flag all change the answer,
        // and the scan is cheap enough that throwing it away is cheaper than
        // working out which chapters were affected.
        _folders.Changed += Invalidate;
        _features.Changed += Invalidate;
    }

    /// <summary>Whether starting an agent is possible on this machine, which is
    /// the same question Second Brain's other CLI affordances ask. False hides the
    /// offer rather than letting somebody press a button that reports a missing
    /// CLI.</summary>
    public bool CanAuthor => _features.IsEnabled(AppFeatureKeys.CopilotCli);

    /// <summary>
    /// What exists for this diagram, or null.
    /// <para>
    /// Null covers two cases that look the same from here and are the same to the
    /// caller: the feature is off, or this is not a knowledge chapter diagram at
    /// all — a storybook sample, a diagram in an entry's description, anything
    /// with no chapter to write a specification beside. Answering with an empty
    /// artifact in the second case would put a "generate" offer under a diagram
    /// that has nowhere to put the result.
    /// </para>
    /// </summary>
    public DiagramArtifact? Find(string? source, string? language)
    {
        if (!DiagramView.CanRender(language)) return null;
        if (!_features.IsEnabled(KnowledgeFeatures.ArchifyDiagrams)) return null;
        if (Diagram(source) is not { } diagram) return null;

        var entries = ReadIndex(diagram.ChapterFile);
        var hash = DiagramSourceHash.Of(source);
        entries.TryGetValue(hash, out var entry);

        // An entry filed under a different hash for the same chapter and ordinal
        // is an artifact somebody generated before the fence was edited. It is the
        // one case worth saying out loud: the reader is looking at mermaid, the
        // picture that exists is not of what they can see, and neither of those is
        // obvious from the screen.
        //
        // The chapter half of that test is not optional. `_archify/` sits beside
        // the chapter, and in `.arc42/` every chapter shares one folder and so one
        // index — matching on the ordinal alone reports a diagram stale because a
        // different chapter's diagram N exists, and offers to re-render it against
        // the wrong specification.
        var displaced = entry is null
            ? entries.Values.FirstOrDefault(candidate => IsSameDiagram(candidate, diagram))
            : null;

        var directory = Path.Combine(Path.GetDirectoryName(diagram.ChapterFile)!, ArtifactDirectory);

        // What is actually on disk, not what the default type would have been
        // called. A flowchart defaults to `workflow`, but one may legitimately be
        // authored as `architecture`, `lifecycle` or `dataflow` — and computing
        // the filename from the default made every such specification invisible,
        // so the render button never appeared for a diagram that was ready to
        // render. The index still wins where it has an opinion, because it
        // records what was rendered rather than what is lying about.
        var named = entry ?? displaced;
        var discovered = DiscoverSpecification(directory, diagram);

        var type = named?.Type ?? discovered?.Type ?? ArchifyDiagramTypes.For(source);
        if (type is null) return new DiagramArtifact(null, null, null, null, false);

        var quality = named?.Quality ?? discovered?.Quality ?? ShowcaseQuality;
        var stem = $"{Path.GetFileNameWithoutExtension(diagram.ChapterFile)}.{diagram.Ordinal}.{type}"
            + (string.Equals(quality, StandardQuality, StringComparison.Ordinal) ? $".{StandardQuality}" : string.Empty);
        var specFile = Path.Combine(directory, $"{stem}.json");
        var artifactFile = Path.Combine(directory, $"{stem}.html");

        // Read only when the hash matched. An artifact on disk that no entry
        // points at is either the displaced one or a leftover, and showing either
        // is the drift this whole design exists to prevent.
        var html = entry is not null && File.Exists(artifactFile) ? TryReadAllText(artifactFile) : null;

        return new DiagramArtifact(
            html,
            html is null ? null : artifactFile,
            File.Exists(specFile) ? specFile : null,
            type,
            IsOutOfDate: html is null && displaced is not null,
            Quality: quality);
    }

    /// <summary>
    /// The specification on disk for this chapter's diagram N, whatever type or
    /// quality it was authored at, or null when there is none.
    /// <para>
    /// Listed rather than guessed at, which is the point: the filename carries
    /// the type and the quality, and both are choices an author makes that no
    /// default can predict. The generator surfaces two competing specifications
    /// as an error; here the alphabetically first wins instead. A knowledge pane
    /// must not fail to draw a chapter because somebody left a stray file beside
    /// it, and picking by sort order at least makes the wrong answer the same
    /// wrong answer every time.
    /// </para>
    /// </summary>
    private static SpecificationName? DiscoverSpecification(string directory, ChapterDiagram diagram)
    {
        if (!Directory.Exists(directory)) return null;

        var chapter = Path.GetFileNameWithoutExtension(diagram.ChapterFile);

        string[] candidates;
        try
        {
            candidates = Directory.GetFiles(directory, $"{chapter}.{diagram.Ordinal}.*.json");
        }
        catch (Exception)
        {
            return null;
        }

        Array.Sort(candidates, StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            // The pattern above is a filesystem glob, which is looser than the
            // rule: it matches any number of segments and, on Windows, the odd
            // short-name coincidence. The regex is the rule.
            if (SpecNamePattern.Match(Path.GetFileName(candidate)) is not { Success: true } match) continue;
            if (!string.Equals(match.Groups[1].Value, chapter, StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(match.Groups[2].ValueSpan, out var ordinal) || ordinal != diagram.Ordinal) continue;

            var type = match.Groups[3].Value;
            if (!ArchifyDiagramTypes.All.Contains(type, StringComparer.Ordinal)) continue;

            // `standard` is the only quality a filename may name — showcase is
            // what saying nothing means, so a name that spells it out is not a
            // specification this rule recognises, exactly as in the generator.
            if (!match.Groups[4].Success) return new SpecificationName(type, ShowcaseQuality);
            if (string.Equals(match.Groups[4].Value, StandardQuality, StringComparison.Ordinal))
            {
                return new SpecificationName(type, StandardQuality);
            }
        }

        return null;
    }

    /// <summary>Runs the pinned generator over the specification beside the
    /// chapter, in the clone the chapter came from. The generator validates before
    /// it writes and records the index entry itself, so this only starts it and
    /// reports what it said.</summary>
    public async Task<string?> RenderAsync(string? source, CancellationToken cancellationToken = default)
    {
        if (Diagram(source) is not { } diagram)
        {
            return "This diagram is not part of a knowledge chapter, so there is nothing to render.";
        }

        if (Find(source, "mermaid") is not { SpecPath: { } specFile })
        {
            return "There is no Archify specification for this diagram yet.";
        }

        var tool = Path.Combine(diagram.RootPath, "tools", "diagrams", "archify-artifacts.mjs");
        if (!File.Exists(tool))
        {
            return $"The Archify generator is not in this repository: {tool} does not exist.";
        }

        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = diagram.RootPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(tool);
        start.ArgumentList.Add("render");
        start.ArgumentList.Add(specFile);

        try
        {
            using var process = Process.Start(start);
            if (process is null) return "The Archify generator did not start.";

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(error) ? output : error;
                return string.IsNullOrWhiteSpace(detail)
                    ? $"The Archify generator failed with exit code {process.ExitCode}."
                    : detail.Trim();
            }

            // The generator wrote a new index entry, so what this class believes
            // about the chapter is now one render behind.
            Invalidate();
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"The Archify generator could not be started: {ex.Message}";
        }
    }

    /// <summary>Opens an agent session with a brief describing the diagram and
    /// where its specification belongs. The half no running app can do by itself:
    /// there is no mermaid-to-Archify converter, and Archify's own instructions
    /// describe an agent reading a diagram for meaning and writing fresh
    /// JSON.</summary>
    public async Task<string?> AuthorAsync(string? source, CancellationToken cancellationToken = default)
    {
        if (Diagram(source) is not { } diagram)
        {
            return "This diagram is not part of a knowledge chapter, so there is nowhere to author a specification.";
        }

        var type = ArchifyDiagramTypes.For(source);
        if (type is null)
        {
            return "No Archify diagram type fits this kind of diagram.";
        }

        try
        {
            await _launcher.LaunchAsync(new CopilotCliRequest(Brief(diagram, type, source), diagram.RootPath), cancellationToken);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public void Dispose()
    {
        _folders.Changed -= Invalidate;
        _features.Changed -= Invalidate;
    }

    /// <summary>What the agent is told. Names the four things it cannot work out
    /// on its own — which fence, where the specification goes, which type, and
    /// which instructions to follow — and then gets out of the way.</summary>
    private static string Brief(ChapterDiagram diagram, string type, string? source)
    {
        var chapter = Relative(diagram.RootPath, diagram.ChapterFile);
        var stem = $"{Path.GetFileNameWithoutExtension(diagram.ChapterFile)}.{diagram.Ordinal}.{type}";
        var spec = Relative(diagram.RootPath, Path.Combine(Path.GetDirectoryName(diagram.ChapterFile)!, ArtifactDirectory, $"{stem}.json"));

        return $"""
            Author an Archify specification for mermaid diagram {diagram.Ordinal} in {chapter}, then render it.

            Follow tools/archify/SKILL.md. Write the specification to {spec} as an Archify
            "{type}" diagram, then run:

                node tools/diagrams/archify-artifacts.mjs render {spec}

            The render must report 9/9 checks with no errors and no warnings.

            The markdown fence stays canonical and must not be edited. The artifact is a
            re-authoring of it for readers, so say what this diagram says and do not add
            anything it does not.

            The diagram:

            ```mermaid
            {DiagramSourceHash.Normalize(source)}
            ```
            """;
    }

    /// <summary>Whether an index entry is for this chapter's diagram N. The
    /// chapter is read from the entry when it names one and recovered from the
    /// specification's filename — <c>&lt;chapter&gt;.&lt;ordinal&gt;.&lt;type&gt;.json</c> — when it
    /// does not, matching <c>isSameDiagram</c> in the generator. An entry that
    /// names neither is treated as this chapter's, which is the old behaviour and
    /// is correct in every folder holding one chapter.</summary>
    private static bool IsSameDiagram(IndexEntry entry, ChapterDiagram diagram)
    {
        if (entry.Ordinal != diagram.Ordinal) return false;

        var chapter = Path.GetFileNameWithoutExtension(diagram.ChapterFile);
        var named = entry.Chapter is { } declared
            ? Path.GetFileNameWithoutExtension(declared)
            : entry.Spec is { } spec ? SpecNamePattern.Match(Path.GetFileName(spec)) is { Success: true } match
                ? match.Groups[1].Value
                : null
            : null;

        return named is null || string.Equals(named, chapter, StringComparison.OrdinalIgnoreCase);
    }

    private static string Relative(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>How long the fence map is trusted before the chapters are
    /// re-stated. Short, because the thing it is protecting against is a person
    /// editing a chapter in another window and wondering why the app has not
    /// noticed; long enough that a pane full of diagrams does not re-stat once
    /// per diagram.</summary>
    internal static readonly TimeSpan RecheckAfter = TimeSpan.FromSeconds(2);

    private void Invalidate()
    {
        lock (_gate)
        {
            _diagrams = null;
            _checkedAt = default;
        }
    }

    private ChapterDiagram? Diagram(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;

        Dictionary<string, ChapterDiagram> diagrams;
        lock (_gate)
        {
            // A chapter edited on disk has to be noticed, and nothing raises an
            // event when one is: IKnowledgeFolderSource.Changed fires when a
            // folder moves, not when a file inside it is saved. Without this the
            // edited fence hashes to something the cached map has never heard of,
            // Find returns null, and the reader gets plain mermaid with no drift
            // warning and no offer — silently worse than the designed answer, and
            // exactly the case hashing the source exists to catch.
            //
            // Re-stating is cheap and rebuilding is not, so the timer only buys
            // the right to look: the map is rebuilt when the chapters actually
            // changed, and left alone when they did not.
            if (_diagrams is not null && _clock.GetUtcNow() - _checkedAt > RecheckAfter)
            {
                _checkedAt = _clock.GetUtcNow();
                if (ChapterSignature() != _signature) _diagrams = null;
            }

            if (_diagrams is null)
            {
                _diagrams = BuildIndex();
                _signature = ChapterSignature();
                _checkedAt = _clock.GetUtcNow();
            }

            diagrams = _diagrams;
        }

        return diagrams.TryGetValue(DiagramSourceHash.Of(source), out var diagram) ? diagram : null;
    }

    /// <summary>How many chapter files there are and when the newest was last
    /// written. Enough to notice an edit, a new chapter or a deleted one, without
    /// reading a byte of any of them.</summary>
    private (int Files, long Newest) ChapterSignature()
    {
        var files = 0;
        var newest = 0L;

        foreach (var folder in ChapterFolders())
        {
            foreach (var chapter in MarkdownFiles(folder))
            {
                files++;
                try
                {
                    var written = File.GetLastWriteTimeUtc(chapter).Ticks;
                    if (written > newest) newest = written;
                }
                catch (Exception)
                {
                    // A file that cannot be stated is one BuildIndex will skip too.
                }
            }
        }

        return (files, newest);
    }

    /// <summary>
    /// Every mermaid fence in every knowledge chapter the app can currently
    /// reach, filed by hash.
    /// <para>
    /// Every configured scope rather than the one on screen, because a diagram's
    /// source is all this port is given — there is no repository alias to narrow
    /// it with. That is safe precisely because the key is a hash: two clones with
    /// the same chapter text have the same diagram, and either one's artifact is
    /// an answer to it.
    /// </para>
    /// </summary>
    private Dictionary<string, ChapterDiagram> BuildIndex()
    {
        var found = new Dictionary<string, ChapterDiagram>(StringComparer.Ordinal);

        foreach (var (folder, root) in ChapterFoldersWithRoots())
        {
            foreach (var chapter in MarkdownFiles(folder))
            {
                var ordinal = 0;
                foreach (var fence in MermaidFences(chapter))
                {
                    ordinal++;
                    // First writer wins: the same chapter reachable through two
                    // scopes is the same diagram, and the first clone resolved
                    // is as good an answer as the second.
                    found.TryAdd(DiagramSourceHash.Of(fence), new ChapterDiagram(chapter, ordinal, root));
                }
            }
        }

        return found;
    }

    /// <summary>The chapter folders currently reachable, paired with the clone
    /// root each was resolved through. One enumeration shared by the index and by
    /// the signature that decides when to rebuild it, so the two cannot disagree
    /// about which chapters exist.</summary>
    private IEnumerable<(string Folder, string Root)> ChapterFoldersWithRoots()
    {
        var aliases = new List<string?> { null };
        aliases.AddRange(_repositories.Current.Repositories.Select(repository => (string?)repository.Alias));

        foreach (var alias in aliases)
        {
            foreach (var key in DiagramFolderKeys)
            {
                KnowledgeFolderLocation location;
                try
                {
                    location = _folders.Resolve(key, alias);
                }
                catch (Exception)
                {
                    continue;
                }

                if (!location.Available) continue;
                if (location.FullPath is not { } folder || location.RootPath is not { } root) continue;
                if (!Directory.Exists(folder)) continue;

                yield return (folder, root);
            }
        }
    }

    private IEnumerable<string> ChapterFolders() =>
        ChapterFoldersWithRoots().Select(entry => entry.Folder);

    private static IEnumerable<string> MarkdownFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            string[] subdirectories;
            string[] files;
            try
            {
                subdirectories = Directory.GetDirectories(directory);
                files = Directory.GetFiles(directory, "*.md");
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var subdirectory in subdirectories)
            {
                var name = Path.GetFileName(subdirectory);
                // The artifact folder holds generated HTML and JSON, and a hidden
                // folder holds somebody's tooling. Neither is a chapter.
                if (name.Equals(ArtifactDirectory, StringComparison.Ordinal) || name.StartsWith('.')) continue;
                pending.Push(subdirectory);
            }

            foreach (var file in files) yield return file;
        }
    }

    /// <summary>The mermaid fence bodies in one chapter, in document order. The
    /// same scan as the generator's, including the indent it strips back off an
    /// indented fence.</summary>
    private static IEnumerable<string> MermaidFences(string file)
    {
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception)
        {
            yield break;
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

        string? marker = null;
        var length = 0;
        var indent = 0;
        var info = string.Empty;
        List<string> body = [];

        foreach (var line in lines)
        {
            var match = FencePattern.Match(line);

            if (marker is null)
            {
                if (!match.Success) continue;
                marker = match.Groups[2].Value[..1];
                length = match.Groups[2].Value.Length;
                indent = match.Groups[1].Value.Length;
                info = match.Groups[3].Value.ToLowerInvariant();
                body = [];
                continue;
            }

            var closes = match.Success
                && match.Groups[2].Value.StartsWith(marker, StringComparison.Ordinal)
                && match.Groups[2].Value.Length >= length
                && match.Groups[3].Value.Length == 0;

            if (!closes)
            {
                body.Add(line);
                continue;
            }

            if (info is "mermaid" or "mmd")
            {
                var fenceIndent = indent;
                yield return string.Join('\n', body.Select(entry =>
                    entry[Math.Min(fenceIndent, entry.Length - entry.AsSpan().TrimStart().Length)..]));
            }

            marker = null;
        }
    }

    private Dictionary<string, IndexEntry> ReadIndex(string chapterFile)
    {
        var file = Path.Combine(Path.GetDirectoryName(chapterFile)!, ArtifactDirectory, IndexFile);
        if (!File.Exists(file)) return [];

        try
        {
            var document = JsonSerializer.Deserialize<ArtifactIndex>(File.ReadAllText(file), JsonOptions);
            return document?.Entries is { } entries
                ? new Dictionary<string, IndexEntry>(entries, StringComparer.Ordinal)
                : [];
        }
        catch (Exception)
        {
            // An unreadable index is the same answer as no index: mermaid, with an
            // offer. A knowledge pane must not fail to draw a chapter because a
            // generated file beside it is malformed.
            return [];
        }
    }

    private static string? TryReadAllText(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A mermaid fence found in a chapter: which file, which fence, and
    /// which clone it was reached through.</summary>
    private sealed record ChapterDiagram(string ChapterFile, int Ordinal, string RootPath);

    /// <summary>The two decisions a specification's filename carries.</summary>
    private sealed record SpecificationName(string Type, string Quality);

    private sealed class ArtifactIndex
    {
        public Dictionary<string, IndexEntry>? Entries { get; init; }
    }

    private sealed class IndexEntry
    {
        /// <summary>The chapter file this entry belongs to. Absent in an index
        /// written before one folder was found to hold several chapters.</summary>
        public string? Chapter { get; init; }

        public int Ordinal { get; init; }

        public string? Type { get; init; }

        /// <summary>The quality profile the artifact was rendered at. Absent in
        /// an index written before the opt-out existed, which reads the same as
        /// <c>showcase</c> — every artifact predating it was rendered at
        /// showcase, because nothing else was possible.</summary>
        public string? Quality { get; init; }

        public string? Kind { get; init; }

        public string? Spec { get; init; }

        public string? Artifact { get; init; }
    }
}
