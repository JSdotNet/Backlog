using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

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
/// heading — one row per area, in tab order, with <c>Area</c>, <c>Folder</c> and
/// <c>Holds</c> columns. Reading it back is <see cref="BrainConventions"/>'s, the
/// one reader the database builder and the Brain folder source use too;
/// <see cref="ReadDeclaredAreas"/> asks it, and a file that declares none falls
/// back to <see cref="ParaAreas"/>, the PARA set the ADR names.
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
    public static IReadOnlyList<BrainArea> ParaAreas => BrainConventions.Para;

    /// <summary>The shipped <c>README.md</c>.</summary>
    public static string Readme { get; } = ReadResource(ReadmeFileName);

    /// <summary>The shipped <c>CONVENTIONS.md</c>, declaring <see cref="ParaAreas"/>.</summary>
    public static string Conventions { get; } = ReadResource(ConventionsFileName);

    /// <summary>
    /// The areas a <c>CONVENTIONS.md</c> declares, in the order it declares them,
    /// or <see cref="ParaAreas"/> when it declares none — <see cref="BrainConventions.Parse"/>.
    /// <para>
    /// Deliberately forgiving: a row whose folder is not one plain folder name —
    /// empty, nested, rooted, or climbing out with <c>..</c> — is skipped rather
    /// than refused, because the file is the person's to edit and a half-typed row
    /// must not cost them the rest. A folder declared twice counts once.
    /// </para>
    /// </summary>
    public static IReadOnlyList<BrainArea> ReadDeclaredAreas(string? conventionsMarkdown) =>
        BrainConventions.Parse(conventionsMarkdown);

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
