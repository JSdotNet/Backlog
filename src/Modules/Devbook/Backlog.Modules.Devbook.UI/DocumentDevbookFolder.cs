using Backlog.UI.Components.Devbook;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// A devbook folder that is a flat set of Markdown documents — a root document,
/// its siblings, and a <c>##</c> chapter per subject in each — and nothing
/// structural beyond that. <c>.design</c> is one; <c>.ai</c> is another.
/// <para>
/// The two are read and drawn by the same provider and the same view, and this
/// record is everything that differs between them: where the folder is, what it
/// is called, which status vocabulary its chapters speak, and which file opens
/// it. It exists so that the view can be handed the folder rather than knowing
/// one — the design view used to spell <c>.design</c> in five places, and the
/// first folder shaped like it would have had to copy all eleven hundred lines
/// to change the word.
/// </para>
/// <para>
/// Not every devbook folder fits. <c>.arc42</c> has record subfolders,
/// <c>.domain</c> has one directory per bounded context, and <c>.tech</c> is a
/// graph drawn from one file; each of those has a panel of its own. This is for
/// the folders whose whole structure is "documents, in reading order".
/// </para>
/// </summary>
/// <param name="Key">The folder's settings key and conventional path, <c>.design</c>.</param>
/// <param name="AreaKey">The pane's section key, <c>design</c> — what the area
/// strip, the menu and the atlas pass around.</param>
/// <param name="DisplayName">What a reader is shown, <c>Design</c>.</param>
/// <param name="Folder">The component library's name for it, which picks the
/// status vocabulary.</param>
/// <param name="RootDocument">The file that opens the folder and sorts first,
/// <c>README.md</c> or <c>adoption-map.md</c>. The folder's own
/// <c>_reading-order.json</c> may name one too; this is the convention that holds
/// when it does not.</param>
public sealed record DocumentDevbookFolder(
    string Key,
    string AreaKey,
    string DisplayName,
    DevbookFolder Folder,
    string RootDocument)
{
    /// <summary>The design guidance: principles, tokens, interaction rules.</summary>
    public static DocumentDevbookFolder Design { get; } =
        new(".design", "design", "Design", DevbookFolder.Design, "README.md");

    /// <summary>
    /// The AI adoption record: how this project develops <em>with</em> AI, stage
    /// by stage. The root is the adoption map rather than a README, and the
    /// stage files are numbered so the flow orders itself — which is why the
    /// folder declares no reading order and this record has to know its root.
    /// </summary>
    public static DocumentDevbookFolder Ai { get; } =
        new(".ai", "ai", "AI", DevbookFolder.Ai, "adoption-map.md");

    /// <summary>The folder's path prefix as a reference spells it, <c>.design/</c>.</summary>
    public string PathPrefix => Key + "/";

    /// <summary>The repository-relative path of a file in this folder, with the
    /// separators a reference uses.</summary>
    public string DocumentPath(string fileName) =>
        PathPrefix + fileName.Replace('\\', '/').TrimStart('/');
}
