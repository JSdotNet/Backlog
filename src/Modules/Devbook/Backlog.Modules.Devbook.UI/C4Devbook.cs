using System.Text.Json;

using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Markdown;
using Backlog.UI.Components.Diagrams.C4;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The C4 model that sits beside the architecture chapters.
/// <para>
/// Everything the pure reader deliberately does not know is here: whether the
/// feature is switched on, which repository clone the chapters came from, and where
/// <c>_c4/</c> sits inside it. The reader is handed text and answers with a
/// workspace; this is where that becomes a question about files.
/// </para>
/// <para>
/// Nothing here is written to the repository. A workspace is one authored
/// <c>.dsl</c> — written in c4hero, which is a browser editor rather than anything
/// this app runs — plus an authored <c>references.json</c> saying which chapter each
/// view documents. The views are read out of the DSL on load and written as mermaid
/// on the spot.
/// </para>
/// <para>
/// A folder with no <c>.dsl</c> at all is given a model anyway, derived in memory
/// from the C4 mermaid fences its chapters already carry — one view per fence, each
/// documenting the chapter it is in. That is the one thing here that is derived
/// rather than read, and it is held only as long as the catalog is: an authored
/// workspace, once there is one, replaces it entirely.
/// </para>
/// <para>
/// That is the whole difference from the Archify arrangement next door. An Archify
/// artifact is a re-authoring of a mermaid fence with nothing in it pointing back,
/// so it has to be matched to that fence by hash and filed in a generated index, and
/// it can go stale. A C4 workspace is attached to no fence, has no committed
/// rendering, and therefore has nothing to drift from.
/// </para>
/// </summary>
public sealed class C4DevbookStore : IDisposable
{
    /// <summary>The folder beside the chapters that holds the workspaces. Named to
    /// sit next to <c>_archify</c> and <c>_meta</c>, which is what puts it out of
    /// the way of the chapter listing.</summary>
    public const string WorkspaceDirectory = "_c4";

    /// <summary>
    /// The authored map from view key to the chapters that view documents.
    /// <para>
    /// <c>references.json</c> rather than <c>index.json</c> because the
    /// <c>_archify/index.json</c> beside a chapter is generated, and a file with that
    /// name beside a workspace would read as generated too. This one is authored and
    /// reviewed like the <c>.dsl</c> is.
    /// </para>
    /// <para>
    /// A file at all because the two better homes are both closed. A chapter's own
    /// <c>related:</c> list is refused by the devbook-meta checker, which resolves
    /// references against <c>.md</c> nodes only and treats anything under
    /// <c>.devbook/arc42/</c> as an error rather than a warning — and that checker is an
    /// installed copy of plugin tooling this repository must not edit. A
    /// <c>properties</c> block in the workspace or on a view keeps the link with the
    /// model and is deleted by c4hero, whose parser skips both and never writes them
    /// back, so the reference would vanish on the first save in the editor.
    /// </para>
    /// </summary>
    public const string ReferenceFile = "references.json";

    /// <summary>The knowledge folder this feature is scoped to. Architecture only,
    /// deliberately: C4 describes systems, containers and components, and the other
    /// folders describe a domain model, a technology graph and a design language,
    /// none of which C4 has a vocabulary for.</summary>
    public const string FolderKey = ".arc42";

    /// <summary>What the derived workspace is filed under in place of a file name.
    /// Deliberately not a <c>.dsl</c>: <see cref="C4Catalog.Find"/> resolves only
    /// references naming one, so nothing a chapter writes can be mistaken for a
    /// reference to a model nobody authored.</summary>
    public const string DerivedWorkspaceFile = "chapters";

    /// <summary>The derived workspace's name, as the tab shows it.</summary>
    public const string DerivedWorkspaceName = "From the chapters";

    private readonly IAppFeatureSettings _features;
    private readonly IDevbookFolderSource _folders;
    private readonly object _gate = new();

    private string? _cachedFor;
    private C4Catalog? _cached;

    /// <summary>Bumped whenever the cache is dropped, so a read that began before the
    /// drop does not put what it found back afterwards. Two quick saves start two reads
    /// of the chapters, and the slower, older one finishing last would otherwise leave
    /// the model as the first save had it.</summary>
    private int _generation;

    public C4DevbookStore(IAppFeatureSettings features, IDevbookFolderSource folders)
    {
        _features = features;
        _folders = folders;

        _folders.Changed += Invalidate;
        _features.Changed += Invalidate;
    }

