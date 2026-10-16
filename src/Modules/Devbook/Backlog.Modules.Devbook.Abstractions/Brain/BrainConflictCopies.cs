namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// Finds the OneDrive conflict copies in a Brain (local ADR 0026 §5): when two
/// machines edit one note before either has synced, OneDrive keeps both, the
/// second as <c>&lt;name&gt;-&lt;MACHINE&gt;.md</c> beside <c>&lt;name&gt;.md</c>.
/// The app shows such a pair and never merges or removes either file, so this
/// only reads names.
///
/// <para>A note is a copy when, in the same folder, a note named by the part
/// before one of its hyphens exists, and the part after it is 1–15 characters of
/// letters, digits and hyphens with at least one letter and no lowercase letter —
/// the computer name, which OneDrive writes in capitals and which may itself hold
/// a hyphen (<c>plan-DESKTOP-AB12.md</c>). That keeps <c>plan-2.md</c>,
/// <c>plan-draft.md</c> and <c>my-plan.md</c> ordinary notes. Names compare
/// exactly; only the <c>.md</c> extension compares in any case.</para>
/// </summary>
public static class BrainConflictCopies
{
    private const string Extension = ".md";

    /// <summary>The longest computer name Windows allows (NetBIOS).</summary>
    private const int MaxSuffixLength = 15;

    /// <summary>
    /// The conflict pairs among <paramref name="relativePaths"/> — paths relative
    /// to the Brain's root, separated by <c>/</c> or <c>\</c> — ordered by the
    /// original's path and then the copy's. Paths come back <c>/</c>-separated.
    /// </summary>
    public static IReadOnlyList<BrainConflictCopy> Find(IEnumerable<string> relativePaths)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);

        var notes = relativePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Replace('\\', '/'))
            .Where(path => path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Keyed by folder and name without the extension, so plan.md and
        // plan.MD are one original whichever spelling the folder holds.
        var originals = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var note in notes) originals.TryAdd(StemKey(note), note);

        var pairs = new List<BrainConflictCopy>();
        foreach (var note in notes)
        {
            var slash = note.LastIndexOf('/');
            var folder = slash < 0 ? string.Empty : note[..(slash + 1)];
            var stem = note[(slash + 1)..^Extension.Length];

            // From the first hyphen on, but never at index 0: an original needs a
            // name, and -draft.md is one whose own name starts with a hyphen.
            for (var hyphen = stem.IndexOf('-'); hyphen >= 0; hyphen = stem.IndexOf('-', hyphen + 1))
            {
                if (hyphen == 0 || !IsMachineName(stem[(hyphen + 1)..])) continue;
                if (!originals.TryGetValue(folder + stem[..hyphen], out var original)) continue;

                pairs.Add(new BrainConflictCopy(original, note));
                break;
            }
        }

        return
        [
            .. pairs
                .OrderBy(pair => pair.Original, StringComparer.Ordinal)
                .ThenBy(pair => pair.Copy, StringComparer.Ordinal)
        ];
    }

    private static string StemKey(string note) => note[..^Extension.Length];

    private static bool IsMachineName(string suffix) =>
        suffix.Length is > 0 and <= MaxSuffixLength
        && suffix.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '-')
        && suffix.Any(char.IsAsciiLetterUpper);
}

/// <summary>One OneDrive conflict pair in the Brain: the note and the copy kept
/// beside it, both relative to the Brain's root and <c>/</c>-separated.</summary>
public sealed record BrainConflictCopy(string Original, string Copy);
