using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Removing a chapter's or file's <c>status</c> field, which is what a reader
/// picking "No status" does.
///
/// <para>The invariant these tests exist for is that the <c>meta</c> fence
/// survives. The fence is what marks a heading as an addressable chapter — the
/// index generator makes one node per heading that carries one — and in
/// <c>.arc42</c> and <c>.design</c>, which define no <c>type</c> field, clearing
/// the status routinely leaves the block empty. Tidying an empty fence away would
/// silently drop the chapter out of the graph, so the writer must not, and
/// <see cref="Keeps_the_fence_when_the_status_was_the_only_field_in_it"/> is the
/// test that says so.</para>
/// </summary>
public class KnowledgeStatusClearTests
{
    [Fact]
    public void Keeps_the_fence_when_the_status_was_the_only_field_in_it()
    {
        var file = WriteDesign("""
            # Colour Scheme

            ```meta
            status: active
            ```

            Tokens.
            """);

        KnowledgeMarkdownStatusWriter.RemoveStatus(file.FolderRoot, ".design/color-scheme.md", ".design/");

        Assert.Equal(
            """
            # Colour Scheme

            ```meta
            ```

            Tokens.
            """,
            file.Read());
    }

    [Fact]
    public void Leaves_every_other_field_and_its_indentation_untouched()
    {
        var file = WriteDesign("""
            # Colour Scheme

            ```meta
            type: design
            status: active
            related: [".arc42/02-constraints.md#technical-constraints"]
            ```

            Tokens.
            """);

        KnowledgeMarkdownStatusWriter.RemoveStatus(file.FolderRoot, ".design/color-scheme.md", ".design/");

        Assert.Equal(
            """
            # Colour Scheme

            ```meta
            type: design
            related: [".arc42/02-constraints.md#technical-constraints"]
            ```

            Tokens.
            """,
            file.Read());
    }

    /// <summary>Removing a line changes the line count, which is exactly where
    /// rejoining with the wrong newline would turn a one-field edit into a
    /// whole-file diff.</summary>
    [Fact]
    public void Keeps_a_crlf_file_on_crlf()
    {
        var file = WriteDesign("# Colour Scheme\r\n\r\n```meta\r\nstatus: active\r\ntype: design\r\n```\r\n\r\nTokens.\r\n");

        KnowledgeMarkdownStatusWriter.RemoveStatus(file.FolderRoot, ".design/color-scheme.md", ".design/");

        var text = file.Read();
        Assert.Equal("# Colour Scheme\r\n\r\n```meta\r\ntype: design\r\n```\r\n\r\nTokens.\r\n", text);
        Assert.DoesNotContain("\n\n\n", text.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void Keeps_an_lf_file_on_lf()
    {
        var file = WriteDesign("# Colour Scheme\n\n```meta\nstatus: active\ntype: design\n```\n\nTokens.\n");

        KnowledgeMarkdownStatusWriter.RemoveStatus(file.FolderRoot, ".design/color-scheme.md", ".design/");

        var text = file.Read();
        Assert.Equal("# Colour Scheme\n\n```meta\ntype: design\n```\n\nTokens.\n", text);
        Assert.DoesNotContain('\r', text);
    }

    [Fact]
    public void Clearing_a_fence_that_states_no_status_writes_nothing()
    {
        const string original = """
            # Colour Scheme

            ```meta
            type: design
            ```

            Tokens.
            """;
        var file = WriteDesign(original);

        KnowledgeMarkdownStatusWriter.RemoveStatus(file.FolderRoot, ".design/color-scheme.md", ".design/");

        Assert.Equal(original, file.Read());
    }

    [Fact]
    public void Clearing_a_heading_with_no_fence_creates_none()
    {
        const string original = """
            # Colour Scheme

            Tokens.
            """;
        var file = WriteDesign(original);

        KnowledgeMarkdownStatusWriter.RemoveStatus(file.FolderRoot, ".design/color-scheme.md", ".design/");

        Assert.Equal(original, file.Read());
        Assert.DoesNotContain("```meta", file.Read(), StringComparison.Ordinal);
    }

    [Fact]
    public void Clearing_one_chapter_leaves_its_siblings_fence_alone()
    {
        var file = WriteDesign("""
            # Colour Scheme

            ```meta
            status: active
            ```

            Tokens.

            ## Surfaces

            ```meta
            status: draft
            ```

            Surface tokens.

            ## Text

            ```meta
            status: proposed
            ```

            Text tokens.
            """);

        KnowledgeMarkdownStatusWriter.RemoveStatus(file.FolderRoot, ".design/color-scheme.md#surfaces", ".design/");

        var text = file.Read();
        Assert.Equal(
            """
            # Colour Scheme

            ```meta
            status: active
            ```

            Tokens.

            ## Surfaces

            ```meta
            ```

            Surface tokens.

            ## Text

            ```meta
            status: proposed
            ```

            Text tokens.
            """,
            text);
    }

    /// <summary>The writer tests prove the bytes; this one proves the contract the
    /// bytes exist for. A cleared chapter has to read back as a chapter still —
    /// heading intact, addressable, carrying an empty record rather than no record
    /// — because that is what the fence was being kept for.</summary>
    [Fact]
    public void A_cleared_chapter_reads_back_as_an_addressable_chapter_with_an_empty_record()
    {
        var file = WriteDesign("""
            # Colour Scheme

            ```meta
            status: active
            ```

            Tokens.

            ## Surfaces

            ```meta
            status: draft
            ```

            Surface tokens.
            """);

        KnowledgeMarkdownStatusWriter.RemoveStatus(file.FolderRoot, ".design/color-scheme.md#surfaces", ".design/");

        var document = KnowledgeMarkdownParser.Parse(".design/color-scheme.md", file.Read());

        Assert.Equal("Colour Scheme", document.Title);
        Assert.Contains(document.Blocks, block => block is KnowledgeHeadingBlock { Level: 2, Text: "Surfaces" });
    }

    [Fact]
    public async Task An_arc42_chapter_can_have_its_status_removed_through_the_store()
    {
        var root = NewRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".arc42"));
            File.WriteAllText(Path.Combine(root, ".arc42", "04-solution-strategy.md"), """
                # Solution Strategy

                ```meta
                status: proposed
                related: [.tech/shared.md]
                ```

                Strategy text.
                """);
            var knowledge = new Arc42KnowledgeStore(new KnowledgeFolderSource(ConfiguredSettings(root)));

            await knowledge.ClearStatusAsync("backlog", ".arc42/04-solution-strategy.md");

            var text = File.ReadAllText(Path.Combine(root, ".arc42", "04-solution-strategy.md"));
            Assert.DoesNotContain("status:", text, StringComparison.Ordinal);
            Assert.Contains("```meta", text, StringComparison.Ordinal);
            Assert.Contains("related: [.tech/shared.md]", text, StringComparison.Ordinal);

            var document = Assert.Single((await knowledge.LoadAsync("backlog")).Documents);
            Assert.True(string.IsNullOrEmpty(document.Status));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task A_domain_chapter_can_have_its_status_removed_through_the_store()
    {
        var root = NewRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".domain", "tasks"));
            File.WriteAllText(Path.Combine(root, ".domain", "tasks", "features.md"), """
                # Task Features

                ```meta
                type: features
                status: draft
                ```

                ## Inbox Capture

                ```meta
                type: feature
                status: active
                ```

                Capture text.
                """);
            var knowledge = new DomainKnowledgeStore(new KnowledgeFolderSource(ConfiguredSettings(root)));

