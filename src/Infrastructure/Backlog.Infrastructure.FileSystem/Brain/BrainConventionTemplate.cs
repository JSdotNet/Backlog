namespace Backlog.Infrastructure.FileSystem;

/// <summary>One area a Brain declares: the name on its tab, the top folder that
/// holds it, and a line on what goes there.</summary>
public sealed record BrainArea(string Name, string Folder, string Holds);

/// <summary>
/// The convention the app ships for a new Brain: the <c>README.md</c> and
/// <c>CONVENTIONS.md</c> that <b>Create conventions</b> writes, and the areas the
/// second one declares.
/// <para>
/// Local ADR 0026 puts the convention in the folder, so a second machine reading
/// the synced folder sees the same areas; this is only where a Brain's first copy
/// comes from. Both files are embedded in the assembly rather than built from
/// strings here, so what lands in the person's folder can be read and reviewed as
/// the Markdown it is (<c>Brain/Templates/</c>).
/// </para>
/// <para>
/// <c>CONVENTIONS.md</c> declares its areas as a table under an <c>## Areas</c>
/// heading — one row per area, in tab order, with the folder in the second
/// column. <see cref="ReadDeclaredAreas"/> reads that table back; a file that
/// declares none falls back to <see cref="ParaAreas"/>, the PARA set the ADR
/// names.
/// </para>
/// </summary>
public static class BrainConventionTemplate
{
    /// <summary>The file that marks the Brain's root, with <c>index: root</c>.</summary>
    public const string ReadmeFileName = "README.md";

    /// <summary>The file that declares the Brain's areas.</summary>
    public const string ConventionsFileName = "CONVENTIONS.md";

    private const string ResourcePrefix = "Backlog.Brain.";

    /// <summary>The PARA set, in tab order: what a Brain has when its
    /// <c>CONVENTIONS.md</c> declares no areas (local ADR 0026, §2).</summary>
    public static IReadOnlyList<BrainArea> ParaAreas { get; } =
    [
        new("Inbox", "_inbox", "Notes not yet filed; where a capture lands."),
        new("Projects", "projects", "Active work with a goal and a deadline."),
        new("Areas", "areas", "Ongoing responsibilities without a deadline."),
        new("Resources", "resources", "Reference material and collected knowledge."),
        new("People", "people", "Notes about the people the work involves."),
        new("Journal", "journal", "Dated entries."),
        new("Archive", "archive", "Inactive notes kept for future search."),
    ];

    /// <summary>The shipped <c>README.md</c>.</summary>
    public static string Readme { get; } = ReadResource(ReadmeFileName);

    /// <summary>The shipped <c>CONVENTIONS.md</c>, declaring <see cref="ParaAreas"/>.</summary>
    public static string Conventions { get; } = ReadResource(ConventionsFileName);

    /// <summary>
    /// The areas a <c>CONVENTIONS.md</c> declares, in the order it declares them,
    /// or <see cref="ParaAreas"/> when it declares none.
    /// <para>
    /// Deliberately forgiving: a row whose folder is not one plain folder name —
    /// empty, nested, rooted, or climbing out with <c>..</c> — is skipped rather
    /// than refused, because the file is the person's to edit and a half-typed row
    /// must not cost them the rest. A folder declared twice counts once.
    /// </para>
    /// </summary>
    public static IReadOnlyList<BrainArea> ReadDeclaredAreas(string? conventionsMarkdown)
    {
        if (string.IsNullOrWhiteSpace(conventionsMarkdown)) return ParaAreas;

        var areas = new List<BrainArea>();
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inAreas = false;
        var inFence = false;

        foreach (var raw in conventionsMarkdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();

            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence) continue;

            if (line.StartsWith('#'))
            {
                var heading = line.TrimStart('#').Trim();
                inAreas = line.StartsWith("## ", StringComparison.Ordinal)
                    && string.Equals(heading, "Areas", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inAreas || !line.StartsWith('|')) continue;

            var cells = line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
            if (cells.Length < 2) continue;

            var name = cells[0];
            var folder = cells[1].Trim('`').Trim();

            // The header row and the rule under it.
            if (string.Equals(folder, "Folder", StringComparison.OrdinalIgnoreCase)) continue;
            if (folder.Length > 0 && folder.All(c => c is '-' or ':')) continue;

            if (!IsPlainFolderName(folder) || !folders.Add(folder)) continue;

            areas.Add(new BrainArea(
                string.IsNullOrWhiteSpace(name) ? folder : name,
                folder,
                cells.Length > 2 ? cells[2] : string.Empty));
        }

        return areas.Count == 0 ? ParaAreas : areas;
    }

    /// <summary>One folder directly under the Brain, and nothing that resolves
    /// anywhere else.</summary>
    internal static bool IsPlainFolderName(string folder) =>
        folder.Length > 0
        && folder is not "." and not ".."
        && folder.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && folder.IndexOfAny(['/', '\\', ':']) < 0
        && folder.Trim() == folder
        && !folder.EndsWith('.');

    private static string ReadResource(string fileName)
    {
        var assembly = typeof(BrainConventionTemplate).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourcePrefix + fileName)
            ?? throw new InvalidOperationException($"The Brain template '{fileName}' is not embedded in {assembly.GetName().Name}.");
        using var reader = new StreamReader(stream);

        // One line ending, whatever the checkout used, so a scaffolded file reads
        // the same on every machine that writes one.
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }
}
