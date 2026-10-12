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
    /// One part's state against the page's last run, in the dot's precedence. No
    /// run is never run; a run on another signature — or a version 1 run, which
    /// proves none — is stale for every part; on a current run the part is what the
    /// run says, and a part the run did not reach is never run.
    /// </summary>
    public static ScenarioState PartState(string pageSignature, ScenarioRun? run, string anchor)
    {
        if (run is null) return ScenarioState.NeverRun;
        if (run.Signature is null || !string.Equals(run.Signature, pageSignature, StringComparison.OrdinalIgnoreCase)) return ScenarioState.Stale;

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
    public static ScenarioPart? FindPart(ScenarioPage page, string anchor)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (string.IsNullOrWhiteSpace(anchor)) return null;

        var slugged = ScenarioPageParser.Slugify(anchor);
        return page.Parts.FirstOrDefault(part => string.Equals(part.Anchor, anchor, StringComparison.Ordinal))
            ?? page.Parts.FirstOrDefault(part => string.Equals(part.Anchor, anchor, StringComparison.OrdinalIgnoreCase))
            ?? page.Parts.FirstOrDefault(part => string.Equals(part.Anchor, slugged, StringComparison.Ordinal));
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
    /// A <c>Proved by:</c> target taken apart: the page it names — a repository
    /// path as written, a path from the referring page's folder, or a bare
    /// <c>&lt;stem&gt;.md</c> left for the caller to look up by stem — and the part.
    /// Null for a target that names no <c>.md</c> page or no part.
    /// </summary>
    /// <param name="fromPagePath">The referring page, repository-relative.</param>
    public static (string Page, string Anchor, bool ByStem)? ResolveTarget(string fromPagePath, string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;

        var value = target.Trim().Trim('`');
        var hash = value.IndexOf('#', StringComparison.Ordinal);
        if (hash <= 0 || hash == value.Length - 1) return null;

        var page = value[..hash].Trim().Replace('\\', '/');
        var anchor = value[(hash + 1)..].Trim();
        if (!page.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || anchor.Length == 0) return null;

        if (!page.Contains('/', StringComparison.Ordinal))
        {
            // A bare file name is looked up by name, so it has to be one: a
            // wildcard would turn one line of committed text into a listing.
            return page.IndexOfAny(['*', '?']) < 0 && page.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                ? (page, anchor, true)
                : null;
        }

        // A repository path — `.devbook/domain/…`, or a root layout's `.domain/…` —
        // is the repository's; anything else is the referring page's folder's.
        if (page[0] == '.' && !page.StartsWith("./", StringComparison.Ordinal) && !page.StartsWith("../", StringComparison.Ordinal))
        {
            return Normalize(page) is { } repositoryPath ? (repositoryPath, anchor, false) : null;
        }

        var folder = fromPagePath.Replace('\\', '/');
        var slash = folder.LastIndexOf('/');
        folder = slash < 0 ? string.Empty : folder[..slash];

        var joined = Normalize(folder.Length == 0 ? page : $"{folder}/{page}");
        return joined is null ? null : (joined, anchor, false);
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
