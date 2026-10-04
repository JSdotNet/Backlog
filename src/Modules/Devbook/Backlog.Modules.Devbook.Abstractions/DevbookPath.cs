namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// The one comparison every Devbook panel selects a chapter by: whether a document's
/// path is the path somebody asked for.
/// <para>
/// A selection arrives in more than one spelling — repository-relative from a
/// search hit, folder-relative from the menu, with backslashes from a Windows path —
/// so both sides are normalised and a selection matches a document whose path is it
/// or ends with it on a segment boundary.
/// </para>
/// <para>
/// Two rules, because the panels need two. An area panel reads <c>.arc42/05.md</c>
/// and <c>arc42/05.md</c> as one chapter: the root layout spells its folders with a
/// dot and the menu names a chapter without one, so <see cref="Normalize"/> drops
/// every leading dot and slash. An instruction source lives in a dot folder —
/// <c>.github</c>, <c>.claude</c>, <c>.agents</c> — and there the dot is the name:
/// <c>github/copilot-instructions.md</c> is a different file, so
/// <see cref="NormalizeKeepingDotFolder"/> trims only a leading <c>./</c> or
/// <c>/</c>, the way <see cref="DevbookLayout"/> reads a path.
/// </para>
/// </summary>
public static class DevbookPath
{
    /// <summary>
    /// A Devbook area path with forward slashes and no leading <c>.</c> or <c>/</c>:
    /// <c>.arc42\adr\0001.md</c> and <c>./arc42/adr/0001.md</c> both become
    /// <c>arc42/adr/0001.md</c>.
    /// </summary>
    public static string Normalize(string? path) =>
        path is null ? string.Empty : path.Replace('\\', '/').TrimStart('.', '/');

    /// <summary>
    /// A path with forward slashes and no leading <c>./</c> or <c>/</c>, but with the
    /// dot of a dot folder kept: <c>./.github\copilot-instructions.md</c> becomes
    /// <c>.github/copilot-instructions.md</c>.
    /// </summary>
    public static string NormalizeKeepingDotFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        var forward = path.Trim().Replace('\\', '/');
        while (forward.StartsWith("./", StringComparison.Ordinal)) forward = forward[2..];
        return forward.TrimStart('/');
    }

    /// <summary>
    /// Whether <paramref name="candidate"/>, a Devbook area document, is the chapter
    /// <paramref name="selected"/> names, under <see cref="Normalize"/>.
    /// </summary>
    public static bool Matches(string? candidate, string? selected) =>
        SuffixMatches(Normalize(candidate), Normalize(selected));

    /// <summary>
    /// Whether <paramref name="candidate"/>, an instruction source, is the document
    /// <paramref name="selected"/> names, under <see cref="NormalizeKeepingDotFolder"/>.
    /// </summary>
    public static bool MatchesKeepingDotFolder(string? candidate, string? selected) =>
        SuffixMatches(NormalizeKeepingDotFolder(candidate), NormalizeKeepingDotFolder(selected));

    private static bool SuffixMatches(string candidate, string selected) =>
        string.Equals(candidate, selected, StringComparison.OrdinalIgnoreCase)
        || candidate.EndsWith($"/{selected}", StringComparison.OrdinalIgnoreCase);
}
