using System.Text.RegularExpressions;

namespace Backlog.Infrastructure.Devbook.Scenarios;

/// <summary>
/// The rules that take a Devbook reference to the scenario parts it stands for, and
/// a part to its state: what an entry's acceptance checklist is built from.
/// <para>
/// Pure, so the rules are tested apart from the files: the reader in
/// <see cref="DevbookReferenceResolver"/> fetches and reads, and asks these.
/// </para>
/// </summary>
public static partial class ScenarioReferences
{
    /// <summary>
    /// One part's state against its journey's last run, in the dot's precedence. No
    /// run is never run; a run on another signature — or a version 1 run, which
    /// proves none — is stale for every part; on a current run the part is what the
    /// run says, and a part the run did not reach is never run.
    /// </summary>
    public static ScenarioState PartState(string journeySignature, ScenarioRun? run, string anchor)
    {
        if (run is null) return ScenarioState.NeverRun;
        if (run.Signature is null || !string.Equals(run.Signature, journeySignature, StringComparison.OrdinalIgnoreCase)) return ScenarioState.Stale;

        return run.Part(anchor)?.Outcome switch
        {
            ScenarioOutcome.Passed => ScenarioState.Passed,
            ScenarioOutcome.Failed => ScenarioState.Failed,
            _ => ScenarioState.NeverRun
        };
    }

    /// <summary>The part an anchor names: its slug, then without regard to case,
    /// then the anchor slugged the way a heading is — so a part typed as its
    /// heading reads still finds it.</summary>
    public static ScenarioPart? FindPart(ScenarioJourney journey, string anchor)
    {
        ArgumentNullException.ThrowIfNull(journey);
        return Find(journey.Parts, part => part.Anchor, anchor);
    }

    /// <summary>The journey of a scenarios file an anchor names, by the same rule as
    /// <see cref="FindPart"/>. Only a journey typed <c>scenario</c> is found.</summary>
    public static ScenarioJourney? FindJourney(ScenariosFile file, string anchor)
    {
        ArgumentNullException.ThrowIfNull(file);
        return Find(file.Scenarios, journey => journey.Slug, anchor);
    }

    /// <summary>The journey of a scenarios file holding the part an anchor names, and
    /// that part — anchors are unique in a scenarios file, so there is one at most.</summary>
    public static (ScenarioJourney Journey, ScenarioPart Part)? FindPart(ScenariosFile file, string anchor)
    {
        ArgumentNullException.ThrowIfNull(file);
        foreach (var journey in file.Scenarios)
        {
            if (FindPart(journey, anchor) is { } part) return (journey, part);
        }

        return null;
    }

    private static T? Find<T>(IReadOnlyList<T> items, Func<T, string> anchorOf, string anchor)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(anchor)) return null;

        var slugged = ScenarioPageParser.Slugify(anchor);
        return items.FirstOrDefault(item => string.Equals(anchorOf(item), anchor, StringComparison.Ordinal))
            ?? items.FirstOrDefault(item => string.Equals(anchorOf(item), anchor, StringComparison.OrdinalIgnoreCase))
            ?? items.FirstOrDefault(item => string.Equals(anchorOf(item), slugged, StringComparison.Ordinal));
    }

    /// <summary>
    /// Every <c>Proved by:</c> target in a page's chapter — the heading
    /// <paramref name="anchor"/> names and everything under it — or in the whole
    /// page when no anchor is given, in page order and each once. Lines inside a
    /// code fence are an example, not a pointer. An anchor no heading has finds
    /// nothing.
    /// </summary>
    public static IReadOnlyList<string> ProvedByTargets(string markdown, string? anchor)
    {
        var lines = LineBreak().Split(markdown ?? string.Empty);
        var targets = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var inside = anchor is null;
        var level = 0;
        string? fence = null;

        foreach (var line in lines)
        {
            var marker = FenceMarker().Match(line);
            if (fence is not null)
            {
                if (marker.Success && marker.Groups[1].Value[0] == fence[0] && marker.Groups[1].Value.Length >= fence.Length) fence = null;
                continue;
            }

            if (marker.Success)
            {
                fence = marker.Groups[1].Value;
                continue;
            }

            if (anchor is not null && Heading().Match(line) is { Success: true } heading)
            {
                var headingLevel = heading.Groups[1].Value.Length;
                if (inside && headingLevel <= level) break;
                if (!inside && SameAnchor(ScenarioPageParser.Slugify(heading.Groups[2].Value), anchor))
                {
                    inside = true;
                    level = headingLevel;
                }

                continue;
            }

            if (!inside) continue;

            if (ProvedByLine().Match(line) is { Success: true } proved && proved.Groups[1].Value is { Length: > 0 } target && seen.Add(target))
            {
                targets.Add(target);
            }
        }

        return targets;
    }

    /// <summary>
    /// A <c>Proved by:</c> target taken apart: the scenarios file it names and the
    /// part. As devbook's checker resolves it (<c>scenario.mjs</c> <c>resolveFile</c>):
    /// a repository path as written, or a path from the referring page's folder — a
    /// bare <c>scenarios.md</c> is the one in the requirement's own context,
    /// <c>../shipping/scenarios.md</c> another's. Null for a target that names no
    /// <c>.md</c> file or no part.
    /// </summary>
    /// <param name="fromPagePath">The referring page, repository-relative.</param>
    public static (string Page, string Anchor)? ResolveTarget(string fromPagePath, string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;

        var value = target.Trim().Trim('`');
        var hash = value.IndexOf('#', StringComparison.Ordinal);
        if (hash <= 0 || hash == value.Length - 1) return null;

        var page = value[..hash].Trim().Replace('\\', '/');
        var anchor = value[(hash + 1)..].Trim();
        if (!page.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || anchor.Length == 0) return null;

        // A repository path — `.devbook/domain/…`, or a root layout's `.domain/…` —
        // is the repository's; anything else is the referring page's folder's.
        if (page[0] == '.'
            && page.Contains('/', StringComparison.Ordinal)
            && !page.StartsWith("./", StringComparison.Ordinal)
            && !page.StartsWith("../", StringComparison.Ordinal))
        {
            return Normalize(page) is { } repositoryPath ? (repositoryPath, anchor) : null;
        }

        var folder = fromPagePath.Replace('\\', '/');
        var slash = folder.LastIndexOf('/');
        folder = slash < 0 ? string.Empty : folder[..slash];

        var joined = Normalize(folder.Length == 0 ? page : $"{folder}/{page}");
        return joined is null ? null : (joined, anchor);
    }

    /// <summary>A forward-slash path with its <c>.</c> and <c>..</c> segments
    /// folded, or null when it climbs out of the repository.</summary>
    private static string? Normalize(string path)
    {
        var segments = new List<string>();
        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment == ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0) return null;
                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return segments.Count == 0 ? null : string.Join('/', segments);
    }

    private static bool SameAnchor(string slug, string anchor) =>
        string.Equals(slug, anchor, StringComparison.OrdinalIgnoreCase)
        || string.Equals(slug, ScenarioPageParser.Slugify(anchor), StringComparison.Ordinal);

    [GeneratedRegex(@"\r?\n")]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"^\s*(`{3,}|~{3,})")]
    private static partial Regex FenceMarker();

    [GeneratedRegex(@"^(#{1,6})\s+(.*?)\s*#*\s*$")]
    private static partial Regex Heading();

    // The checker's PROVED_BY_LINE (metadata.mjs).
    [GeneratedRegex(@"^\s*Proved by:\s*(.*?)\s*$")]
    private static partial Regex ProvedByLine();
}
