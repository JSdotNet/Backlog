using System.Collections.Concurrent;

using Backlog.Infrastructure.Devbook.Building;
using Backlog.Infrastructure.Devbook.Scenarios;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.Devbook;

/// <summary>
/// <see cref="IDevbookReferenceResolver"/> over a repository's devbook: the
/// generated database first, the Markdown where there is none.
///
/// <para><b>Why here.</b> The port is Tasks' and the devbook is Devbook's, and a
/// Tasks screen may ask only its own module. This project already reads both the
/// database and the folder port, and the slug a heading is addressed by
/// (<see cref="DevbookMarkdown.Slugify"/>) is the generator's rule ported here —
/// a second copy anywhere else would be a second opinion about which anchor a
/// heading has.</para>
///
/// <para><b>The ladder is ADR 0004's.</b> A page the database has rows for is
/// answered from them. A page it has none for — no database yet, a file added
/// since the build, a file with no headings — is read from its folder through
/// <see cref="IDevbookFolderSource.PrepareContentAsync"/>, as the MCP chapter
/// read does, and its headings parsed with the generator's own parse. Only when
/// nobody can look — no repository, a folder that is not readable right now —
/// is the answer <see cref="DevbookReferenceState.Unverified"/>: that is not a
/// broken link, and saying it was would send a person off to fix one.</para>
///
/// <para><b>Nothing here throws for a reference.</b> A value that is no reference
/// at all comes back <see cref="DevbookReferenceState.UnknownPage"/> titled with
/// itself, so a task carrying one still draws its other chips.</para>
/// </summary>
public sealed class DevbookReferenceResolver(IDevbookFolderSource folders) : IDevbookReferenceResolver
{
    private readonly IDevbookFolderSource _folders = folders ?? throw new ArgumentNullException(nameof(folders));

    /// <summary>
    /// What each reference points at now, in the order given.
    /// <para>
    /// Run on the thread pool, not on the caller's: the task panel asks from its
    /// render, on the UI's dispatcher, and everything up to the first file read —
    /// the folder settings, opening the database, a query per reference — used to
    /// run right there and hold the render while it did. The token is honoured
    /// before the work starts and between references.
    /// </para>
    /// </summary>
    public Task<IReadOnlyList<ResolvedDevbookReference>> ResolveAsync(
        string? repositoryAlias,
        IReadOnlyList<string> references,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(references);

        return Task.Run(() => ResolveOffTheCallerAsync(repositoryAlias, references, cancellationToken), cancellationToken);
    }

    private async Task<IReadOnlyList<ResolvedDevbookReference>> ResolveOffTheCallerAsync(
        string? repositoryAlias,
        IReadOnlyList<string> references,
        CancellationToken cancellationToken)
    {
        if (references.Count == 0) return [];

        var alias = string.IsNullOrWhiteSpace(repositoryAlias) ? null : repositoryAlias.Trim();
        var areas = alias is null ? [] : Areas(_folders.Folders(alias));

        // Opened once for the whole list and only when something can be asked of
        // it: open, ask, dispose, as every reader of the file does.
        using var database = areas.Count == 0 ? null : DevbookDatabaseSource.TryOpen(_folders, alias);

        var answers = new List<ResolvedDevbookReference>(references.Count);
        var scenarios = new ScenarioReads();

        foreach (var reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var answer = await ResolveOneAsync(reference ?? string.Empty, alias, areas, database, cancellationToken).ConfigureAwait(false);
            answers.Add(await WithScenarioPartsAsync(answer, alias, areas, scenarios, cancellationToken).ConfigureAwait(false));
        }

        return answers;
    }

