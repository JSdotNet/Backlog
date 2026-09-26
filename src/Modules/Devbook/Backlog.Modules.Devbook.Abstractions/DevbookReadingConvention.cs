using System.Globalization;
using System.Text.RegularExpressions;

namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// The reading order of a devbook directory, as the folder convention derives it
/// from names alone.
///
/// <para>A port of the name-only half of the devbook generator's
/// <c>outline.mjs</c> (<c>DIRECTORY_CONVENTION</c>, <c>conventionFor</c>,
/// <c>orderedSequence</c>, <c>splitBase</c>) and of <c>fileNumberFromPath</c> in
/// its <c>metadata.mjs</c>. Nothing about the order is authored per repository:
/// local ADR 0016 retired <c>_reading-order.json</c>, and one left in any
/// repository is ignored. What a document can still say about its own place —
/// <c>index: root</c>, <c>index: exclude</c>, a <c>number</c> field — lives in its
/// <c>meta</c> block, and only the database builder reads that; it layers those
/// rules on top of <see cref="Order(DevbookDirectoryConvention?, IEnumerable{string}, string?, Func{string, long?}?)"/>.
/// A reader that will not open a Markdown file to learn an order — the panels, the
/// rail — calls <see cref="Order(string?, int, IEnumerable{string})"/>, which takes
/// the convention root and filename numbers for the whole answer.</para>
///
/// <para>Ordering, per directory: the root document first; then, when anything
/// else carries a number, the numbered entries by number and the rest by name;
/// otherwise the directory's convention — the root's split files, each
/// <c>first</c> slot with its split files and its subpage, whatever the
/// convention does not name, then the <c>last</c> slots. A directory the
/// convention has no entry for — all of <c>arc42</c>, and anything below a
/// bounded context — sorts by name after its root. "By name" is JavaScript's
/// default sort, which is ordinal by UTF-16 code unit.</para>
///
/// <para>The table mirrors the structure block in each folder's own rules file,
/// through the generator; change it only with the generator, since
/// <c>DevbookBuilderParityTests</c> holds the database builder to it.</para>
/// </summary>
public static partial class DevbookReadingConvention
{
    private static readonly Dictionary<string, DevbookDirectoryConvention> Conventions = new(StringComparer.Ordinal)
    {
        ["domain"] = new("context-map.md", [], []),
        ["domain/*"] = new(
            "context.md",
            ["domain.md", "actors.md", "skills.md", "features.md", "model.md", "flow.md", "dependencies.md"],
            [])
        {
            Split = ["domain.md", "features.md", "skills.md", "model.md", "flow.md"],
            SubpageSuffix = "invariants",
            SubpageOf = ["domain.md"]
        },
        ["tech"] = new("technology-graph.md", ["shared.md"], ["tooling.md"]),
        ["ai"] = new("adoption-map.md", [], []),
        ["design"] = new(
            "README.md",
            ["design-principles.md", "color-scheme.md", "typography-and-layout.md", "interaction-guidelines.md", "accessibility.md", "component-libraries.md"],
            [])
    };

    /// <summary>
    /// The convention for a directory <paramref name="depth"/> levels below the
    /// root of a devbook folder of kind <paramref name="folderKind"/> —
    /// <c>domain</c> at 0 is the context map's directory, at 1 a bounded
    /// context — or <see langword="null"/> when the convention says nothing
    /// about it and it sorts by name.
    /// </summary>
    /// <param name="folderKind">The folder's kind without a dot: <c>domain</c>,
    /// <c>tech</c>. A leading dot, as the root layout spells it, is ignored.</param>
    public static DevbookDirectoryConvention? For(string? folderKind, int depth)
    {
        if (string.IsNullOrWhiteSpace(folderKind) || depth < 0) return null;

        var kind = folderKind.TrimStart('.');
        var key = depth == 0 ? kind : kind + string.Concat(Enumerable.Repeat("/*", depth));
        return Conventions.GetValueOrDefault(key);
    }

    /// <summary>
    /// The directory's root document by convention, when a file of that name is
    /// among <paramref name="names"/>; otherwise <see langword="null"/>.
    /// </summary>
    public static string? ConventionRoot(DevbookDirectoryConvention? convention, IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return convention is not null && names.Contains(convention.Root, StringComparer.Ordinal) ? convention.Root : null;
    }

