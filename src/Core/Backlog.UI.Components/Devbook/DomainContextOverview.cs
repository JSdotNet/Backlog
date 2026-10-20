using System.Text.RegularExpressions;

using Backlog.SharedKernel.Devbook;
using Backlog.SharedKernel.Markdown;
using Backlog.SharedKernel.Metadata;

namespace Backlog.UI.Components.Devbook;

/// <summary>How many chapters of one kind a context's files hold.</summary>
/// <param name="Type">The chapters' <c>type</c>, as the <c>meta</c> block writes it.</param>
/// <param name="Label">The kind in plain words: <c>Requirements</c>, <c>Always-true rules</c>.</param>
public sealed record DomainChapterCount(string Type, string Label, int Count);

/// <summary>How many chapters carry one <c>status</c>.</summary>
public sealed record DomainStatusCount(string Status, int Count);

/// <summary>A <c>feature-flag</c> or <c>setting</c> chapter of <c>context.md</c>.</summary>
/// <param name="Title">The heading as written.</param>
/// <param name="IsSetting">A setting rather than a feature flag.</param>
/// <param name="Key">The block's <c>key</c>, or null when it states none.</param>
/// <param name="Default">The block's <c>default</c>, or null when it states none.</param>
/// <param name="Status">The block's <c>status</c>; <c>active</c> when it states
/// none, the domain folder's resting value.</param>
/// <param name="Summary">The chapter's first sentence as plain text.</param>
public sealed record DomainContextSwitch(string Title, bool IsSetting, string? Key, string? Default, string Status, string Summary);

