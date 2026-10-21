using System.Collections.Concurrent;
using System.Globalization;

using Backlog.Infrastructure.Devbook.Scenarios;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.UI.Components.Devbook;

using Microsoft.AspNetCore.Components;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The journeys of one repository's domain area, each with its last run and the state
/// the dot shows: what the Devbook menu marks on a scenarios file, what a scenarios file
/// shows above its body, and what a <c>scenario:&lt;scenarios file&gt;#&lt;label&gt;</c>
/// image in any chapter resolves to.
/// <para>
/// Which journeys there are comes from devbook's committed register,
/// <c>_meta/scenarios.json</c> (BL1), when the repository has one at schema version 2 —
/// the domain folder's own, else the repository-wide rollup under <c>.devbook/_meta/</c>.
/// A repository that commits no register is not left without: every scenarios file —
/// <c>scenarios.md</c>, or a split <c>scenarios.&lt;name&gt;.md</c> — says so itself, in
/// each journey's <c>type: scenario</c>, and the domain folder is read for them once per
/// load. Either way each file is then read, because the state is computed rather than
/// stored — the journey's signature now, by devbook's algorithm, against the one its
/// committed <c>run.json</c> executed.
/// </para>
/// <para>
/// One catalog per repository, kept until the folder source says the content moved:
/// the menu, the panel and every image in the pane ask the same question of the same
/// files, and answering it once is the point of keeping it here.
/// </para>
/// </summary>
public sealed class DevbookScenarioStore
{
    private readonly IDevbookFolderSource _source;
    private readonly ConcurrentDictionary<string, Task<DevbookScenarioCatalog>> _catalogs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _images = new(StringComparer.OrdinalIgnoreCase);

    public DevbookScenarioStore(IDevbookFolderSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _source.Changed += Invalidate;
    }

    /// <summary>Raised when a catalog was dropped, so whoever drew one asks again.</summary>
    public event Action? Changed;

    /// <summary>Forgets every catalog and image; the next ask reads the files again.</summary>
    public void Invalidate()
    {
        _catalogs.Clear();
        _images.Clear();
        Changed?.Invoke();
    }

