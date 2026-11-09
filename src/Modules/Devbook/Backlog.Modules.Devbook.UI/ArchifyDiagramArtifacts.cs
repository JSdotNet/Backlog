using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Devbook;
using Backlog.Infrastructure.Devbook.Archify;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel;
using Backlog.UI.Components.Diagrams;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Answers <see cref="IDiagramArtifactSource"/> from the repository clone the
/// devbook chapters are read out of.
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
/// is pleasant, but because the lookup is synchronous and asked once per diagram
/// per render, which no Node process can answer in time. The two must agree
/// exactly, and the unit tests pin the hash on both sides.
/// </para>
/// <para>
/// The picture itself is the one thing Node does draw, and only once per
/// specification per machine (local ADR 0034): the renders are not committed, so
/// a diagram whose index entry matches its fence is drawn from its specification
/// by <see cref="ArchifyArtifactRenderer"/> into the app's devbook cache, and
/// every lookup after that is answered from the cache without starting anything.
/// </para>
/// </summary>
public sealed class ArchifyDiagramArtifacts : IDiagramArtifactSource, IDisposable
{
    /// <summary>The folder beside a chapter that holds its specifications, its
    /// artifacts and the index tying them to fences.</summary>
    private const string ArtifactDirectory = "_archify";

    private const string IndexFile = "index.json";

    /// <summary>The devbook folders whose chapters carry diagrams. The
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
    private readonly IDevbookFolderSource _folders;
    private readonly GitHubSettingsStore _repositories;
    private readonly ICopilotCliLauncher _launcher;
    private readonly ArchifyArtifactRenderer _renderer;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    private Dictionary<string, ChapterDiagram>? _diagrams;
    private DateTimeOffset _checkedAt;
    private (int Files, long Newest) _signature;

    public ArchifyDiagramArtifacts(
        IAppFeatureSettings features,
        IDevbookFolderSource folders,
        GitHubSettingsStore repositories,
        ICopilotCliLauncher launcher,
        ArchifyArtifactRenderer renderer)
        : this(features, folders, repositories, launcher, renderer, TimeProvider.System)
    {
    }

    /// <summary>The same adapter against a clock the caller names, so the interval
    /// the fence map is trusted for can be stood either side of without a test
    /// waiting out two real seconds.</summary>
    internal ArchifyDiagramArtifacts(
        IAppFeatureSettings features,
        IDevbookFolderSource folders,
        GitHubSettingsStore repositories,
        ICopilotCliLauncher launcher,
        ArchifyArtifactRenderer renderer,
        TimeProvider clock)
    {
        _features = features;
        _folders = folders;
        _repositories = repositories;
        _launcher = launcher;
        _renderer = renderer;
        _clock = clock;

        // A moved folder, a new clone or a switched flag all change the answer,
        // and the scan is cheap enough that throwing it away is cheaper than
        // working out which chapters were affected.
        _folders.Changed += Invalidate;
        _features.Changed += Invalidate;
    }

    /// <summary>Whether starting an agent is possible on this machine, which is
    /// the same question Devbook's other CLI affordances ask. False hides the
    /// offer rather than letting somebody press a button that reports a missing
    /// CLI.</summary>
    public bool CanAuthor => _features.IsEnabled(AppFeatureKeys.CopilotCli);

    /// <summary>
    /// What exists for this diagram, or null.
    /// <para>
    /// Null covers two cases that look the same from here and are the same to the
    /// caller: the feature is off, or this is not a devbook chapter diagram at
    /// all — a storybook sample, a diagram in an entry's description, anything
    /// with no chapter to write a specification beside. Answering with an empty
    /// artifact in the second case would put a "generate" offer under a diagram
    /// that has nowhere to put the result.
    /// </para>
    /// </summary>
    public DiagramArtifact? Find(string? source, string? language) => Resolve(source, language).Artifact;

    /// <summary>
    /// <see cref="Find"/>, and then the picture drawn if it can be: a diagram
    /// whose index entry matches its fence and whose specification is there, but
    /// which nobody has drawn on this machine yet. A failed render comes back with
    /// its reason and falls back to the mermaid; it is not remembered, so the next
    /// lookup tries again.
    /// </summary>
    public async Task<DiagramArtifact?> LoadAsync(string? source, string? language, CancellationToken cancellationToken = default)
    {
        var (artifact, request) = Resolve(source, language);
        if (artifact is not { IsRenderPending: true } || request is null) return artifact;

        var render = await _renderer.RenderAsync(request, cancellationToken).ConfigureAwait(false);

        return render.Html is { } html
            ? artifact with { Html = html, ArtifactPath = render.ArtifactPath, IsRenderPending = false }
            : artifact with { IsRenderPending = false, RenderFailure = render.Failure };
    }