    /// <summary>
    /// Every page and chapter the repository's devbook offers a picker, in reading
    /// order: each page, then its chapters.
    /// <para>
    /// On the thread pool for the reason <see cref="ResolveAsync"/> is, and asked
    /// again on every picker opened and every retry of an empty answer, so the
    /// headings are read in one query for the whole repository
    /// (<see cref="DevbookDatabase.ChapterHeadings"/>) rather than one per page.
    /// </para>
    /// </summary>
    public Task<IReadOnlyList<DevbookReferenceTarget>> ListTargetsAsync(
        string? repositoryAlias,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryAlias)) return Task.FromResult<IReadOnlyList<DevbookReferenceTarget>>([]);

        var alias = repositoryAlias.Trim();
        return Task.Run(() => ListTargets(alias, cancellationToken), cancellationToken);
    }

    private IReadOnlyList<DevbookReferenceTarget> ListTargets(string alias, CancellationToken cancellationToken)
    {
        var areas = Areas(_folders.Folders(alias));
        if (areas.Count == 0) return [];

        using var database = DevbookDatabaseSource.TryOpen(_folders, alias);
        if (database is null) return [];

        var headings = database.ChapterHeadings();
        var targets = new List<DevbookReferenceTarget>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // The repository scope's outline is every folder's reading order, parents
        // before children; its file rows are the pages in the order a reader meets
        // them.
        foreach (var row in database.Outline(DevbookDatabaseSchema.RepositoryScope))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.Equals(row.Type, "file", StringComparison.Ordinal)) continue;
            if (Match(areas, row.Path) is not { } area) continue;

            var chapters = headings[row.Path].ToList();
            var title = PageTitle(chapters.Select(Heading.FromHeadingRow).ToList()) ?? row.Title ?? row.Name;

            if (seen.Add(row.Path)) targets.Add(new DevbookReferenceTarget(row.Path, title, area.Folder, 0, null));

            // The page's own level-1 heading is the page; offering it again as a
            // chapter would put the same thing in the picker twice.
            var skippedTitle = false;

            foreach (var chapter in chapters)
            {
                if (!skippedTitle && chapter.Level == 1)
                {
                    skippedTitle = true;
                    continue;
                }

                var reference = $"{row.Path}#{chapter.Slug}";
                if (!seen.Add(reference)) continue;

                targets.Add(new DevbookReferenceTarget(reference, chapter.Title ?? chapter.Slug, area.Folder, chapter.Level, chapter.Status));
            }
        }

        return targets;
    }

    private async Task<ResolvedDevbookReference> ResolveOneAsync(
        string reference,
        string? alias,
        IReadOnlyList<Area> areas,
        DevbookDatabase? database,
        CancellationToken cancellationToken)
    {
        if (Parse(reference) is not { } parsed)
        {
            return new ResolvedDevbookReference(reference, reference, null, DevbookReferenceState.UnknownPage, reference, null, null);
        }

        var (path, anchor) = parsed;

        if (alias is null || areas.Count == 0)
        {
            return Answer(DevbookReferenceState.Unverified, title: null, status: null, folder: null);
        }

        if (Match(areas, path) is not { } area)
        {
            return Answer(DevbookReferenceState.OutsideDevbook, title: null, status: null, folder: null);
        }

        var headings = FromDatabase(database, path) ?? await FromFileAsync(area, alias, path, cancellationToken).ConfigureAwait(false);

        return headings switch
        {
            Lookup.Unreadable => Answer(DevbookReferenceState.Unverified, null, null, area.Folder),
            Lookup.Missing => Answer(DevbookReferenceState.UnknownPage, null, null, area.Folder),
            Lookup.Found found => FromHeadings(found.Headings, area.Folder),
            _ => throw new InvalidOperationException("Every lookup is one of three.")
        };

        ResolvedDevbookReference FromHeadings(IReadOnlyList<Heading> found, string folder)
        {
            var pageTitle = PageTitle(found);

            if (anchor is null) return Answer(DevbookReferenceState.Page, pageTitle, null, folder);

            return FindHeading(found, anchor) is { } heading
                ? Answer(DevbookReferenceState.Chapter, heading.Text, heading.Status, folder)
                : Answer(DevbookReferenceState.UnknownHeading, pageTitle, null, folder);
        }

        ResolvedDevbookReference Answer(DevbookReferenceState state, string? title, string? status, string? folder) =>
            new(reference, path, anchor, state, string.IsNullOrWhiteSpace(title) ? reference : title, status, folder);
    }

    // --- Scenario parts ------------------------------------------------------

    /// <summary>
    /// The answer with the scenario parts it stands for (BL in the scenario-pages
    /// plan): a part of a scenario page, every part of one, or the parts a
    /// requirement chapter's <c>Proved by:</c> lines name — each with its state
    /// against the page's last run.
    /// <para>
    /// Only a found page in the domain folder is read for it, because that is where
    /// scenario pages and requirements live, and the page is read from its file even
    /// when the database answered the headings: whether it is a scenario page, its
    /// parts and its signature come from its text, and the database keeps none of
    /// them. Nothing here fails the reference — a page that cannot be read, a run
    /// that does not parse, a pointer to nowhere is a reference with no parts.
    /// </para>
    /// </summary>
    private async Task<ResolvedDevbookReference> WithScenarioPartsAsync(
        ResolvedDevbookReference answer,
        string? alias,
        IReadOnlyList<Area> areas,
        ScenarioReads reads,
        CancellationToken cancellationToken)
    {
        if (alias is null || answer.State is not (DevbookReferenceState.Chapter or DevbookReferenceState.Page)) return answer;
        if (Match(areas, answer.Path) is not { Folder: DomainFolder } area) return answer;

        try
        {
            var parts = await ScenarioPartsAsync(area, alias, answer.Path, answer.Anchor, areas, reads, cancellationToken).ConfigureAwait(false);
            return parts.Count == 0 ? answer : answer with { ScenarioParts = parts };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return answer;
        }
    }

    private const string DomainFolder = "domain";

    private async Task<IReadOnlyList<ScenarioPartEvidence>> ScenarioPartsAsync(
        Area area,
        string alias,
        string path,
        string? anchor,
        IReadOnlyList<Area> areas,
        ScenarioReads reads,
        CancellationToken cancellationToken)
    {
        if (await ReadDomainPageAsync(area, alias, path, reads, cancellationToken).ConfigureAwait(false) is not { } read) return [];

        if (read.Scenario is { } scenario)
        {
            var parts = anchor is null
                ? scenario.Parts
                : ScenarioReferences.FindPart(scenario, anchor) is { } part ? [part] : [];
            return await EvidenceAsync(read, parts, reads, cancellationToken).ConfigureAwait(false);
        }

        // Not a scenario page: a requirement, whose cases point at the parts that
        // prove them.
        var evidence = new List<ScenarioPartEvidence>();
        foreach (var target in ScenarioReferences.ProvedByTargets(read.Markdown, anchor))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ScenarioReferences.ResolveTarget(read.Path, target) is not { } pointer) continue;

            // One target that cannot be read costs that target, not the others.
            try
            {
                var pagePath = pointer.ByStem
                    ? await FindByStemAsync(area, alias, pointer.Page, reads, cancellationToken).ConfigureAwait(false)
                    : pointer.Page;
                if (pagePath is null || Match(areas, pagePath) is not { Folder: DomainFolder } targetArea) continue;

                if (await ReadDomainPageAsync(targetArea, alias, pagePath, reads, cancellationToken).ConfigureAwait(false) is not { Scenario: { } page } targetRead) continue;
                if (ScenarioReferences.FindPart(page, pointer.Anchor) is not { } part) continue;

                evidence.AddRange(await EvidenceAsync(targetRead, [part], reads, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Skipped: the requirement still lists the parts it could read.
            }
        }

        return evidence;
    }

    /// <summary>A domain page read from its file, once per resolve, or null when it
    /// is not there or cannot be read.</summary>
    private async Task<ScenarioRead?> ReadDomainPageAsync(
        Area area,
        string alias,
        string path,
        ScenarioReads reads,
        CancellationToken cancellationToken)
    {
        if (reads.Pages.TryGetValue(path, out var known)) return known;

        ScenarioRead? read = null;
        var relative = path[(area.Prefix.Length + 1)..];
        if (relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            var location = await _folders
                .PrepareContentAsync(area.Setting.Key, alias, [relative], cancellationToken)
                .ConfigureAwait(false);

            if (location.Available
                && !string.IsNullOrWhiteSpace(location.FullPath)
                && !string.IsNullOrWhiteSpace(location.RootPath)
                && ResolveWithin(location.FullPath, relative) is { } file
                && File.Exists(file))
            {
                // The page under the path its file has, whichever spelling the
                // reference used: a part is one part however it was reached, and
                // a run names the page where it is.
                var actual = Path.GetRelativePath(location.RootPath, file).Replace(Path.DirectorySeparatorChar, '/');
                var (markdown, page) = await ParsedAsync(file, actual, cancellationToken).ConfigureAwait(false);
                read = new ScenarioRead(area, alias, location, actual, markdown, page.IsScenario ? page : null);
            }
        }

        reads.Pages[path] = read;
        return read;
    }

    /// <summary>
    /// A page's text and its parse, kept until the file changes: the task panel asks
    /// on every open, and a page nobody edited reads the same as last time. Keyed by
    /// the full path, the write time and the length, so an edit — or a branch fetch
    /// that replaced the file — is read again.
    /// </summary>
    private async Task<(string Markdown, ScenarioPage Page)> ParsedAsync(string file, string relativePath, CancellationToken cancellationToken)
    {
        var info = new FileInfo(file);
        var stamp = (info.LastWriteTimeUtc, info.Length, relativePath);
        if (_parsed.TryGetValue(file, out var held) && held.Stamp == stamp) return (held.Markdown, held.Page);

        var markdown = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
        var page = ScenarioPageParser.Parse(markdown, relativePath);
        _parsed[file] = (stamp, markdown, page);
        return (markdown, page);
    }

    private readonly ConcurrentDictionary<string, ((DateTime, long, string) Stamp, string Markdown, ScenarioPage Page)> _parsed =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The scenario page a bare <c>&lt;stem&gt;.md</c> names, searched for
    /// under the domain folder; null when no page or more than one has that name —
    /// an ambiguous stem names no page, as the checker reports it.</summary>
    private async Task<string?> FindByStemAsync(
        Area area,
        string alias,
        string fileName,
        ScenarioReads reads,
        CancellationToken cancellationToken)
    {
        if (reads.Stems.TryGetValue(fileName, out var known)) return known;

        var answer = await SearchStemAsync(area, alias, fileName, reads, cancellationToken).ConfigureAwait(false);
        reads.Stems[fileName] = answer;
        return answer;
    }

    private async Task<string?> SearchStemAsync(
        Area area,
        string alias,
        string fileName,
        ScenarioReads reads,
        CancellationToken cancellationToken)
    {
        var location = await _folders.PrepareListingAsync(area.Setting.Key, alias, cancellationToken).ConfigureAwait(false);
        if (!location.Available || string.IsNullOrWhiteSpace(location.FullPath)) return null;

        var found = _folders.FileTree(location)
            .EnumerateFiles(location.FullPath, fileName, recursive: true)
            .Select(file => Path.GetRelativePath(location.FullPath, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(relative => !relative.Split('/').Any(segment => segment is "_meta" or "_tools" || segment.StartsWith('.')))
            .Select(relative => $"{area.Prefix}/{relative}")
            .ToList();

        var scenarios = new List<string>();
        foreach (var candidate in found)
        {
            if (await ReadDomainPageAsync(area, alias, candidate, reads, cancellationToken).ConfigureAwait(false) is { Scenario: not null } page) scenarios.Add(page.Path);
        }

        return scenarios.Count == 1 ? scenarios[0] : null;
    }

    /// <summary>The parts of one scenario page with the state each has against the
    /// page's last run.</summary>
    private async Task<IReadOnlyList<ScenarioPartEvidence>> EvidenceAsync(
        ScenarioRead read,
        IReadOnlyList<ScenarioPart> parts,
        ScenarioReads reads,
        CancellationToken cancellationToken)
    {
        if (read.Scenario is not { } page || parts.Count == 0) return [];

        var (signature, run) = await RunOfAsync(read, page, reads, cancellationToken).ConfigureAwait(false);
        var title = string.IsNullOrWhiteSpace(page.Title) ? page.Stem : page.Title;

        return
        [
            .. parts.Select(part => new ScenarioPartEvidence(
                $"{page.Path}#{part.Anchor}",
                page.Path,
                title,
                page.Stem,
                part.Anchor,
                part.Title,
                ToPartState(ScenarioReferences.PartState(signature, run, part.Anchor)),
                run?.RanAt))
        ];
    }

    /// <summary>The page's signature now and its last run, read once per page. On
    /// a branch the run and the data sets the signature hashes are fetched first,
    /// as the Devbook pane's catalog fetches them.</summary>
    private async Task<(string Signature, ScenarioRun? Run)> RunOfAsync(
        ScenarioRead read,
        ScenarioPage page,
        ScenarioReads reads,
        CancellationToken cancellationToken)
    {
        if (reads.Runs.TryGetValue(page.Path, out var known)) return known;

        var root = read.Location.RootPath!;
        var tree = _folders.FileTree(read.Location);
        var scenarioFolder = tree.DirectoryExists(Path.Combine(root, ScenarioPageParser.ScenarioFolder))
            || !tree.DirectoryExists(Path.Combine(root, ScenarioPageParser.LegacyScenarioFolder))
                ? ScenarioPageParser.ScenarioFolder
                : ScenarioPageParser.LegacyScenarioFolder;
        var runFolder = Path.Combine(root, scenarioFolder, page.Stem);

        if (read.Location.Source is DevbookSourceKind.Branch)
        {
            var wanted = new[] { Path.Combine(runFolder, "run.json") }
                .Concat(page.Setup.Data
                    .Select(ScenarioPageParser.Collapse)
                    .Where(ScenarioSignature.IsDataSetName)
                    .Select(name => Path.Combine(root, scenarioFolder, "data", name))
                    .Where(tree.DirectoryExists)
                    .SelectMany(folder => tree.EnumerateFiles(folder, "*", recursive: true)))
                .Where(tree.FileExists)
                .Select(file => "**/" + Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (wanted.Count > 0) await _folders.PrepareContentAsync(read.Area.Setting.Key, read.Alias, wanted, cancellationToken).ConfigureAwait(false);
        }

        var run = ScenarioFiles.ReadRun(runFolder);

        // Two pages sharing a stem share a run folder; a run that names the page it
        // executed belongs to that page alone.
        if (run?.Page is { } ranPage && !SamePage(ranPage, page.Path))
        {
            run = null;
        }

        var answer = (ScenarioSignature.OfPage(page, root, scenarioFolder), run);
        reads.Runs[page.Path] = answer;
        return answer;
    }

    /// <summary>Whether a run's <c>page</c> names this page, in any spelling a
    /// layout gives it.</summary>
    private static bool SamePage(string ranPage, string pagePath) =>
        Parse(ranPage) is { } ran
        && Spellings(ran.Path).Intersect(Spellings(pagePath), StringComparer.OrdinalIgnoreCase).Any();

    private static ScenarioPartState ToPartState(ScenarioState state) => state switch
    {
        ScenarioState.Passed => ScenarioPartState.Passed,
        ScenarioState.Failed => ScenarioPartState.Failed,
        ScenarioState.Stale => ScenarioPartState.Stale,
        _ => ScenarioPartState.NeverRun
    };

    /// <summary>One page read for its scenario parts.</summary>
    /// <param name="Path">The page's repository-relative path where its file is.</param>
    private sealed record ScenarioRead(Area Area, string Alias, DevbookFolderLocation Location, string Path, string Markdown, ScenarioPage? Scenario);

    /// <summary>What one resolve has read, so a page several references name is
    /// read once.</summary>
    private sealed class ScenarioReads
    {
        public Dictionary<string, ScenarioRead?> Pages { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, (string Signature, ScenarioRun? Run)> Runs { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string?> Stems { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The rows the database holds for a page, under either layout's
    /// spelling of its path, or null when it holds none — which is a reason to
    /// read the file rather than an answer.</summary>
    private static Lookup? FromDatabase(DevbookDatabase? database, string path)
    {
        if (database is null) return null;

        foreach (var spelling in Spellings(path))
        {
            var rows = database.Chapters(spelling);
            if (rows.Count > 0) return new Lookup.Found([.. rows.Select(Heading.FromRow)]);
        }

        return null;
    }

    private async Task<Lookup> FromFileAsync(Area area, string alias, string path, CancellationToken cancellationToken)
    {
        var relative = path[(area.Prefix.Length + 1)..];

        // A page is a Markdown file; a folder or anything else named here is no
        // page, whatever is on disk.
        if (!relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return Lookup.Missing.Instance;

        // Named down to the one file, as the MCP chapter read does: for a branch
        // snapshot this is the moment that page is fetched, and the port answers
        // a failed fetch as an unavailable folder rather than by throwing.
        var location = await _folders
            .PrepareContentAsync(area.Setting.Key, alias, [relative], cancellationToken)
            .ConfigureAwait(false);

        if (!location.Available || string.IsNullOrWhiteSpace(location.FullPath)) return Lookup.Unreadable.Instance;

        var file = ResolveWithin(location.FullPath, relative);
        if (file is null || !File.Exists(file)) return Lookup.Missing.Instance;

        try
        {
            var markdown = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            return new Lookup.Found([.. DevbookMarkdown.Parse(markdown).Chapters.Select(Heading.FromParsed)]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Lookup.Unreadable.Instance;
        }
    }

    /// <summary>
    /// The heading an anchor names: its slug exactly, then without regard to
    /// case, then the anchor slugged the way a heading is — so an anchor typed as
    /// the heading reads still finds it. The first heading wins among repeats, as
    /// a browser's anchor does.
    /// </summary>
    private static Heading? FindHeading(IReadOnlyList<Heading> headings, string anchor)
    {
        var slugged = DevbookMarkdown.Slugify(anchor);

        return headings.FirstOrDefault(heading => string.Equals(heading.Slug, anchor, StringComparison.Ordinal))
            ?? headings.FirstOrDefault(heading => string.Equals(heading.Slug, anchor, StringComparison.OrdinalIgnoreCase))
            ?? headings.FirstOrDefault(heading => string.Equals(heading.Slug, slugged, StringComparison.Ordinal));
    }

    /// <summary>The page's title: its first level-1 heading, else its first
    /// heading, else none.</summary>
    private static string? PageTitle(IReadOnlyList<Heading> headings) =>
        (headings.FirstOrDefault(heading => heading.Level == 1) ?? headings.FirstOrDefault())?.Text;

    /// <summary>
    /// A written reference taken apart the way a task stores one: surrounding
    /// <c>[...]</c> off, split on the first <c>#</c>, backslashes to <c>/</c>, a
    /// leading <c>./</c> or <c>/</c> dropped. Null for a value naming no page —
    /// one whose path has neither a folder nor a <c>.md</c> ending.
    /// </summary>
    internal static (string Path, string? Anchor)? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var value = text.Trim();
        if (value.Length >= 2 && value[0] == '[' && value[^1] == ']') value = value[1..^1].Trim();

        var hash = value.IndexOf('#', StringComparison.Ordinal);
        var path = (hash < 0 ? value : value[..hash]).Trim().Replace('\\', '/');
        var anchor = hash < 0 ? null : value[(hash + 1)..].Trim();

        while (path.StartsWith("./", StringComparison.Ordinal) || path.StartsWith('/'))
        {
            path = path.StartsWith('/') ? path[1..] : path[2..];
        }

        if (path.Length == 0) return null;
        if (!path.Contains('/', StringComparison.Ordinal) && !path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return null;

        return (path, string.IsNullOrEmpty(anchor) ? null : anchor);
    }

    /// <summary>
    /// The spellings one page may be stored under: as written, then in the root
    /// layout's (<c>.domain/…</c>), then in the devbook layout's
    /// (<c>.devbook/domain/…</c>). The generator writes whichever layout it found,
    /// and a reference written before a repository moved still names the page.
    /// </summary>
    private static IEnumerable<string> Spellings(string path)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { path };
        yield return path;

        var conventional = DevbookLayout.ConventionalPath(path);
        if (seen.Add(conventional)) yield return conventional;

        if (conventional.StartsWith('.') && conventional.IndexOf('/') is > 1 and var slash)
        {
            var devbook = $"{DevbookFolderSetting.DevbookRoot}/{conventional[1..slash]}{conventional[slash..]}";
            if (seen.Add(devbook)) yield return devbook;
        }
    }

    /// <summary>
    /// The adopted devbook folders of a repository and every repository-relative
    /// prefix each answers to — its configured or default folder and the legacy
    /// root folder — longest first, so the most specific folder claims a path.
    /// Switched-off folders are not adopted, and Instructions, whose root is the
    /// repository itself, is no devbook folder.
    /// </summary>
    private static IReadOnlyList<Area> Areas(IReadOnlyList<DevbookFolderSetting> settings) =>
        [
            .. settings
                .Where(setting => setting.Enabled && !string.IsNullOrWhiteSpace(setting.DefaultRelativePath))
                .SelectMany(setting => new[] { setting.EffectivePath, setting.DefaultRelativePath, setting.LegacyRelativePath }
                    .Select(Prefix)
                    .Where(prefix => prefix.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(prefix => new Area(setting, prefix, DevbookChapterKey.NormalizeAreaKey(setting.Key))))
                .OrderByDescending(area => area.Prefix.Length)
        ];

    private static string Prefix(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || Path.IsPathRooted(folder)) return string.Empty;

        var forward = folder.Trim().Replace('\\', '/');
        while (forward.StartsWith("./", StringComparison.Ordinal)) forward = forward[2..];
        return forward.Trim('/');
    }

    private static Area? Match(IReadOnlyList<Area> areas, string path) =>
        areas.FirstOrDefault(area => DevbookChapterKey.StartsWithSegment(path, area.Prefix));

    /// <summary>The full path of a relative path under a root, or null when it
    /// climbs out of it — the reference arrives from a task, or over the wire.</summary>
    private static string? ResolveWithin(string rootPath, string relativePath)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
            var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

            return fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>One adopted folder under one of the prefixes it answers to.</summary>
    private sealed record Area(DevbookFolderSetting Setting, string Prefix, string Folder);

    /// <summary>One heading, whichever side of the ladder it was read from.</summary>
    private sealed record Heading(int Level, string Text, string Slug, string? Status)
    {
        public static Heading FromRow(DevbookChapterRow row) => new(row.Level, row.Title ?? row.Slug, row.Slug, row.Status);

        public static Heading FromHeadingRow(DevbookChapterHeadingRow row) => new(row.Level, row.Title ?? row.Slug, row.Slug, row.Status);

        public static Heading FromParsed(DevbookParsedChapter chapter) =>
            new(chapter.Level, chapter.Text, chapter.Slug, chapter.Meta?.GetValueOrDefault("status") as string);
    }

    /// <summary>What looking for a page found.</summary>
    private abstract record Lookup
    {
        public sealed record Found(IReadOnlyList<Heading> Headings) : Lookup;

        public sealed record Missing : Lookup
        {
            public static readonly Missing Instance = new();
        }

        public sealed record Unreadable : Lookup
        {
            public static readonly Unreadable Instance = new();
        }
    }
}
