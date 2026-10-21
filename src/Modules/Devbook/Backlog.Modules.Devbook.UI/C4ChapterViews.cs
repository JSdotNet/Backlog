using Backlog.UI.Components.Diagrams.C4;
using Backlog.UI.Components.Markdown;

using DevbookC4Graph = Backlog.Infrastructure.Devbook.Building.DevbookC4Graph;
using DevbookMarkdown = Backlog.Infrastructure.Devbook.Building.DevbookMarkdown;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// One C4 view drawn inside the arc42 chapter that documents it, under the section
/// the view map names, beside the chapter's own C4 fence in that section. A view the
/// chapter embeds itself is drawn by its embed instead.
/// </summary>
/// <param name="View">The view, as the catalog lists it.</param>
/// <param name="Workspace">The authored workspace the view is read from.</param>
/// <param name="ModelView">The view in that workspace's model.</param>
/// <param name="SectionIndex">The heading it is drawn under, by its position among
/// the chapter's headings in <see cref="MarkdownChapters.Split"/> order — the position
/// the read view hands its per-heading hook, which is how the embed finds its place,
/// and what keeps two headings of one title apart.</param>
/// <param name="SectionTitle">That heading's text, as <see cref="MarkdownChapters"/>
/// spells it.</param>
/// <param name="SectionLevel">That heading's level.</param>
/// <param name="Fence">The section's first C4 mermaid fence read as a workspace, or
/// null when the section draws none.</param>
/// <param name="FenceView">The one view that fence draws.</param>
/// <param name="OnlyInFence">Element names the fence draws and the model's view does
/// not, in the fence's order.</param>
/// <param name="OnlyInModel">Element names the model's view draws and the fence does
/// not, in the model's order.</param>
public sealed record C4ChapterEmbed(
    C4ViewEntry View,
    C4WorkspaceEntry Workspace,
    C4View ModelView,
    int SectionIndex,
    string SectionTitle,
    int SectionLevel,
    C4Workspace? Fence,
    C4View? FenceView,
    IReadOnlyList<string> OnlyInFence,
    IReadOnlyList<string> OnlyInModel)
{
    /// <summary>Whether the chapter's fence and the model draw different elements.
    /// False when there is no fence to compare: nothing disagrees with a fence that is
    /// not there.</summary>
    public bool Disagrees => OnlyInFence.Count > 0 || OnlyInModel.Count > 0;
}

/// <summary>
/// Which C4 views an arc42 chapter documents, where in the chapter each one belongs,
/// and how each differs from the C4 fence the chapter draws there.
/// <para>
/// Read off the same two authored statements everything else reads:
/// <c>_c4/references.json</c> says which chapter and section a view documents, and
/// the chapter's own text says what its fence draws. Nothing is stored. The chapter
/// stays canonical — this only says where the model and the chapter part ways, by
/// element name, and never edits either.
/// </para>
/// <para>
/// A model drawn from the chapters' own fences is left out. Its views <em>are</em>
/// those fences, so embedding one would draw the fence twice and compare it with
/// itself.
/// </para>
/// </summary>
public static class C4ChapterViews
{
    /// <summary>
    /// The views to draw in this chapter, in the order the catalog lists them.
    /// <para>
    /// A reference whose <c>#section</c> names no heading of the chapter — or names
    /// none at all — is drawn under the chapter's first heading rather than dropped:
    /// the view still documents the chapter, and an anchor that drifted is no reason
    /// to hide that. A chapter with no heading has nowhere to draw it.
    /// </para>
    /// </summary>
    public static IReadOnlyList<C4ChapterEmbed> For(C4Catalog catalog, string? chapterPath, string? chapterText)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (!catalog.HasViews || string.IsNullOrWhiteSpace(chapterPath) || string.IsNullOrEmpty(chapterText)) return [];

        var chapters = MarkdownChapters.Split(chapterText);
        if (chapters.Count == 0) return [];

        var wanted = C4ViewEntry.FileOf(chapterPath);
        var embedded = EmbeddedKeys(chapterText);
        var embeds = new List<C4ChapterEmbed>();

        foreach (var view in catalog.Views)
        {
            if (catalog.WorkspaceOf(view) is not { Derived: false } workspace) continue;
            if (embedded.Contains(view.Key)) continue;
            if (workspace.Workspace.View(view.Key) is not { } modelView) continue;

            var reference = view.Documents.FirstOrDefault(entry =>
                string.Equals(C4ViewEntry.FileOf(entry), wanted, StringComparison.OrdinalIgnoreCase));
            if (reference is null) continue;

            var (index, named) = SectionFor(chapters, Anchor(reference));
            embeds.Add(Compare(view, workspace, modelView, index, chapters[index], named, chapterPath));
        }

