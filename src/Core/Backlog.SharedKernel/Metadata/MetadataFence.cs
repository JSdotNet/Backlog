using Backlog.SharedKernel.Markdown;

namespace Backlog.SharedKernel.Metadata;

/// <summary>
/// The <c>meta</c> fence under a heading, found the way every devbook reader looks
/// for it: past blank lines, the first line opens a fence (<see cref="MarkdownFence"/>)
/// whose language is <see cref="MetadataReader.FenceLanguage"/>, and the fence ends
/// where CommonMark closes it.
/// <para>
/// One finder rather than a copy per reader, each with its own "starts with three
/// backticks": that shortcut closed a block at an inner <c>```</c>, which is the
/// mistake <see cref="MarkdownFence"/> exists to stop.
/// </para>
/// </summary>
/// <param name="Start">The index of the fence's opening line.</param>
/// <param name="End">The index one past its closing line — past the end of the
/// lines for an unterminated fence.</param>
/// <param name="Body">The lines between, joined with <c>\n</c>.</param>
public sealed record MetadataFence(int Start, int End, string Body)
{
    /// <summary>The <c>meta</c> fence on the first non-blank line at or after
    /// <paramref name="from"/>, or null when that line opens none.</summary>
    public static MetadataFence? At(IReadOnlyList<string> lines, int from)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var at = Math.Max(from, 0);
        while (at < lines.Count && string.IsNullOrWhiteSpace(lines[at])) at++;
        if (at >= lines.Count || MarkdownFence.Open(lines[at]) is not { } fence || !MetadataReader.IsMetaBlock(fence.Language)) return null;

        var body = new List<string>();
        var index = at + 1;
        while (index < lines.Count && !fence.IsClosedBy(lines[index]))
        {
            body.Add(lines[index]);
            index++;
        }

        return new MetadataFence(at, Math.Min(index + 1, lines.Count), string.Join('\n', body));
    }

    /// <summary>
    /// Whether a line, already trimmed by the caller's own rule, is the opener the
    /// devbook generator recognises: exactly <c>```meta</c> — three backticks and the
    /// language, nothing else. Stricter than <see cref="At"/>, which takes any fence
    /// CommonMark opens with that language; the ports of <c>metadata.mjs</c> ask
    /// this so their rows stay the generator's.
    /// </summary>
    public static bool IsGeneratorOpener(string? trimmedLine) =>
        trimmedLine is { Length: 7 }
        && MarkdownFence.Open(trimmedLine) is { Marker: '`', Length: 3, Language: MetadataReader.FenceLanguage };

    /// <summary>The block's fields.</summary>
    public MetadataRecord Record => MetadataReader.Parse(Body);

    /// <summary>
    /// The block's fields as one string per key, a list's entries joined with
    /// <c>, </c> — the flat shape the Domain readers key on. Read by the same field
    /// reader <see cref="MetadataReader.Parse"/> is built on, so keys are lower-cased,
    /// values unquoted, and an empty or <c>null</c> value is absent.
    /// </summary>
    public Dictionary<string, string> Flat()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, values) in MetadataReader.ReadFields(Body)) result[key] = string.Join(", ", values);
        return result;
    }
}
