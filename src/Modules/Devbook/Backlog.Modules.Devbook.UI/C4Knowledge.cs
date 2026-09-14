using System.Text.Json;

using Backlog.Modules.Knowledge.Abstractions;
using Backlog.SharedKernel;
using Backlog.UI.Components.Diagrams.C4;

namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// The C4 model that sits beside the architecture chapters.
/// <para>
/// Everything the pure reader deliberately does not know is here: whether the
/// feature is switched on, which repository clone the chapters came from, and where
/// <c>_c4/</c> sits inside it. The reader is handed text and answers with a
/// workspace; this is where that becomes a question about files.
/// </para>
/// <para>
/// Nothing here is generated. A workspace is one authored <c>.dsl</c> — written in
/// c4hero, which is a browser editor rather than anything this app runs — plus an
/// authored <c>references.json</c> saying which chapter each view documents. The
/// views are read out of the DSL on load and written as mermaid on the spot.
/// </para>
/// <para>
/// That is the whole difference from the Archify arrangement next door. An Archify
/// artifact is a re-authoring of a mermaid fence with nothing in it pointing back,
/// so it has to be matched to that fence by hash and filed in a generated index, and
/// it can go stale. A C4 workspace is attached to no fence, has no committed
/// rendering, and therefore has nothing to drift from.
/// </para>
/// </summary>
public sealed class C4KnowledgeStore : IDisposable
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
    /// <c>related:</c> list is refused by the knowledge-meta generator, which resolves
    /// references against <c>.md</c> nodes only and treats anything under
    /// <c>.arc42/</c> as an error rather than a warning — and that generator is an
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

    private readonly IAppFeatureSettings _features;
    private readonly IKnowledgeFolderSource _folders;
    private readonly object _gate = new();

    private string? _cachedFor;
    private C4Catalog? _cached;

    public C4KnowledgeStore(IAppFeatureSettings features, IKnowledgeFolderSource folders)
    {
        _features = features;
        _folders = folders;

        _folders.Changed += Invalidate;
        _features.Changed += Invalidate;
    }

    public event Action? Changed;

    /// <summary>Whether the feature is on at all. Asked before anything is drawn, so
    /// a panel with the feature off is the panel it was before this existed.</summary>
    public bool Enabled => _features.IsEnabled(KnowledgeFeatures.C4Diagrams);

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
    public Task<C4Catalog> LoadAsync(string? repositoryAlias = null)
    {
        if (!Enabled) return Task.FromResult(C4Catalog.Off);

        var key = repositoryAlias ?? string.Empty;

        lock (_gate)
        {
            if (_cached is not null && string.Equals(_cachedFor, key, StringComparison.Ordinal))
            {
                return Task.FromResult(_cached);
            }
        }

        var catalog = Read(repositoryAlias);

        lock (_gate)
        {
            _cachedFor = key;
            _cached = catalog;
        }

        return Task.FromResult(catalog);
    }

    private C4Catalog Read(string? repositoryAlias)
    {
        var location = _folders.Resolve(FolderKey, repositoryAlias);
        if (!location.Available || location.FullPath is null) return C4Catalog.Off;

        var directory = Path.Combine(location.FullPath, WorkspaceDirectory);
        if (!Directory.Exists(directory)) return new C4Catalog(true, directory, [], [], []);

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

    /// <summary>How a view is addressed: the workspace path as the repository sees it,
    /// then the view key as the anchor. The same shape every other knowledge reference
    /// has, which is what lets one travel through <c>KnowledgeChapterLink</c> and the
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

    private void Invalidate()
    {
        lock (_gate)
        {
            _cached = null;
            _cachedFor = null;
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
/// <c>references.json</c> states them. The authored half of the reference; the other
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
    /// on.</summary>
    internal static string FileOf(string reference)
    {
        var path = reference.Trim().Replace('\\', '/').TrimStart('.', '/');
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
public sealed record C4WorkspaceEntry(
    string File,
    string Name,
    C4Workspace Workspace,
    IReadOnlyList<C4Problem> Problems,
    IReadOnlyDictionary<string, string[]> References)
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