        return embeds;
    }

    /// <summary>The views the chapter already draws itself, as a
    /// <c>![…](c4:&lt;view key&gt;)</c> embed, read the way the store reads them. Such a
    /// view is drawn where the chapter put it, so it is not drawn a second time under
    /// its section.</summary>
    private static HashSet<string> EmbeddedKeys(string chapterText)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!chapterText.Contains("c4:", StringComparison.OrdinalIgnoreCase)) return keys;

        foreach (var (key, _) in DevbookC4Graph.Embeds(chapterText, DevbookMarkdown.Parse(chapterText).Chapters))
        {
            if (key.Length > 0) keys.Add(key);
        }

        return keys;
    }

    /// <summary>The embeds drawn under one heading, matched on its position, title and
    /// level as the read view reports them. The title and level are checked as well as
    /// the position so that a read view whose headings did not line up with the text
    /// draws nothing rather than a view under the wrong heading.</summary>
    public static IReadOnlyList<C4ChapterEmbed> Under(IReadOnlyList<C4ChapterEmbed> embeds, int position, string title, int level) =>
        [.. embeds.Where(embed => embed.SectionIndex == position
            && embed.SectionLevel == level
            && string.Equals(embed.SectionTitle, title, StringComparison.Ordinal))];

    /// <summary>The heading's anchor, made the way the panel and the knowledge writer
    /// make it: letters and digits kept, every run of anything else one hyphen.</summary>
    public static string Slug(string heading)
    {
        var chars = heading.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? Anchor(string reference)
    {
        var hash = reference.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 || hash == reference.Length - 1 ? null : reference[(hash + 1)..];
    }

    /// <summary>The section a reference names, by position, and whether it named
    /// one — or the chapter's first heading when it named none that is there.</summary>
    private static (int Index, bool Named) SectionFor(IReadOnlyList<MarkdownChapter> chapters, string? anchor)
    {
        if (anchor is not null)
        {
            var slug = Slug(anchor);
            for (var index = 0; index < chapters.Count; index++)
            {
                if (string.Equals(Slug(chapters[index].Title), slug, StringComparison.Ordinal)) return (index, true);
            }
        }

        return (0, false);
    }

    private static C4ChapterEmbed Compare(
        C4ViewEntry view,
        C4WorkspaceEntry workspace,
        C4View modelView,
        int index,
        MarkdownChapter section,
        bool named,
        string chapterPath)
    {
        var modelNames = Names(workspace.Workspace, modelView);
        var none = new C4ChapterEmbed(view, workspace, modelView, index, section.Title, section.Level, null, null, [], []);

        // The section's first C4 fence, its sub-sections included — a chapter
        // runs to the next heading that is not beneath it, which is the reading
        // MarkdownChapters already gives. A reference that named no section has the
        // whole chapter as its section, and there the first C4 fence may well draw
        // another view: only a fence of the view's own kind is compared, and none
        // is when the chapter has no such fence.
        var fences = C4DevbookStore.C4Fences(chapterPath, section.Text)
            .Select(source => C4MermaidReader.Read(null, [source]))
            .Where(read => read.Views.Count > 0);
        if (!named) fences = fences.Where(read => read.Views[0].Kind == modelView.Kind);

        if (fences.FirstOrDefault() is not { } fence) return none;
        var fenceView = fence.Views[0];

        var fenceNames = Names(fence, fenceView);
        var inModel = new HashSet<string>(modelNames.Select(Key), StringComparer.Ordinal);
        var inFence = new HashSet<string>(fenceNames.Select(Key), StringComparer.Ordinal);

        return new C4ChapterEmbed(
            view,
            workspace,
            modelView,
            index,
            section.Title,
            section.Level,
            fence,
            fenceView,
            [.. fenceNames.Where(name => !inModel.Contains(Key(name)))],
            [.. modelNames.Where(name => !inFence.Contains(Key(name)))]);
    }

    /// <summary>The names of the elements a view draws, each once, in the order the
    /// writer selects them — the same selection both sides go through, so a boundary
    /// counts on both or on neither.</summary>
    private static List<string> Names(C4Workspace workspace, C4View view)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var names = new List<string>();

        foreach (var element in C4MermaidWriter.VisibleElements(workspace, view))
        {
            var name = element.Name.Trim();
            if (name.Length > 0 && seen.Add(Key(name))) names.Add(name);
        }

        return names;
    }

    /// <summary>A name as two spellings of it agree: case and runs of white space
    /// folded.</summary>
    private static string Key(string name) =>
        string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