    /// <summary>What exists for this diagram, and the render request that would
    /// draw it when its picture is not cached yet.</summary>
    private (DiagramArtifact? Artifact, ArchifyRenderRequest? Request) Resolve(string? source, string? language)
    {
        if (!DiagramView.CanRender(language)) return (null, null);
        if (!_features.IsEnabled(DevbookFeatures.ArchifyDiagrams)) return (null, null);
        if (Diagram(source) is not { } diagram) return (null, null);

        var entries = ReadIndex(diagram);
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
        var displaced = entries
            .FirstOrDefault(candidate => candidate.Key != hash && IsSameDiagram(candidate.Value, diagram))
            .Value;

        var directory = Path.Combine(Path.GetDirectoryName(diagram.ChapterFile)!, ArtifactDirectory);

        // What is on disk, then what this fence's own entry recorded, then the
        // default for the kind — the order `scan` in the generator resolves in.
        // Never what a displaced entry recorded: that entry is about whichever
        // fence held this ordinal before, and naming the specification from it
        // asked for a file at a type this fence may not have, which hid the one
        // that is there and swapped the Render offer for Author.
        var discovered = DiscoverSpecification(directory, diagram, entry);

        var type = discovered?.Type ?? entry?.Type ?? ArchifyDiagramTypes.For(source);
        if (type is null) return (new DiagramArtifact(null, null, null, null, false), null);

        var quality = discovered?.Quality ?? entry?.Quality ?? ShowcaseQuality;
        var specFile = discovered is not null ? Path.Combine(directory, discovered.FileName) : null;

        // Drawn only when this fence's entry names the file on disk. A
        // specification no entry files under this fence's hash is either the
        // displaced one or not rendered yet; one whose entry names another file is
        // a neighbour's left at this ordinal after fences moved, or this fence's
        // own renamed to follow it — and drawing either would be the drift this
        // design exists to prevent. `render` tells the two apart and re-keys the
        // renamed one. The picture comes from the app's cache and never from an
        // HTML file beside the specification: renders are not committed (local
        // ADR 0034), and one lying there is a local preview that may be of an
        // older specification.
        var current = entry is not null && discovered is not null && Names(entry, discovered, diagram);
        var request = current
            ? new ArchifyRenderRequest(specFile!, type, quality, diagram.RootPath)
            : null;
        var cached = request is null ? null : _renderer.TryCached(request);

        // Out of date: a picture exists that is not of this fence as it stands —
        // the fence was edited, or it moved and the file at its ordinal is not the
        // one its entry names, or no specification followed it there.
        var outOfDate = request is null
            && (displaced is not null || (entry is not null && (discovered is not null || Moved(entry, diagram))));

        return (new DiagramArtifact(
            cached?.Html,
            cached?.ArtifactPath,
            specFile,
            type,
            IsOutOfDate: outOfDate,
            Quality: quality,
            IsRenderPending: request is not null && cached is null), request);
    }

    /// <summary>
    /// Whether this fence's own entry is about the specification on disk.
    /// <para>
    /// An entry of this chapter's names its file, and recorded the ordinal the
    /// fence then held, so after fences moved it names the file at the old ordinal
    /// — the one the fence's specification was authored as, not the neighbour's now
    /// sitting at the new one. Any other entry — one written before the
    /// <c>spec</c> field, one of an identical fence elsewhere in this chapter, or
    /// one of another chapter's identical fence — names some other fence's file
    /// and only has to agree on type and quality: one hash has one entry, so
    /// identical fences share both. These are <c>unrecorded</c> and
    /// <c>contradicts</c> in the generator's <c>scan</c>.
    /// </para>
    /// </summary>
    private static bool Names(IndexEntry entry, SpecificationName discovered, ChapterDiagram diagram)
    {
        if (entry.Spec is { } spec && IsSameChapter(entry, diagram)
            && (entry.Ordinal == diagram.Ordinal || Moved(entry, diagram)))
        {
            return string.Equals(Path.GetFileName(spec), discovered.FileName, StringComparison.OrdinalIgnoreCase);
        }

        return (entry.Type is null || string.Equals(entry.Type, discovered.Type, StringComparison.Ordinal))
            && string.Equals(entry.Quality ?? ShowcaseQuality, discovered.Quality, StringComparison.Ordinal);
    }