    public event Action? Changed;

    /// <summary>Whether the feature is on at all. Asked before anything is drawn, so
    /// a panel with the feature off is the panel it was before this existed.</summary>
    public bool Enabled => _features.IsEnabled(DevbookFeatures.C4Diagrams);

    /// <summary>
    /// The workspaces beside the architecture chapters of this scope, with every
    /// view already written as mermaid.
    /// <para>
    /// Written eagerly rather than on selection because these models are small — a
    /// handful of views over a few dozen elements — and because a view that cannot
    /// be written is worth knowing about while the list is being drawn rather than
    /// when somebody clicks it.
    /// </para>
    /// </summary>
    public async Task<C4Catalog> LoadAsync(string? repositoryAlias = null)
    {
        if (!Enabled) return C4Catalog.Off;

        var key = repositoryAlias ?? string.Empty;
        int generation;

        lock (_gate)
        {
            if (_cached is not null && string.Equals(_cachedFor, key, StringComparison.Ordinal))
            {
                return _cached;
            }

            generation = _generation;
        }

        // Only the workspace folder, not the architecture chapters beside it:
        // the arc42 store fetches those when its catalog is built, and this
        // store is read alongside it rather than instead of it.
        var location = await _folders
            .PrepareContentAsync(FolderKey, repositoryAlias, [WorkspaceDirectory + "/"])
            .ConfigureAwait(false);

        // With no workspace the model is drawn from the chapters' own fences, so
        // the chapters have to be on disk too. The arc42 store fetches the same
        // folder, and a file already current is not fetched twice. The Domain
        // panel loads this store as well, so opening it on a branch with no
        // workspace fetches the arc42 chapters too — accepted, since the fallback
        // needs them for the views that panel links to.
        if (!HasAuthoredWorkspace(location))
        {
            location = await _folders.PrepareContentAsync(FolderKey, repositoryAlias).ConfigureAwait(false);
        }

        // On the pool, because deriving reads every chapter in the folder and the
        // caller's continuation is the UI thread in the desktop host.
        var prepared = location;
        var catalog = await Task.Run(() => Read(prepared)).ConfigureAwait(false);

        lock (_gate)
        {
            if (generation == _generation)
            {
                _cachedFor = key;
                _cached = catalog;
            }
        }

        return catalog;
    }

    private static C4Catalog Read(DevbookFolderLocation location)
    {
        if (!location.Available || location.FullPath is null) return C4Catalog.Off;

        var directory = Path.Combine(location.FullPath, WorkspaceDirectory);
        if (!HasAuthoredWorkspace(location)) return Derive(location, directory);

        var views = new List<C4ViewEntry>();
        var workspaces = new List<C4WorkspaceEntry>();
        var problems = new List<C4WorkspaceProblem>();
        var references = ReadReferences(directory, problems);

        foreach (var file in Directory.EnumerateFiles(directory, "*.dsl", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);

            C4Workspace workspace;
            try
            {
                workspace = C4DslReader.Read(File.ReadAllText(file));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                problems.Add(new C4WorkspaceProblem(name, new C4Problem(0, name, exception.Message)));
                continue;
            }

            foreach (var problem in workspace.Problems) problems.Add(new C4WorkspaceProblem(name, problem));

            // The parsed workspace is kept, not just the views written out of it. The
            // explorer needs the model itself — what a node drills into, which facet
            // values exist, what a search matches are all questions about the model
            // rather than about one picture.
            workspaces.Add(new C4WorkspaceEntry(
                name,
                workspace.Name ?? Path.GetFileNameWithoutExtension(name),
                workspace,
                [.. workspace.Problems],
                references));

            foreach (var view in workspace.Views)
            {
                views.Add(new C4ViewEntry(
                    Reference($"{FolderKey}/{WorkspaceDirectory}/{name}", view.Key),
                    name,
                    workspace.Name ?? Path.GetFileNameWithoutExtension(name),
                    view.Key,
                    view.Kind,
                    Label(workspace, view),
                    C4MermaidWriter.Write(workspace, view),
                    Documents(references, view.Key)));
            }
        }

        return new C4Catalog(true, directory, views, problems, workspaces);
    }

    /// <summary>Whether somebody wrote a workspace. Any <c>.dsl</c> at all decides
    /// it: an authored model is the model, and drawing the chapters' fences beside it
    /// would offer two models of one architecture and leave the reader to choose.</summary>
    private static bool HasAuthoredWorkspace(DevbookFolderLocation location)
    {
        if (!location.Available || location.FullPath is null) return false;

        var directory = Path.Combine(location.FullPath, WorkspaceDirectory);
        return Directory.Exists(directory)
            && Directory.EnumerateFiles(directory, "*.dsl", SearchOption.TopDirectoryOnly).Any();
    }

