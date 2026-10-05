using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel.Markdown;
using Backlog.SharedKernel.Metadata;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The demos a chapter's page has, as a session is told about them: by path and
/// address, never by content.
/// <para>
/// A demo is one self-contained HTML document, aimed at 500 KB and allowed past
/// it. Handing that to a caller that pays per token, on every read of a page that
/// happens to have one, would make the chapter the small part of its own answer.
/// So a read says which demos there are and where they point; opening one is a
/// file read the session can do in its own checkout.
/// </para>
/// <para>
/// Two pairings, per <see cref="DevbookReadingConvention.DemoPage"/>: the demos
/// beside the page whose name pairs them with it, and every place a <c>demo</c>
/// field in one of the page's <c>meta</c> blocks names. The second is read off the
/// parse <see cref="ChapterReading"/> already made, so a field's block index is the
/// same one the rest of the payload numbers by.
/// </para>
/// </summary>
internal static class ChapterDemos
{
    internal const string ByName = "name";
    internal const string ByField = "field";

    /// <summary>
    /// The demos beside a page that pair with it by name, spelled the way the
    /// chapter path is — <c>.devbook/domain/ordering/features.demo.html</c> for
    /// <c>.devbook/domain/ordering/features.md</c>.
    /// </summary>
    /// <param name="chapterPath">The chapter as the caller named it, normalized.</param>
    /// <param name="chapterFile">The chapter's full path on disk.</param>
    internal static IReadOnlyList<string> BesidePage(string chapterPath, string chapterFile)
    {
        var directory = Path.GetDirectoryName(chapterFile);
        if (directory is null) return [];

        string[] names;
        try
        {
            names = [.. Directory.EnumerateFiles(directory).Select(Path.GetFileName).OfType<string>()];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be listed has no demos to report; the chapter
            // itself was readable, and that is the answer the caller asked for.
            return [];
        }

        var slash = chapterPath.LastIndexOf('/');
        var prefix = slash < 0 ? string.Empty : chapterPath[..(slash + 1)];

        return [.. DevbookReadingConvention.DemosOf(Path.GetFileName(chapterFile), names).Select(name => prefix + name)];
    }

    /// <summary>
    /// The demos a page has: <paramref name="besidePage"/> first, then each place
    /// a <c>demo</c> field names, in document and field order.
    /// </summary>
    /// <param name="blocks">The full parse, indexed as the payload's blocks are.</param>
    /// <param name="besidePage">From <see cref="BesidePage"/>.</param>
    /// <param name="exists">Whether a repository-relative demo path is there, or
    /// null when that cannot be told — a branch snapshot fetched only the chapter.</param>
    internal static IReadOnlyList<ChapterDemoPayload> Of(
        IReadOnlyList<MdBlock> blocks,
        IReadOnlyList<string> besidePage,
        Func<string, bool?> exists)
    {
        var demos = besidePage.Select(path => new ChapterDemoPayload(path, null, ByName, null, true)).ToList();

        for (var index = 0; index < blocks.Count; index++)
        {
            if (blocks[index] is not MdCode code || !MetadataReader.IsMetaBlock(code.Language)) continue;

            foreach (var reference in DevbookReadingConvention.DemoField(FieldValue(code.Text, "demo")))
            {
                demos.Add(new ChapterDemoPayload(reference.Path, reference.Address, ByField, index, exists(reference.Path)));
            }
        }

        return demos;
    }

    /// <summary>One field's raw value in a <c>meta</c> block body, or null. A
    /// later line wins, as it does in the metadata parse.</summary>
    private static string? FieldValue(string body, string field)
    {
        string? value = null;

        foreach (var line in body.Split('\n'))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0) continue;
            if (line[..colon].Trim() == field) value = line[(colon + 1)..].Trim();
        }

        return value;
    }
}