    /// <summary>Whether this chapter's entry for this fence recorded another
    /// ordinal, at which no identical fence sits now — the fence moved. An
    /// identical fence there is the same diagram, and its entry is this one's too
    /// (<c>recordedElsewhere</c> in the generator).</summary>
    private static bool Moved(IndexEntry entry, ChapterDiagram diagram) =>
        IsSameChapter(entry, diagram) && !diagram.Ordinals.Contains(entry.Ordinal);

    /// <summary>
    /// The specification on disk for this chapter's diagram N, whatever type or
    /// quality it was authored at, or null when there is none.
    /// <para>
    /// Listed rather than guessed at, which is the point: the filename carries
    /// the type and the quality, and both are choices an author makes that no
    /// default can predict. The generator surfaces two competing specifications
    /// as an error; here the one this fence's own entry names wins, and otherwise
    /// the alphabetically first. A Devbook pane must not fail to draw a chapter
    /// because somebody left a stray file beside it, and picking by sort order at
    /// least makes the wrong answer the same wrong answer every time.
    /// </para>
    /// </summary>
    private static SpecificationName? DiscoverSpecification(string directory, ChapterDiagram diagram, IndexEntry? entry)
    {
        var found = DiscoverSpecifications(directory, diagram);

        return (entry is not null ? found.FirstOrDefault(candidate => Names(entry, candidate, diagram)) : null)
            ?? found.FirstOrDefault();
    }

    private static List<SpecificationName> DiscoverSpecifications(string directory, ChapterDiagram diagram)
    {
        List<SpecificationName> found = [];
        if (!Directory.Exists(directory)) return found;
        var chapter = Path.GetFileNameWithoutExtension(diagram.ChapterFile);

        string[] candidates;
        try
        {
            candidates = Directory.GetFiles(directory, $"{chapter}.{diagram.Ordinal}.*.json");
        }
        catch (IOException)
        {
            return found;
        }
        catch (UnauthorizedAccessException)
        {
            return found;
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
            var name = Path.GetFileName(candidate);
            if (!match.Groups[4].Success) found.Add(new SpecificationName(name, type, ShowcaseQuality));
            else if (string.Equals(match.Groups[4].Value, StandardQuality, StringComparison.Ordinal))
            {
                found.Add(new SpecificationName(name, type, StandardQuality));
            }
        }

        return found;
    }