            await knowledge.ClearStatusAsync("backlog", ".domain/tasks/features.md#inbox-capture");

            var text = File.ReadAllText(Path.Combine(root, ".domain", "tasks", "features.md"));
            Assert.Contains("status: draft", text, StringComparison.Ordinal);
            Assert.DoesNotContain("status: active", text, StringComparison.Ordinal);
            Assert.Equal(2, Occurrences(text, "```meta"));
        }
        finally
        {
            Cleanup(root);
        }
    }

    /// <summary>
    /// <c>.tech</c> has no clearing path at all, and that is the design rather than
    /// an omission: its status is a position on an adoption ladder, so an absent one
    /// could not be told apart from <c>candidate</c>. The exemption is expressed as
    /// "the method does not exist" instead of as a guard, because a guard is
    /// something a later caller can copy wrongly.
    /// </summary>
    [Fact]
    public void The_technology_store_offers_no_way_to_clear_a_status()
    {
        Assert.Null(typeof(TechnologyKnowledgeService).GetMethod("ClearStatusAsync"));
        Assert.NotNull(typeof(Arc42KnowledgeStore).GetMethod("ClearStatusAsync"));
        Assert.NotNull(typeof(DomainKnowledgeStore).GetMethod("ClearStatusAsync"));
        Assert.NotNull(typeof(DesignKnowledgeProvider).GetMethod("ClearStatusAsync"));
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "backlog-knowledge-clear-tests", Guid.NewGuid().ToString("N"));

    private static void Cleanup(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    /// <summary>A single `.design` file on disk, plus the folder root the writer
    /// resolves item paths against. The writer is addressed with the folder root
    /// rather than the repository root, which is what the stores hand it.</summary>
    private sealed record DesignFile(string FolderRoot, string Path)
    {
        public string Read() => File.ReadAllText(Path);
    }

    private static DesignFile WriteDesign(string content)
    {
        var folderRoot = System.IO.Path.Combine(NewRoot(), ".design");
        Directory.CreateDirectory(folderRoot);
        var path = System.IO.Path.Combine(folderRoot, "color-scheme.md");
        File.WriteAllText(path, content);

        return new DesignFile(folderRoot, path);
    }

    private static GitHubSettingsStore ConfiguredSettings(string repo)
    {
        var path = Path.Combine(repo, "github.json");
        var settings = new GitHubSettingsStore(path);
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        settings.SetRepositories(repositories);
        settings.SetCloneDirectory("backlog", repo);
        return settings;
    }
}