/// <summary>
/// What the Domain view's Overview tab says about one bounded context (local ADR
/// 0037): the boundary <c>context.md</c> draws above its first chapter, how many
/// chapters of each kind the context's files hold, how many of the provable ones
/// name a test, how settled the chapters are, and the context's flags and
/// settings. Everything is read from the files as written; no chapter carries a
/// field for the view.
/// </summary>
/// <param name="Boundary">The prose of <c>context.md</c> between its title and
/// its first <c>##</c> chapter, as markdown, without its <c>meta</c> block or any
/// <c>annotation</c> note.</param>
/// <param name="Counts">The chapters per kind, in the order a reader meets the
/// kinds: domain first, then features and requirements.</param>
/// <param name="Provable">The requirement and always-true rule chapters.</param>
/// <param name="Proved">Those of them whose block names a <c>tests</c> entry.</param>
/// <param name="Statuses">The chapters per <c>status</c>, least settled first.</param>
/// <param name="Switches">The <c>feature-flag</c> and <c>setting</c> chapters of
/// <c>context.md</c>, in file order.</param>
public sealed partial record DomainContextOverview(
    string Boundary,
    IReadOnlyList<DomainChapterCount> Counts,
    int Provable,
    int Proved,
    IReadOnlyList<DomainStatusCount> Statuses,
    IReadOnlyList<DomainContextSwitch> Switches)
{
    public static DomainContextOverview Empty { get; } = new(string.Empty, [], 0, 0, [], []);

    /// <summary>The kinds a chapter is counted as, in reading order, each in
    /// plain words. A file's own <c>type</c> — <c>requirements</c>,
    /// <c>model</c> — is not a chapter kind and is never counted.</summary>
    private static readonly (string Type, string Label)[] Kinds =
    [
        ("aggregate", "Aggregates"),
        ("entity", "Entities"),
        ("value-object", "Value objects"),
        ("enum", "Enums"),
        ("domain-service", "Domain services"),
        ("domain-event", "Events"),
        ("term", "Terms"),
        ("feature", "Features"),
        ("sub-feature", "Sub-features"),
        ("requirement", "Requirements"),
        ("invariant", "Always-true rules")
    ];

    private static readonly HashSet<string> Provables = new(StringComparer.OrdinalIgnoreCase) { "requirement", "invariant" };

    private static readonly HashSet<string> SwitchTypes = new(StringComparer.OrdinalIgnoreCase) { "feature-flag", "setting" };

    /// <summary>The status words of the domain folder, least settled first.</summary>
    private static readonly string[] StatusOrder = ["draft", "proposed", "approved", "active", "accepted", "deprecated"];

    /// <summary>Reads the overview of one context.</summary>
    /// <param name="contextMarkdown">The context's <c>context.md</c>.</param>
    /// <param name="files">The context's other files, each as written.</param>
    public static DomainContextOverview Read(string? contextMarkdown, IEnumerable<string> files)
    {
        var context = contextMarkdown ?? string.Empty;
        var contextChapters = MetaChapters(context).ToList();
        var chapters = contextChapters.Concat(files.SelectMany(MetaChapters)).ToList();

        var counted = chapters.Where(chapter => chapter.Type is { } type
            && (SwitchTypes.Contains(type) || Kinds.Any(kind => string.Equals(kind.Type, type, StringComparison.OrdinalIgnoreCase)))).ToList();

        IReadOnlyList<DomainChapterCount> counts =
        [
            .. Kinds
                .Select(kind => new DomainChapterCount(kind.Type, kind.Label,
                    counted.Count(chapter => string.Equals(chapter.Type, kind.Type, StringComparison.OrdinalIgnoreCase))))
                .Where(count => count.Count > 0)
        ];

        var provable = counted.Where(chapter => Provables.Contains(chapter.Type!)).ToList();

        IReadOnlyList<DomainStatusCount> statuses =
        [
            .. counted
                .GroupBy(chapter => chapter.Status, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => Array.IndexOf(StatusOrder, group.Key) is var rank && rank < 0 ? StatusOrder.Length : rank)
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new DomainStatusCount(group.Key, group.Count()))
        ];

        IReadOnlyList<DomainContextSwitch> switches =
        [
            .. contextChapters
                .Where(chapter => chapter.Type is { } type && SwitchTypes.Contains(type))
                .Select(chapter => new DomainContextSwitch(
                    chapter.Title,
                    string.Equals(chapter.Type, "setting", StringComparison.OrdinalIgnoreCase),
                    chapter.Meta.Extra.TryGetValue("key", out var key) ? key.FirstOrDefault() : null,
                    chapter.Meta.Extra.TryGetValue("default", out var value) ? value.FirstOrDefault() : null,
                    chapter.Status,
                    DomainUnits.FirstSentence(chapter.Prose)))
        ];

        return new DomainContextOverview(BoundaryOf(context), counts, provable.Count, provable.Count(chapter => chapter.Meta.Tests.Count > 0), statuses, switches);
    }

    /// <summary>A chapter that opens with a <c>meta</c> block: its heading, the
    /// block as the shared reader reads it, and its prose up to the next heading.</summary>
    private sealed record MetaChapter(string Title, MetadataRecord Meta, string Prose)
    {
        public string? Type => Meta.Type;

        /// <summary>The block's status, or the domain folder's resting value when
        /// it leaves the field out (<see cref="DevbookSchema.RestsByOmission"/>).</summary>
        public string Status => string.IsNullOrWhiteSpace(Meta.Status)
            ? DevbookSchema.RestingStatus
            : Meta.Status.Trim().ToLowerInvariant();
    }

    private static IEnumerable<MetaChapter> MetaChapters(string markdown)
    {
        var lines = Lines(markdown);
        for (var i = 0; i < lines.Length; i++)
        {
            if (MarkdownFence.Open(lines[i]) is { } skipped)
            {
                i = CloseOf(lines, i, skipped);
                continue;
            }

            var heading = Heading().Match(lines[i]);
            if (!heading.Success || heading.Groups[1].Value.Length < 2) continue;

            var at = i + 1;
            while (at < lines.Length && string.IsNullOrWhiteSpace(lines[at])) at++;
            if (at >= lines.Length || MarkdownFence.Open(lines[at]) is not { } fence || !MetadataReader.IsMetaBlock(fence.Language)) continue;

            var close = CloseOf(lines, at, fence);
            var meta = MetadataReader.Parse(string.Join('\n', lines[(at + 1)..Math.Min(close, lines.Length)]));

            // The prose runs from after the block to the next heading outside a fence.
            var prose = new List<string>();
            var end = close + 1;
            while (end < lines.Length && !Heading().IsMatch(lines[end]))
            {
                if (MarkdownFence.Open(lines[end]) is { } inner)
                {
                    var innerClose = CloseOf(lines, end, inner);
                    prose.AddRange(lines[end..Math.Min(innerClose + 1, lines.Length)]);
                    end = innerClose + 1;
                    continue;
                }
                prose.Add(lines[end++]);
            }

            i = close;
            yield return new MetaChapter(heading.Groups[2].Value.Trim(), meta, string.Join('\n', prose));
        }
    }

    /// <summary>The prose under the title, up to the first chapter, without the
    /// title's <c>meta</c> block or any review note: neither is the boundary.</summary>
    private static string BoundaryOf(string markdown)
    {
        var lines = Lines(markdown);
        var boundary = new List<string>();
        var seenTitle = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (MarkdownFence.Open(lines[i]) is { } fence)
            {
                var close = CloseOf(lines, i, fence);
                if (!MetadataReader.IsMetaBlock(fence.Language) && !DevbookAnnotationFence.IsAnnotationBlock(fence.Language))
                    boundary.AddRange(lines[i..Math.Min(close + 1, lines.Length)]);
                i = close;
                continue;
            }

            if (Heading().Match(lines[i]) is { Success: true } heading)
            {
                if (heading.Groups[1].Value.Length == 1 && !seenTitle)
                {
                    seenTitle = true;
                    continue;
                }
                break;
            }
            boundary.Add(lines[i]);
        }
        return string.Join('\n', boundary).Trim();
    }

    /// <summary>The line that closes the fence opened at <paramref name="open"/>,
    /// or the last line when nothing closes it.</summary>
    private static int CloseOf(string[] lines, int open, MarkdownFence fence)
    {
        var at = open + 1;
        while (at < lines.Length && !fence.IsClosedBy(lines[at])) at++;
        return Math.Min(at, lines.Length - 1);
    }

    private static string[] Lines(string markdown) =>
        markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <summary>The ATX heading rule <see cref="DomainUnits"/> reads with.</summary>
    [GeneratedRegex(@"^(#{1,6})[ \t]+(.+?)(?:[ \t]+#+)?[ \t]*$")]
    private static partial Regex Heading();
}
