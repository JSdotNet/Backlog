namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The identity half of editing a knowledge chapter: which file a selection
/// means, for each of the five areas, and when the honest answer is "none".
/// <para>
/// Null is the load-bearing case. It is what makes an editing surface render
/// read-only, so every way a selection can fail to name a file is tested for it
/// rather than for an exception the panels would have to catch.
/// </para>
/// </summary>
public sealed class KnowledgeChapterResolverTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    [Fact]
    public void An_arc42_document_path_resolves_with_its_area_prefix()
    {
        var root = Area(".arc42", "08-crosscutting-concepts.md");

        var chapter = KnowledgeChapterResolver.TryResolve("arc42", root, ".arc42/08-crosscutting-concepts.md");

        Assert.NotNull(chapter);
        Assert.Equal("arc42", chapter.AreaKey);
        Assert.Equal("08-crosscutting-concepts.md", chapter.RelativePath);
        Assert.Equal(Path.Combine(root, "08-crosscutting-concepts.md"), chapter.FullPath);
    }

    [Fact]
    public void An_arc42_menu_path_resolves_without_the_area_prefix()
    {
        var root = Area(".arc42", "08-crosscutting-concepts.md");

        var chapter = KnowledgeChapterResolver.TryResolve("arc42", root, "08-crosscutting-concepts.md");

        Assert.NotNull(chapter);
        Assert.Equal("08-crosscutting-concepts.md", chapter.RelativePath);
    }

    [Fact]
    public void A_nested_arc42_decision_record_resolves()
    {
        var root = Area(".arc42", "adr/0001-use-markdown.md");

        var chapter = KnowledgeChapterResolver.TryResolve("arc42", root, ".arc42/adr/0001-use-markdown.md");

        Assert.NotNull(chapter);
        Assert.Equal("adr/0001-use-markdown.md", chapter.RelativePath);
        Assert.True(File.Exists(chapter.FullPath));
    }

    [Fact]
    public void A_domain_selection_drops_its_heading_anchor()
    {
        var root = Area(".domain", "context-map.md");

        var chapter = KnowledgeChapterResolver.TryResolve("domain", root, ".domain/context-map.md#second-brain");

        Assert.NotNull(chapter);
        Assert.Equal("context-map.md", chapter.RelativePath);
    }

    [Fact]
    public void A_technology_layer_file_name_resolves_against_the_tech_folder()
    {
        var root = Area(".tech", "frontend.md");

        var chapter = KnowledgeChapterResolver.TryResolve("tech", root, "frontend.md");

        Assert.NotNull(chapter);
        Assert.Equal("frontend.md", chapter.RelativePath);
    }

    [Fact]
    public void A_design_file_name_resolves_against_the_design_folder()
    {
        var root = Area(".design", "interaction-guidelines.md");

        var chapter = KnowledgeChapterResolver.TryResolve("design", root, "interaction-guidelines.md");

        Assert.NotNull(chapter);
        Assert.Equal("interaction-guidelines.md", chapter.RelativePath);
    }

    [Fact]
    public void An_instruction_document_resolves_against_the_repository_root_with_its_leading_dot_folder()
    {
        // The instructions area has no folder of its own — its configured path is
        // empty, so its root is the repository and .github/ is part of the
        // relative path rather than a prefix to strip.
        var root = TempDir();
        Write(root, ".github/copilot-instructions.md");

        var chapter = KnowledgeChapterResolver.TryResolve("instructions", root, ".github/copilot-instructions.md");

        Assert.NotNull(chapter);
        Assert.Equal("instructions", chapter.AreaKey);
        Assert.Equal(".github/copilot-instructions.md", chapter.RelativePath);
    }

    [Fact]
    public void An_instruction_selection_naming_agent_finds_the_agents_folder()
    {
        var root = TempDir();
        Write(root, ".agents/review.md");

        var chapter = KnowledgeChapterResolver.TryResolve("instructions", root, ".agent/review.md");

        Assert.NotNull(chapter);
        Assert.Equal(".agents/review.md", chapter.RelativePath);
    }

    [Fact]
    public void A_windows_separator_selection_resolves()
    {
        var root = Area(".arc42", "adr/0001-use-markdown.md");

        var chapter = KnowledgeChapterResolver.TryResolve("arc42", root, @".arc42\adr\0001-use-markdown.md");

        Assert.NotNull(chapter);
        Assert.Equal("adr/0001-use-markdown.md", chapter.RelativePath);
    }

    [Fact]
    public void A_configured_folder_key_names_the_same_area_as_its_menu_key()
    {
        var root = Area(".domain", "context-map.md");

        var chapter = KnowledgeChapterResolver.TryResolve(".domain", root, "context-map.md");

        Assert.NotNull(chapter);
        Assert.Equal("domain", chapter.AreaKey);
    }

    [Fact]
    public void A_selection_that_climbs_out_of_the_root_resolves_to_nothing()
    {
        var root = Area(".arc42", "08-crosscutting-concepts.md");
        Write(Directory.GetParent(root)!.FullName, "secrets.md");

        Assert.Null(KnowledgeChapterResolver.TryResolve("arc42", root, "../secrets.md"));
    }

    [Fact]
    public void An_absolute_selection_outside_the_root_resolves_to_nothing()
    {
        var root = Area(".arc42", "08-crosscutting-concepts.md");
        var outside = Path.Combine(Directory.GetParent(root)!.FullName, "secrets.md");
        File.WriteAllText(outside, "# Secrets\n");

        Assert.Null(KnowledgeChapterResolver.TryResolve("arc42", root, outside));
    }

    [Fact]
    public void A_selection_with_no_file_behind_it_resolves_to_nothing()
    {
        var root = Area(".arc42", "08-crosscutting-concepts.md");

        Assert.Null(KnowledgeChapterResolver.TryResolve("arc42", root, "99-not-written-yet.md"));
    }

    [Fact]
    public void A_folder_selection_resolves_to_nothing()
    {
        var root = Area(".arc42", "adr/0001-use-markdown.md");

        Assert.Null(KnowledgeChapterResolver.TryResolve("arc42", root, "adr"));
    }

    [Fact]
    public void An_empty_selection_resolves_to_nothing()
    {
        var root = Area(".arc42", "08-crosscutting-concepts.md");

        Assert.Null(KnowledgeChapterResolver.TryResolve("arc42", root, null));
        Assert.Null(KnowledgeChapterResolver.TryResolve("arc42", root, "   "));
    }

    [Fact]
    public void An_available_folder_location_resolves_the_same_way_as_its_path()
    {
        var root = Area(".arc42", "08-crosscutting-concepts.md");
        var location = new KnowledgeFolderLocation(".arc42", true, null, "JSdotNet/Backlog", null, root);

        var chapter = KnowledgeChapterResolver.TryResolve("arc42", location, ".arc42/08-crosscutting-concepts.md");

        Assert.NotNull(chapter);
        Assert.Equal("08-crosscutting-concepts.md", chapter.RelativePath);
    }

    [Fact]
    public void An_unavailable_folder_location_resolves_to_nothing()
    {
        var root = Area(".arc42", "08-crosscutting-concepts.md");
        var location = KnowledgeFolderLocation.Unavailable(".arc42", "Architecture knowledge is turned off.", fullPath: root);

        Assert.Null(KnowledgeChapterResolver.TryResolve("arc42", location, "08-crosscutting-concepts.md"));
        Assert.Null(KnowledgeChapterResolver.TryResolve("arc42", (KnowledgeFolderLocation?)null, "08-crosscutting-concepts.md"));
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>A knowledge area folder inside a throwaway repository, with one
    /// chapter in it — the arrangement every area resolves against.</summary>
    private string Area(string folderName, string relativePath)
    {
        var root = Path.Combine(TempDir(), folderName);
        Directory.CreateDirectory(root);
        Write(root, relativePath);
        return root;
    }

    private static void Write(string root, string relativePath)
    {
        var filePath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, "# Chapter\n\n```meta\nstatus: draft\n```\n");
    }

    private string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "knowledge-chapter-resolver-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }
}