    /// <summary>
    /// The model drawn from the chapters' own C4 fences, for a folder with no
    /// workspace.
    /// <para>
    /// Every view documents the chapter its fence is in, which is the one reference
    /// map nobody has to write: the fence is in the chapter, so the chapter is what
    /// it documents. That map is handed over as though it had been read from
    /// <c>references.json</c>, and both directions of the link then work with no
    /// code of their own.
    /// </para>
    /// <para>
    /// Chapters are found by the rule the chapter catalog uses — recursive, and
    /// skipping any underscored segment — and spelled the way it spells them, so the
    /// path a view documents is the path the chapter list holds.
    /// </para>
    /// </summary>
    private static C4Catalog Derive(DevbookFolderLocation location, string directory)
    {
        var folder = location.FullPath!;
        if (!Directory.Exists(folder)) return new C4Catalog(true, directory, [], [], []);

        var root = Arc42DevbookReader.ResolveRoot(folder, location.RootPath);
        var sources = new List<C4MermaidSource>();
        var problems = new List<C4WorkspaceProblem>();

        var chapters = Directory.EnumerateFiles(folder, "*.md", Arc42DevbookReader.Recursive)
            .Where(path => !Arc42DevbookReader.IsUnderscored(Path.GetRelativePath(folder, path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (var file in chapters)
        {
            var chapter = Path.GetRelativePath(root, file).Replace('\\', '/');

            try
            {
                sources.AddRange(C4Fences(chapter, File.ReadAllText(file)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                problems.Add(new C4WorkspaceProblem(DerivedWorkspaceFile, new C4Problem(0, chapter, exception.Message)));
            }
        }

        // A chapter that could not be read is kept in the catalog's problems, but
        // with no workspace there is no tab to show it under, so it is not seen.
        if (sources.Count == 0) return new C4Catalog(true, directory, [], problems, []);

        var reading = C4MermaidReader.ReadWithOrigins(DerivedWorkspaceName, sources);
        var workspace = reading.Workspace;
        if (workspace.Views.Count == 0) return new C4Catalog(true, directory, [], problems, []);

        var references = reading.ViewOrigins.ToDictionary(
            pair => pair.Key,
            pair => new[] { pair.Value },
            StringComparer.OrdinalIgnoreCase);

        foreach (var problem in workspace.Problems) problems.Add(new C4WorkspaceProblem(DerivedWorkspaceFile, problem));

        var views = workspace.Views
            .Select(view => new C4ViewEntry(
                Reference(reading.ViewOrigins[view.Key], view.Key),
                DerivedWorkspaceFile,
                DerivedWorkspaceName,
                view.Key,
                view.Kind,
                Label(workspace, view),
                C4MermaidWriter.Write(workspace, view),
                references[view.Key]))
            .ToList();

        var entry = new C4WorkspaceEntry(
            DerivedWorkspaceFile,
            DerivedWorkspaceName,
            workspace,
            [.. workspace.Problems],
            references,
            Derived: true);

        return new C4Catalog(true, directory, views, problems, [entry]);
    }

    /// <summary>
    /// The C4 mermaid fences in one chapter.
    /// <para>
    /// Opened and closed by CommonMark's rule rather than by "three backticks", for
    /// the reason the chapter reader gives: an <c>annotation</c> note quoting a fence
    /// is opened with four so the quote can sit inside it, and a review note's
    /// sample diagram is not part of the model.
    /// </para>
    /// </summary>
    private static IEnumerable<C4MermaidSource> C4Fences(string chapter, string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            if (MarkdownFence.Open(lines[index]) is not { } fence) continue;

            var body = new List<string>();
            for (index++; index < lines.Length && !fence.IsClosedBy(lines[index]); index++)
            {
                body.Add(lines[index]);
            }

            var language = fence.Language.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.Equals(language, "mermaid", StringComparison.OrdinalIgnoreCase)) continue;

            var fenceText = string.Join('\n', body);
            if (C4MermaidReader.IsC4(fenceText)) yield return new C4MermaidSource(chapter, fenceText);
        }
    }

    /// <summary>How a view is addressed: the workspace path as the repository sees it,
    /// then the view key as the anchor. The same shape every other knowledge reference
    /// has, which is what lets one travel through <c>DevbookChapterLink</c> and the
    /// pane's own selection without either learning a second spelling.</summary>
    public static string Reference(string workspacePath, string viewKey) => $"{workspacePath}#{viewKey}";

    /// <summary>
    /// The authored reference map, or nothing.
    /// <para>
    /// A missing file is not a problem: a workspace whose views document no chapter yet
    /// is a workspace somebody is still writing, and the panel already says so per view.
    /// A file that will not parse is a problem, because that is a reference map somebody
    /// meant to be read.
    /// </para>
    /// </summary>
    private static Dictionary<string, string[]> ReadReferences(string directory, List<C4WorkspaceProblem> problems)
    {
        var path = Path.Combine(directory, ReferenceFile);
        if (!File.Exists(path)) return [];

        try
        {
            var document = JsonSerializer.Deserialize<ReferenceDocument>(File.ReadAllText(path), JsonOptions);
            return document?.Views is { } views
                ? new Dictionary<string, string[]>(views, StringComparer.OrdinalIgnoreCase)
                : [];
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            problems.Add(new C4WorkspaceProblem(ReferenceFile, new C4Problem(0, ReferenceFile, exception.Message)));
            return [];
        }
    }

    private static IReadOnlyList<string> Documents(Dictionary<string, string[]> references, string key) =>
        references.TryGetValue(key, out var chapters) ? chapters : [];

    private sealed record ReferenceDocument(Dictionary<string, string[]>? Views);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static string Label(C4Workspace workspace, C4View view)
    {
        if (!string.IsNullOrWhiteSpace(view.Title)) return view.Title;
        if (!string.IsNullOrWhiteSpace(view.Description)) return view.Description;

        var scope = workspace.Element(view.ScopeId)?.Name;
        return scope is null ? view.Kind.ToString() : $"{view.Kind} — {scope}";
    }

    /// <summary>
    /// A chapter was saved in the app, so a model drawn from the chapters' fences may
    /// no longer be what they say.
    /// <para>
    /// The cached catalog is dropped quietly rather than through <see cref="Changed"/>:
    /// the caller is the panel that saved, and it re-reads straight afterwards —
    /// raising the event as well would re-read every chapter twice on each debounced
    /// keystroke-save. A catalog read from an authored workspace is kept, because no
    /// chapter save changes a <c>.dsl</c>. A catalog with no workspace at all is
    /// dropped too: the save may have added the first C4 fence.
    /// </para>
    /// </summary>
    public void ChapterSaved()
    {
        lock (_gate)
        {
            if (_cached is { Workspaces: var workspaces } && workspaces.Any(entry => !entry.Derived)) return;

            _cached = null;
            _cachedFor = null;
            _generation++;
        }
    }

    private void Invalidate()
    {
        lock (_gate)
        {
            _cached = null;
            _cachedFor = null;
            _generation++;
        }

        Changed?.Invoke();
    }

    public void Dispose()
    {
        _folders.Changed -= Invalidate;
        _features.Changed -= Invalidate;
    }
}

/// <summary>One view, ready to draw and ready to be referenced.</summary>
/// <param name="Reference">What a chapter writes in its <c>related:</c> list to
/// point at this view, and what the panel matches a chapter's references against.</param>
/// <param name="Mermaid">The view written as mermaid C4. Held rather than a path,
/// because there is no file: the <c>.dsl</c> is the only thing on disk and this is
/// what it says.</param>
/// <param name="Documents">The chapters this view documents, as
/// <c>references.json</c> states them — or, for a view drawn from a chapter's
/// fence, that chapter. The authored half of the reference; the other
/// direction — which views document a chapter — is this list inverted, so there is
/// no second place for the two to disagree.</param>
public sealed record C4ViewEntry(
    string Reference,
    string WorkspaceFile,
    string WorkspaceName,
    string Key,
    C4ViewKind Kind,
    string Title,
    string Mermaid,
    IReadOnlyList<string> Documents)
{
    /// <summary>
    /// Whether this view says it documents the given chapter.
    /// <para>
    /// Matched on the file and not on the heading. A reference states the section it is
    /// about — <c>#container-view</c> — and that is worth keeping in the authored file
    /// for a reader, but the chapter side of the link is drawn against a whole open
    /// chapter, so narrowing the match to the heading would hide the reference on the
    /// very file it was written for.
    /// </para>
    /// </summary>
    public bool Documented(string? chapterPath)
    {
        if (chapterPath is null) return false;

        var wanted = FileOf(chapterPath);
        return Documents.Any(entry => string.Equals(FileOf(entry), wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A reference's path with its heading and any leading <c>./</c> removed,
    /// and back slashes folded — the spelling two references to the same file agree
    /// on. The devbook layout's <c>.devbook/arc42/…</c> folds to the same spelling
    /// as <c>.arc42/…</c>, so a view and a chapter match whichever layout wrote
    /// either.</summary>
    internal static string FileOf(string reference)
    {
        var path = DevbookLayout.ConventionalPath(reference).TrimStart('.', '/');
        var hash = path.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? path : path[..hash];
    }
}

/// <summary>A problem, and which workspace it came out of.</summary>
public sealed record C4WorkspaceProblem(string WorkspaceFile, C4Problem Problem);

/// <summary>
/// One <c>.dsl</c>, parsed, with the reference map that applies to it.
/// <para>
/// Held beside the flattened <see cref="C4ViewEntry"/> list rather than instead of
/// it, because the two answer different questions. A chapter reference resolves
/// against a view entry — one reference, one picture — and the explorer works on the
/// model, which a list of pictures is not.
/// </para>
/// </summary>
/// <param name="Derived">Whether this model was drawn from the chapters' C4 fences
/// rather than read from an authored <c>.dsl</c>. The panel says so, because a
/// derived model is only as complete as the fences are.</param>
public sealed record C4WorkspaceEntry(
    string File,
    string Name,
    C4Workspace Workspace,
    IReadOnlyList<C4Problem> Problems,
    IReadOnlyDictionary<string, string[]> References,
    bool Derived = false)
{
    /// <summary>The chapters a view of this workspace documents, as
    /// <c>references.json</c> states them.</summary>
    public IReadOnlyList<string> Documents(string? viewKey) =>
        viewKey is not null && References.TryGetValue(viewKey, out var chapters) ? chapters : [];
}

/// <param name="Available">Whether the architecture folder resolved at all. False
/// is also what an switched-off feature looks like, and the two are the same to a
/// panel: there is nothing to draw either way.</param>
/// <param name="Workspaces">The parsed models, one per <c>.dsl</c>. What the
/// explorer is given.</param>
public sealed record C4Catalog(
    bool Available,
    string? Directory,
    IReadOnlyList<C4ViewEntry> Views,
    IReadOnlyList<C4WorkspaceProblem> Problems,
    IReadOnlyList<C4WorkspaceEntry> Workspaces)
{
    public static C4Catalog Off { get; } = new(false, null, [], [], []);

    public bool HasViews => Views.Count > 0;

    /// <summary>The workspace a view belongs to, or null. What the explorer is opened
    /// on when a chapter reference names a view.</summary>
    public C4WorkspaceEntry? WorkspaceOf(C4ViewEntry? view) =>
        view is null
            ? null
            : Workspaces.FirstOrDefault(entry => string.Equals(entry.File, view.WorkspaceFile, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The view a reference names, or null.
    /// <para>
    /// A reference has to name a <c>.dsl</c> workspace before anything is matched.
    /// Falling back to the anchor alone would be the obvious tolerance and it is
    /// wrong: a chapter section anchor and a view key are both slugs, so
    /// <c>05-building-block-view.md#container-view</c> would open the container view
    /// instead of scrolling the chapter — a reference that silently goes somewhere
    /// else. Once the path says <c>.dsl</c> there is no such ambiguity, and the
    /// anchor is then matched loosely enough to tolerate a spelling of the key.
    /// </para>
    /// </summary>
    public C4ViewEntry? Find(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;

        var normalized = reference.Trim().Replace('\\', '/').TrimStart('.', '/');
        var hash = normalized.IndexOf('#', StringComparison.Ordinal);
        var path = hash < 0 ? normalized : normalized[..hash];
        var anchor = hash < 0 ? null : normalized[(hash + 1)..];

        if (!path.EndsWith(".dsl", StringComparison.OrdinalIgnoreCase)) return null;

        return Views.FirstOrDefault(view => view.Reference.EndsWith(normalized, StringComparison.OrdinalIgnoreCase))
            ?? Views.FirstOrDefault(view => anchor is not null
                && path.EndsWith(view.WorkspaceFile, StringComparison.OrdinalIgnoreCase)
                && string.Equals(C4Slug.Of(view.Key), C4Slug.Of(anchor), StringComparison.OrdinalIgnoreCase));
    }
}