    /// <summary>Runs the pinned generator over the specification beside the
    /// chapter, in the clone the chapter came from. The generator validates before
    /// it writes and records the index entry itself, so this only starts it and
    /// reports what it said.</summary>
    public async Task<string?> RenderAsync(string? source, CancellationToken cancellationToken = default)
    {
        if (Diagram(source) is not { } diagram)
        {
            return "This diagram is not part of a devbook chapter, so there is nothing to render.";
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

        try
        {
            using var process = Process.Start(GeneratorStart(diagram.RootPath, tool, specFile));
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

    /// <summary>How the generator is started. Node writes UTF-8 whatever the
    /// console's code page is, so both streams are read as UTF-8: decoded with
    /// the code page instead, an em dash in a refusal reached the diagram view as
    /// <c>â€”</c>.</summary>
    internal static ProcessStartInfo GeneratorStart(string rootPath, string tool, string specFile)
    {
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = rootPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add(tool);
        start.ArgumentList.Add("render");
        start.ArgumentList.Add(specFile);
        return start;
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
            return "This diagram is not part of a devbook chapter, so there is nowhere to author a specification.";
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
    private static bool IsSameDiagram(IndexEntry entry, ChapterDiagram diagram) =>
        entry.Ordinal == diagram.Ordinal && IsSameChapter(entry, diagram);

    private static bool IsSameChapter(IndexEntry entry, ChapterDiagram diagram)
    {
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
            // event when one is: IDevbookFolderSource.Changed fires when a
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
                catch (IOException)
                {
                    // A file that cannot be stated is one BuildIndex will skip too.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        return (files, newest);
    }

    /// <summary>
    /// Every mermaid fence in every devbook chapter the app can currently
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
                var hashes = MermaidFences(chapter).Select(DiagramSourceHash.Of).ToList();

                for (var index = 0; index < hashes.Count; index++)
                {
                    var hash = hashes[index];
                    var ordinals = Enumerable.Range(1, hashes.Count)
                        .Where(ordinal => hashes[ordinal - 1] == hash)
                        .ToHashSet();

                    // First writer wins: the same chapter reachable through two
                    // scopes is the same diagram, and the first clone resolved
                    // is as good an answer as the second.
                    found.TryAdd(hash, new ChapterDiagram(chapter, index + 1, root, ordinals));
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
                DevbookFolderLocation location;
                try
                {
                    location = _folders.Resolve(key, alias);
                }
                // A clone that cannot be reached is a folder with nothing in it.
                // Anything else is a bug in the folder source, and reading it as
                // "no diagrams" would hide it.
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
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
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
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
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
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

    /// <summary>
    /// What has been rendered for the chapters in this diagram's folder, keyed by
    /// fence hash.
    ///
    /// <para>The generated database first — local ADR 0004 copied the fourteen
    /// <c>_archify/index.json</c> files into <c>archify_artifact</c> rows, so the
    /// lookup stops reading a JSON file per folder — and the file itself when there
    /// is no database. The specifications stay on disk either way; only the pointer
    /// moved, and the picture is drawn from the specification (local ADR
    /// 0034).</para>
    ///
    /// <para>Read per call rather than cached, which is what it did before. The
    /// artifact index changes underneath this while the app is running — that is
    /// what the generate button does — and a cache with nothing to invalidate it
    /// would leave a reader looking at mermaid beside a picture that now exists.</para>
    /// </summary>
    private Dictionary<string, IndexEntry> ReadIndex(ChapterDiagram diagram) =>
        ReadDatabaseIndex(diagram) ?? ReadJsonIndex(diagram.ChapterFile);

    /// <summary>
    /// The folder's rows out of the database, or <see langword="null"/> when this
    /// repository has no database, or has one that does not describe this chapter.
    ///
    /// <para>The second case matters as much as the first: a chapter reached
    /// through a clone the database was not built from would otherwise be told it
    /// has no artifacts, when what it has is no rows. Answering null sends it to
    /// the file beside it, which is the same answer it got before.</para>
    /// </summary>
    private Dictionary<string, IndexEntry>? ReadDatabaseIndex(ChapterDiagram diagram)
    {
        using var database = DevbookDatabase.TryOpen(DevbookDatabaseLocation.ForRepositoryRoot(diagram.RootPath));
        if (database is null) return null;

        var directory = Path.GetRelativePath(diagram.RootPath, Path.GetDirectoryName(diagram.ChapterFile)!)
            .Replace('\\', '/');

        var rows = database.ArchifyArtifacts(directory);
        if (rows.Count == 0) return null;

        var entries = new Dictionary<string, IndexEntry>(rows.Count, StringComparer.Ordinal);

        foreach (var row in rows)
        {
            // Spelled the way the JSON spells it — bare names beside the chapter —
            // so the two sources hand `IsSameDiagram` and the specification lookup
            // the same strings and neither has to know which one answered.
            entries[row.FenceHash] = new IndexEntry
            {
                Chapter = Path.GetFileName(row.ChapterPath),
                Ordinal = row.Ordinal,
                Type = row.Type,
                Quality = row.Quality,
                Kind = row.Kind,
                Spec = row.SpecPath is { } spec ? Path.GetFileName(spec) : null,
                Artifact = row.ArtifactPath is { } artifact ? Path.GetFileName(artifact) : null
            };
        }

        return entries;
    }

    private Dictionary<string, IndexEntry> ReadJsonIndex(string chapterFile)
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
        catch (JsonException)
        {
            // An unreadable index is the same answer as no index: mermaid, with an
            // offer. A Devbook pane must not fail to draw a chapter because a
            // generated file beside it is malformed.
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>A mermaid fence found in a chapter: which file, which fence, and
    /// which clone it was reached through.</summary>
    /// <param name="Ordinals">Every ordinal in the chapter whose fence is this one's
    /// text, this one's among them: identical fences are one diagram to the index,
    /// which keys by hash.</param>
    private sealed record ChapterDiagram(string ChapterFile, int Ordinal, string RootPath, IReadOnlySet<int> Ordinals);

    /// <summary>A specification on disk, by its filename and the two decisions
    /// that name carries.</summary>
    private sealed record SpecificationName(string FileName, string Type, string Quality);

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
