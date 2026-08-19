namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// Which knowledge chapter is being edited: the area it belongs to, the folder
/// that bounds it, and where it sits beneath that folder.
/// <para>
/// The bounding folder travels with the path rather than being looked up again
/// at write time, because "inside the knowledge root" only means anything
/// against the root the chapter was resolved under — a repository can point an
/// area anywhere, and the same relative path is a different file under a
/// different root. A ref is therefore the whole answer to "which file", and a
/// panel that cannot produce one has no chapter to offer an edit on.
/// </para>
/// </summary>
/// <param name="AreaKey">The knowledge area this chapter belongs to, as the menu
/// and the area catalog name it — <c>arc42</c>, <c>domain</c>, <c>tech</c>,
/// <c>design</c>, <c>instructions</c>.</param>
/// <param name="RootPath">The containment boundary: the resolved folder for the
/// area, or the repository root for instructions.</param>
/// <param name="RelativePath">Where the chapter sits beneath
/// <paramref name="RootPath"/>, with forward slashes and no area prefix.</param>
public sealed record KnowledgeChapterRef(string AreaKey, string RootPath, string RelativePath)
{
    /// <summary>
    /// Where the chapter is on disk.
    /// <para>
    /// Meaningful only while it stays under <see cref="RootPath"/>: a relative
    /// path that climbs out, or one that is rooted somewhere else entirely,
    /// still combines into something — it just is not this area's chapter. The
    /// resolver refuses to build such a ref and the writer re-checks before it
    /// touches anything, so this property never has to be the check itself.
    /// </para>
    /// </summary>
    public string FullPath => Path.GetFullPath(Path.Combine(RootPath, RelativePath));
}