    /// <summary>
    /// The number a file or directory name carries — <c>01-introduction.md</c>,
    /// <c>0007-use-postgres.md</c>, <c>ADR-0007-use-postgres.md</c> — or
    /// <see langword="null"/> for a name without one. Takes the last path
    /// segment, as the generator's <c>fileNumberFromPath</c> does.
    /// </summary>
    public static long? FileNumber(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var segment = name.Replace('\\', '/');
        segment = segment[(segment.LastIndexOf('/') + 1)..];

        var match = FileNamePattern().Match(segment);
        if (!match.Success) return null;

        // JavaScript's Number("0007") is 7; a run too long for a long is not a
        // number any chapter carries.
        return long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    /// <summary>
    /// A directory's entries in reading order from their names alone — the root
    /// the convention names, filename numbers, and the convention's slots.
    /// Nothing is opened.
    /// </summary>
    /// <param name="folderKind">The folder's kind, <c>domain</c>; see <see cref="For"/>.</param>
    /// <param name="depth">Levels below the folder's root; see <see cref="For"/>.</param>
    /// <param name="names">File and directory names, without paths.</param>
    public static IReadOnlyList<string> Order(string? folderKind, int depth, IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var list = names.ToList();
        var convention = For(folderKind, depth);
        return Order(convention, list, ConventionRoot(convention, list), number: null);
    }

    /// <summary>
    /// A directory's entries in reading order, given its root document and each
    /// entry's number — the generator's <c>orderedSequence</c>.
    /// </summary>
    /// <param name="convention">The directory's convention, from <see cref="For"/>.</param>
    /// <param name="names">File and directory names, without paths.</param>
    /// <param name="rootName">The entry that reads first, or <see langword="null"/>.</param>
    /// <param name="number">An entry's number, or <see langword="null"/> for
    /// <see cref="FileNumber"/> on every entry.</param>
    public static IReadOnlyList<string> Order(
        DevbookDirectoryConvention? convention,
        IEnumerable<string> names,
        string? rootName,
        Func<string, long?>? number)
    {
        ArgumentNullException.ThrowIfNull(names);
        number ??= FileNumber;

        var rest = names.Where(name => name != rootName).ToList();
        rest.Sort(StringComparer.Ordinal);
        var lead = rootName is null ? new List<string>() : [rootName];

        var numbers = rest.ToDictionary(name => name, name => number(name), StringComparer.Ordinal);
        var numbered = rest.Where(name => numbers[name] is not null).ToList();
        if (numbered.Count > 0)
        {
            // By number, then by name so a duplicated number is still
            // deterministic — the generator's localeCompare tie-break.
            var inOrder = numbered
                .OrderBy(name => numbers[name]!.Value)
                .ThenBy(name => name, StringComparer.InvariantCulture);

            return [.. lead, .. inOrder, .. rest.Where(name => numbers[name] is null)];
        }

        if (convention is null) return [.. lead, .. rest];

        bool IsSubpage(string name) =>
            convention.SubpageSuffix is { } suffix && name.EndsWith($".{suffix}.md", StringComparison.Ordinal);

        IEnumerable<string> SubpageOf(string name)
        {
            if (convention.SubpageSuffix is not { } suffix) return [];
            if (!convention.SubpageOf.Contains(SplitBase(name) ?? name, StringComparer.Ordinal)) return [];

            var sub = MarkdownExtension().Replace(name, $".{suffix}.md", 1);
            return rest.Contains(sub, StringComparer.Ordinal) ? [sub] : [];
        }

        IEnumerable<string> SplitsOf(string @base) =>
            convention.Split.Contains(@base, StringComparer.Ordinal)
                ? rest.Where(name => !IsSubpage(name) && SplitBase(name) == @base)
                : [];

        IEnumerable<string> Slot(string name) =>
            (rest.Contains(name, StringComparer.Ordinal) ? [name] : Enumerable.Empty<string>())
                .Concat(SplitsOf(name))
                .SelectMany(page => SubpageOf(page).Prepend(page));

        var rootSplits = SplitsOf(convention.Root).ToList();
        var pinnedFirst = convention.First.SelectMany(Slot).ToList();
        var pinnedLast = convention.Last.SelectMany(Slot).ToList();
        var placed = new HashSet<string>(rootSplits.Concat(pinnedFirst).Concat(pinnedLast), StringComparer.Ordinal);

        return [.. lead, .. rootSplits, .. pinnedFirst, .. rest.Where(name => !placed.Contains(name)), .. pinnedLast];
    }

    /// <summary>The file a split file is named after — <c>domain.order.md</c> is
    /// <c>domain.md</c>'s — or <see langword="null"/> for a name without exactly
    /// one inner segment.</summary>
    public static string? SplitBase(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var match = SplitName().Match(name);
        return match.Success ? match.Groups[1].Value + ".md" : null;
    }

    // The generator's FILENAME_NUMBER_PATTERN. `\d` and `[A-Za-z]` are ASCII there,
    // so they are spelled out here rather than left to .NET's Unicode classes.
    [GeneratedRegex("^(?:[A-Za-z]+[-_ ])?([0-9]+)(?:[-_. ]|$)", RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();

    // `/^([^.]+)\.[^.]+\.md$/`. `$` is spelled `\z`: .NET's `$` also matches before
    // a trailing newline, JavaScript's does not.
    [GeneratedRegex("^([^.]+)\\.[^.]+\\.md\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SplitName();

    [GeneratedRegex("\\.md\\z", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownExtension();
}

/// <summary>
/// One directory shape of the reading convention.
/// </summary>
/// <param name="Root">The entry point, read first.</param>
/// <param name="First">Prescribed files pinned after the root, in this order.</param>
/// <param name="Last">Prescribed files pinned after everything else.</param>
public sealed record DevbookDirectoryConvention(
    string Root,
    IReadOnlyList<string> First,
    IReadOnlyList<string> Last)
{
    /// <summary>The files a <c>&lt;file&gt;.&lt;name&gt;.md</c> may be split out
    /// of; such a file reads directly after its base, or in the base's place when
    /// the base is gone.</summary>
    public IReadOnlyList<string> Split { get; init; } = [];

    /// <summary>The suffix of a page that belongs to another —
    /// <c>invariants</c>, so <c>domain.invariants.md</c> reads directly after
    /// <c>domain.md</c> — or <see langword="null"/>.</summary>
    public string? SubpageSuffix { get; init; }

    /// <summary>The files, and their split files, that take a subpage.</summary>
    public IReadOnlyList<string> SubpageOf { get; init; } = [];
}