    /// <summary>The catalog for a repository, read once and kept. A read that fails
    /// is not kept, so the next ask tries again.</summary>
    public async Task<DevbookScenarioCatalog> LoadAsync(string? repositoryAlias, CancellationToken cancellationToken = default)
    {
        var key = repositoryAlias ?? string.Empty;
        var task = _catalogs.GetOrAdd(key, _ => ReadAsync(repositoryAlias));
        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Whatever the files did — a register path that does not parse as a path
            // included — the pane draws its pages as plain pages rather than failing,
            // and the next ask reads them again instead of meeting the same fault.
            _catalogs.TryRemove(new KeyValuePair<string, Task<DevbookScenarioCatalog>>(key, task));
            return DevbookScenarioCatalog.Empty;
        }
    }

    /// <summary>The largest screenshot drawn. A capture is tens or hundreds of
    /// kilobytes; anything far past that is not one, and inlining it would put it in
    /// the page whole.</summary>
    public const long MaxImageBytes = 8 * 1024 * 1024;

    /// <summary>
    /// A screenshot as a <c>data:</c> URI, read off the pool and kept. Null when the
    /// file is not there — a label the run never captured — or too large to be one.
    /// On a branch the file is fetched first: the catalog fetches runs, not pictures,
    /// so only the screenshots somebody looks at come down.
    /// </summary>
    public async Task<string?> ImageAsync(DevbookScenario scenario, string? fullPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        if (string.IsNullOrWhiteSpace(fullPath)) return null;
        if (_images.TryGetValue(fullPath, out var cached)) return cached;

        if (!ScenarioFiles.Exists(fullPath) && scenario.FromBranch)
        {
            await _source.PrepareContentAsync(".domain", scenario.RepositoryAlias, [AnyDepth(scenario.RepositoryRoot, fullPath)], cancellationToken).ConfigureAwait(false);
        }

        var uri = await ScenarioFiles.ReadImageAsync(fullPath, MaxImageBytes, cancellationToken).ConfigureAwait(false);
        if (uri is not null) _images[fullPath] = uri;
        return uri;
    }

    /// <summary>The image types a run's screenshot may be. Anything else a run names
    /// is not drawn.</summary>
    public static bool IsImageFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp";

    private async Task<DevbookScenarioCatalog> ReadAsync(string? repositoryAlias)
    {
        var location = await _source.PrepareContentAsync(".domain", repositoryAlias).ConfigureAwait(false);
        if (!location.Available || location.FullPath is not { } domainRoot || location.RootPath is not { } repositoryRoot)
        {
            return DevbookScenarioCatalog.Empty;
        }

        var tree = _source.FileTree(location);

        // The repository-wide rollup sits outside the domain folder, so a branch has
        // to be asked for it; the domain folder's own came with the folder.
        var rollup = Path.Combine(repositoryRoot, ".devbook", "_meta", ScenarioRegister.FileName);
        if (location.Source is DevbookSourceKind.Branch && tree.FileExists(rollup))
        {
            await _source.PrepareContentAsync(".domain", repositoryAlias, [AnyDepth(repositoryRoot, rollup)]).ConfigureAwait(false);
        }

        return await Task.Run(async () =>
        {
            var register = ScenarioRegister.TryRead(Path.Combine(domainRoot, "_meta", ScenarioRegister.FileName))
                ?? ScenarioRegister.TryRead(rollup);
            var listed = register?.Scenarios.Select(entry => Normalize(entry.Address)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Each scenarios file read once: the register names the files, or — with
            // none — every file in the folder named as a scenarios file is read. A
            // journey is kept when its block says it is one, and, with a register,
            // when the register lists it.
            var journeys = (register is not null
                    ? register.Scenarios
                        .Select(entry => FullPathWithin(repositoryRoot, domainRoot, entry.Path))
                        .OfType<string>()
                        .Where(ScenarioFiles.Exists)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                    : ScenarioFiles.MarkdownFiles(domainRoot).Where(ScenarioPageParser.IsScenariosFileName))
                .SelectMany(path => ScenarioFiles.ReadFile(path, Relative(repositoryRoot, path)).Scenarios
                    .Select(journey => (FullPath: path, Journey: journey)))
                .Where(entry => listed is null || listed.Contains(Normalize(entry.Journey.Address)))
                .ToList();
            if (journeys.Count == 0) return DevbookScenarioCatalog.Empty with { FromRegister = register is not null };

            var scenarioFolder = ScenarioFolderOf(tree, repositoryRoot);
            var fromBranch = location.Source is DevbookSourceKind.Branch;

            // A branch holds the runs and the data sets nowhere until they are asked
            // for, and their names are only known from the branch's own listing. The
            // screenshots are not fetched here: ImageAsync fetches the ones drawn.
            if (fromBranch)
            {
                var wanted = journeys
                    .SelectMany(entry => new[] { Path.Combine(RunFolderOf(repositoryRoot, entry.Journey), "run.json") }
                        .Concat(entry.Journey.Setup.Data
                            .Select(ScenarioPageParser.Collapse)
                            .Where(ScenarioSignature.IsDataSetName)
                            .Select(name => Path.Combine(repositoryRoot, scenarioFolder, "data", name))
                            .Where(tree.DirectoryExists)
                            .SelectMany(folder => tree.EnumerateFiles(folder, "*", recursive: true))))
                    .Where(tree.FileExists)
                    .Select(file => AnyDepth(repositoryRoot, file))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (wanted.Count > 0) await _source.PrepareContentAsync(".domain", repositoryAlias, wanted).ConfigureAwait(false);
            }

            var scenarios = journeys
                .Select(entry =>
                {
                    var runFolder = RunFolderOf(repositoryRoot, entry.Journey);
                    var run = ScenarioFiles.ReadRun(runFolder);

                    // A run that names the journey it executed belongs to that journey alone.
                    if (run?.Journey is { } ranJourney && !SamePath(ranJourney, entry.Journey.Address)) run = null;

                    var signature = ScenarioSignature.OfJourney(entry.Journey, repositoryRoot, scenarioFolder);
                    return new DevbookScenario(
                        entry.Journey,
                        Relative(domainRoot, entry.FullPath),
                        signature,
                        run,
                        ScenarioEvidence.StateOf(entry.Journey, signature, run),
                        runFolder)
                    {
                        RepositoryAlias = repositoryAlias,
                        RepositoryRoot = repositoryRoot,
                        FromBranch = fromBranch
                    };
                })
                .OrderBy(scenario => scenario.Journey.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(scenario => scenario.Journey.Line)
                .ToList();

            return new DevbookScenarioCatalog(scenarios, register is not null);
        }).ConfigureAwait(false);
    }

    /// <summary>A journey's run folder on disk: beside its scenarios file, named for
    /// the journey.</summary>
    private static string RunFolderOf(string repositoryRoot, ScenarioJourney journey) =>
        Path.Combine(repositoryRoot, journey.RunFolder.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The scenario folder the data sets are in: <c>.devbook/scenarios</c>, or
    /// the classic layout's <c>.domain/_tests</c> when only that one exists.</summary>
    private static string ScenarioFolderOf(IDevbookFileTree tree, string repositoryRoot)
    {
        if (tree.DirectoryExists(Path.Combine(repositoryRoot, ScenarioPageParser.ScenarioFolder))) return ScenarioPageParser.ScenarioFolder;
        return tree.DirectoryExists(Path.Combine(repositoryRoot, ScenarioPageParser.LegacyScenarioFolder))
            ? ScenarioPageParser.LegacyScenarioFolder
            : ScenarioPageParser.ScenarioFolder;
    }

    /// <summary>A register path as a full path, or null when it does not land inside
    /// the domain folder — a register is committed text, and a path out of the folder
    /// names no scenarios file.</summary>
    private static string? FullPathWithin(string repositoryRoot, string domainRoot, string relative)
    {
        try
        {
            if (Path.IsPathRooted(relative)) return null;
            var full = Path.GetFullPath(Path.Combine(repositoryRoot, relative));
            return IsWithin(domainRoot, full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path)
    {
        var forward = path.Trim().Replace('\\', '/');
        while (forward.StartsWith("./", StringComparison.Ordinal)) forward = forward[2..];
        return forward.TrimStart('/');
    }

    internal static string AnyDepth(string repositoryRoot, string fullPath) => "**/" + Relative(repositoryRoot, fullPath);

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>Whether <paramref name="path"/>, a full path, is inside
    /// <paramref name="root"/>.</summary>
    internal static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith("../", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }
}

/// <summary>One journey with its last run.</summary>
/// <param name="DomainPath">The path of the scenarios file the journey is in, inside
/// the domain folder — <c>work/scenarios.md</c> — which is how the menu and the panel
/// name it.</param>
/// <param name="Signature">The journey's signature now.</param>
/// <param name="RunFolder">Where its run lives on disk: beside its scenarios file,
/// <c>&lt;scenarios file without .md&gt;/&lt;journey&gt;/</c>.</param>
public sealed record DevbookScenario(
    ScenarioJourney Journey,
    string DomainPath,
    string Signature,
    ScenarioRun? Run,
    ScenarioState State,
    string RunFolder)
{
    /// <summary>The journey's anchor in its scenarios file.</summary>
    public string Slug => Journey.Slug;

    public string Title => Journey.Title;

    /// <summary>The part's title as the journey writes it, or the anchor.</summary>
    public string PartTitle(string? anchor) =>
        Journey.Parts.FirstOrDefault(part => string.Equals(part.Anchor, anchor, StringComparison.OrdinalIgnoreCase))?.Title ?? anchor ?? string.Empty;

    /// <summary>The repository the page was read from, as the store was asked for it.</summary>
    public string? RepositoryAlias { get; init; }

    /// <summary>The repository root the paths here are under.</summary>
    public string RepositoryRoot { get; init; } = string.Empty;

    /// <summary>Whether the page was read from a branch snapshot, whose files are
    /// fetched only when asked for.</summary>
    public bool FromBranch { get; init; }

    /// <summary>The file a label's last capture landed in, or null.</summary>
    public string? ShotFile(string label) =>
        Run?.Shots.TryGetValue(label, out var shot) == true ? InRunFolder(shot.File) : null;

    /// <summary>The failure screenshot of a part, or null.</summary>
    public string? FailureFile(string anchor) =>
        Run?.Part(anchor)?.FailureShot is { Length: > 0 } file ? InRunFolder(file) : null;

    /// <summary>
    /// A file the run names, as a full path inside the run's folder — or null. A run
    /// is committed text anybody can push, so a name that is rooted, climbs out of the
    /// folder, or is not an image is no screenshot, and nothing is read for it.
    /// </summary>
    private string? InRunFolder(string relative)
    {
        try
        {
            var normalized = relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized)) return null;

            var full = Path.GetFullPath(Path.Combine(RunFolder, normalized));
            return DevbookScenarioStore.IsWithin(RunFolder, full) && DevbookScenarioStore.IsImageFile(full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>"2 parts · 3 screenshots · 1 of 2 failed · ran 7 Oct 2026 08:30" — the
    /// E2E summary line.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>
            {
                Count(Journey.Parts.Count, "part"),
                Count(Journey.Labels.Count, "screenshot")
            };
            if (Run is { } run)
            {
                var failed = Journey.Parts.Count(part => run.Part(part.Anchor)?.Outcome == ScenarioOutcome.Failed);
                if (failed > 0) parts.Add($"{failed} of {Journey.Parts.Count} failed");
                if (run.RanAt is { } ranAt) parts.Add($"ran {DevbookScenarioFormat.Time(ranAt)}");
                if (run.Version == 1) parts.Add("version 1 run");
            }
            else
            {
                parts.Add("never run");
            }

            return string.Join(" · ", parts);
        }
    }

    private static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? string.Empty : "s")}";
}

/// <summary>The journeys of one repository.</summary>
public sealed record DevbookScenarioCatalog(IReadOnlyList<DevbookScenario> Scenarios, bool FromRegister = false)
{
    public static DevbookScenarioCatalog Empty { get; } = new([]);

    /// <summary>The journeys of the scenarios file a document or menu path names, in
    /// file order; empty for any other path.</summary>
    public IReadOnlyList<DevbookScenario> ForPath(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? []
            : [.. Scenarios.Where(scenario => DevbookPath.Matches(path, scenario.DomainPath))];

    /// <summary>
    /// What an image target in a chapter shows: <c>scenario:&lt;scenarios file&gt;#&lt;label&gt;</c>
    /// anywhere — the file as a path inside the domain folder, <c>work/scenarios.md</c> —
    /// or <c>shot:&lt;label&gt;</c> in the scenarios file itself. A label is unique in
    /// its file, so it names the one journey it sits in. Null for any other target, so
    /// the chapter's own image rules apply.
    /// </summary>
    public DevbookScenarioShotResolution? Resolve(string target, string? documentPath)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        var trimmed = target.Trim();

        string file;
        string label;
        IReadOnlyList<DevbookScenario> journeys;
        if (trimmed.StartsWith("scenario:", StringComparison.OrdinalIgnoreCase))
        {
            var reference = trimmed["scenario:".Length..];
            var hash = reference.IndexOf('#', StringComparison.Ordinal);
            file = (hash < 0 ? reference : reference[..hash]).Trim();
            label = hash < 0 ? string.Empty : reference[(hash + 1)..].Trim();

            journeys = ForPath(file);
            if (journeys.Count == 0) return new DevbookScenarioShotResolution(DevbookScenarioShotKind.UnknownPage, file, label, null, []);
        }
        else if (trimmed.StartsWith("shot:", StringComparison.OrdinalIgnoreCase))
        {
            journeys = ForPath(documentPath);
            if (journeys.Count == 0) return null;
            file = journeys[0].DomainPath;
            label = trimmed["shot:".Length..].Trim();
        }
        else
        {
            return null;
        }

        var labels = journeys.SelectMany(journey => journey.Journey.Labels).Distinct(StringComparer.Ordinal).ToList();
        if (journeys.FirstOrDefault(journey => journey.Journey.Labels.Contains(label, StringComparer.Ordinal)) is not { } scenario)
        {
            return new DevbookScenarioShotResolution(DevbookScenarioShotKind.UnknownLabel, file, label, journeys[0], labels);
        }

        return new DevbookScenarioShotResolution(
            scenario.ShotFile(label) is null ? DevbookScenarioShotKind.NotCaptured : DevbookScenarioShotKind.Shown,
            file,
            label,
            scenario,
            labels);
    }
}

public enum DevbookScenarioShotKind
{
    Shown,
    NotCaptured,
    UnknownLabel,
    UnknownPage
}

/// <param name="File">The scenarios file the target named, inside the domain folder.</param>
/// <param name="Scenario">The journey the label sits in; for an unknown label, the
/// file's first journey.</param>
/// <param name="Alternatives">The labels the scenarios file has.</param>
public sealed record DevbookScenarioShotResolution(
    DevbookScenarioShotKind Kind,
    string File,
    string Label,
    DevbookScenario? Scenario,
    IReadOnlyList<string> Alternatives)
{
    public ScenarioRunShot? Shot =>
        Scenario?.Run?.Shots.TryGetValue(Label, out var shot) == true ? shot : null;

    /// <summary>Whether the picture is from an earlier run than the last: a label the
    /// last run did not reach keeps the file it had.</summary>
    public bool FromEarlierRun =>
        Shot?.RanAt is { } shotAt && Scenario?.Run?.RanAt is { } runAt && shotAt < runAt;

    public string? FullPath => Kind == DevbookScenarioShotKind.Shown ? Scenario?.ShotFile(Label) : null;
}

/// <summary>
/// The image source the pane cascades to every chapter: a <c>scenario:</c> or
/// <c>shot:</c> image becomes the screenshot with its source line, and every other
/// image keeps the chapter's own rule.
/// <para>
/// One instance per pane, cascaded as fixed, with the catalog swapped inside it. A
/// cascaded value that is not fixed re-renders every chapter view under it on every
/// render of the pane — a reference type always reads as possibly changed — and that
/// took a remark being typed in a chapter away with it.
/// </para>
/// </summary>
public sealed class DevbookScenarioImages(DevbookScenarioStore? store) : IDevbookImageSource
{
    public DevbookScenarioCatalog Catalog { get; set; } = DevbookScenarioCatalog.Empty;

    public RenderFragment? Image(string target, string caption, string? documentPath)
    {
        if (Catalog.Resolve(target, documentPath) is not { } resolution) return null;

        return builder =>
        {
            builder.OpenComponent<DevbookScenarioShot>(0);
            builder.AddComponentParameter(1, nameof(DevbookScenarioShot.Resolution), resolution);
            builder.AddComponentParameter(2, nameof(DevbookScenarioShot.Caption), caption);
            builder.AddComponentParameter(3, nameof(DevbookScenarioShot.Store), store);
            builder.CloseComponent();
        };
    }
}

public static class DevbookScenarioFormat
{
    /// <summary>A run's time as the pane writes it, in the reader's own zone.</summary>
    public static string Time(DateTimeOffset time) =>
        time.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture);
}
