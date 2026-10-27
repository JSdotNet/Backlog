using Backlog.SharedKernel.Devbook;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Cohesion tab names a platform by its <c>.tech</c> chapter's heading, read
/// by <see cref="DevbookChapterHeadings"/>: these pin the heading parse and the
/// read against the repository's own technology folder.
/// </summary>
public sealed class DevbookChapterHeadingsTests
{
    [Fact]
    public void Only_level_two_headings_outside_a_fence_are_read_by_their_slug()
    {
        var headings = DevbookChapterHeadings.Parse("""
            # Title

            ## Model Context Protocol Servers

            ### Not a chapter

            ````markdown
            ```
            ## Inside a fence
            ```
            ````

                ## Indented as code

            ## Claude Code Hooks ##

            ## Node.js
            """);

        Assert.Equal("Model Context Protocol Servers", headings["model-context-protocol-servers"]);
        Assert.Equal("Claude Code Hooks", headings["claude-code-hooks"]);

        // The GitHub slug the checker resolves a reference by, and the hyphenated one.
        Assert.Equal("Node.js", headings["nodejs"]);
        Assert.Equal("Node.js", headings["node-js"]);
        Assert.DoesNotContain("inside-a-fence", headings.Keys);
        Assert.DoesNotContain("indented-as-code", headings.Keys);
    }

    [Fact]
    public void The_repository_s_own_tech_chapters_answer_with_their_headings()
    {
        var references = new[]
        {
            DevbookReference.Parse(".devbook/tech/ai-development.md#model-context-protocol-servers")!,
            DevbookReference.Parse(".devbook/tech/ai-development.md#claude-code-hooks")!,
            DevbookReference.Parse(".devbook/tech/ai-development.md#claude-code")!,
            DevbookReference.Parse(".devbook/tech/ai-development.md#no-such-chapter")!,
            DevbookReference.Parse(".devbook/tech/no-such-file.md#anything")!
        };

        var names = DevbookChapterHeadings.Read(RepositoryRoot.Root.FullName, references);

        Assert.Equal("Model Context Protocol Servers", names[DevbookChapterHeadings.KeyOf(references[0])]);
        Assert.Equal("Claude Code Hooks", names[DevbookChapterHeadings.KeyOf(references[1])]);
        Assert.Equal("Claude Code", names[DevbookChapterHeadings.KeyOf(references[2])]);
        Assert.Equal(3, names.Count);
    }

    [Fact]
    public void A_path_that_leaves_the_repository_reads_nothing()
    {
        var parent = Directory.CreateTempSubdirectory("headings-");
        try
        {
            var root = Directory.CreateDirectory(Path.Combine(parent.FullName, "repo"));
            System.IO.File.WriteAllText(Path.Combine(parent.FullName, "outside.md"), "## Secret\n");
            System.IO.File.WriteAllText(Path.Combine(root.FullName, "inside.md"), "## Shared\n");

            var outside = DevbookReference.Parse("../outside.md#secret");
            var inside = DevbookReference.Parse("inside.md#shared");
            Assert.NotNull(outside);
            Assert.NotNull(inside);

            var names = DevbookChapterHeadings.Read(root.FullName, [outside, inside]);

            Assert.Equal("Shared", Assert.Single(names).Value);
            Assert.Empty(DevbookChapterHeadings.Read(null, [inside]));
        }
        finally
        {
            parent.Delete(recursive: true);
        }
    }
}
