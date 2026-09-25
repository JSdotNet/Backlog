using System.Text.RegularExpressions;

using Backlog.Infrastructure.GitHub;
using Backlog.SharedKernel.Ai;
using Backlog.Tests;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A devbook <c>annotation</c> fence is not chapter content
/// (<c>devbook-annotations.md</c>): the older block readers in the Devbook panels
/// hand it to the review-note renderer or step over it whole, never read it as
/// prose, a section's excerpt or a diagram, and the Ask AI source cuts it — and
/// the review triad — out of what it sends a model.
///
/// <para>The notes are the rule's own examples, read out of the vendored copy, and
/// one built from them whose body quotes a code sample: the case a reader closing
/// a fence at the first inner <c>```</c> used to split in two.</para>
/// </summary>
public sealed class DevbookAnnotationContentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-devbook-annotation-tests", Guid.NewGuid().ToString("n"));

    private static IReadOnlyList<string> RuleExamples { get; } =
    [.. Regex.Matches(
            File.ReadAllText(RepositoryRoot.File(["tests", "Fixtures", "devbook-rules", "devbook-annotations.md"])).Replace("\r\n", "\n"),
            "```annotation\n(.*?)\n```",
            RegexOptions.Singleline)
        .Select(match => match.Groups[1].Value)];

    private const string SampleLead = "Should this read the file before the lock?";

    private const string SampleLine = "var text = File.ReadAllText(path);";

    private const string SampleTail = "Otherwise the lock covers nothing.";

    /// <summary>The rule's full thread, as a whole fence.</summary>
    private static string FullThread => $"```annotation\n{RuleExamples[1]}\n```";

    /// <summary>The rule's three-line note with a body quoting a code sample,
    /// opened with four backticks so the sample's three can sit inside it.</summary>
    private static string NoteWithCodeSample(string sampleLanguage = "csharp", string sampleLine = SampleLine)
    {
        var header = string.Join('\n', RuleExamples[0].Split('\n').Where(line => !line.StartsWith("body:", StringComparison.Ordinal)));
        return $"````annotation\n{header}\nbody: |\n  {SampleLead}\n  ```{sampleLanguage}\n  {sampleLine}\n  ```\n  {SampleTail}\n````";
    }

    [Fact]
    public void The_arc42_reader_keeps_a_note_whole_and_its_view_draws_it_as_a_review_note()
    {
        var document = DevbookMarkdownParser.Parse(
            ".arc42/04-solution-strategy.md",
            $"# Solution Strategy\n\nThe outline is one indexed range read.\n\n{NoteWithCodeSample()}\n\nThe next passage.\n");

        var code = Assert.Single(document.Blocks.OfType<DevbookCodeBlock>());
        Assert.Equal("annotation", code.Language);
        Assert.Contains(SampleTail, code.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            document.Blocks.OfType<DevbookParagraphBlock>(),
            paragraph => MarkdownRender.PlainText(paragraph.Content).Contains(SampleTail, StringComparison.Ordinal));

        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var view = context.Render<DevbookMarkdownView>(parameters => parameters.Add(v => v.Blocks, document.ContentBlocks));

        var note = view.Find("[data-testid='devbook-annotation-note']");
        Assert.Contains("devbook-note--attached", note.ClassList);
        Assert.Equal("The outline is one indexed range read.", note.PreviousElementSibling!.TextContent);

        // The one listing is the sample, drawn inside the note — the fence's YAML
        // never reaches the page as a devbook-code block.
        Assert.Empty(view.FindAll("pre.devbook-code"));
        Assert.Equal(SampleLine, Assert.Single(view.FindAll("pre")).TextContent);
    }

    [Fact]
    public void The_design_reader_keeps_a_note_whole_and_its_section_draws_it_as_a_review_note()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "color-scheme.md");
        File.WriteAllText(path, $"""
# Color scheme

## Semantic colors

Every surface takes one of these.

{FullThread}

{NoteWithCodeSample()}

The next passage.
""");

        var section = Assert.Single(DocumentDevbookParser.ParseFile(_root, path).Sections);

        Assert.Equal(2, section.Blocks.OfType<DocumentDevbookCode>().Count(code => code.Language == "annotation"));
        Assert.DoesNotContain(
            section.Blocks.OfType<DocumentDevbookParagraph>(),
            paragraph => MarkdownRender.PlainText(paragraph.Content).Contains(SampleTail, StringComparison.Ordinal));

        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var view = context.Render<DevbookBlocks>(parameters => parameters.Add(v => v.Blocks, section.Blocks));

        // Consecutive fences all attach to the same passage: two notes, in order.
        var notes = view.FindAll("[data-testid='devbook-annotation-note']");
        Assert.Equal(2, notes.Count);
        Assert.Contains("devbook-note--resolved", notes[0].ClassList);
        Assert.Contains("devbook-note--open", notes[1].ClassList);
        Assert.Empty(view.FindAll("pre.devbook-code"));
    }

    [Fact]
    public void The_domain_reader_leaves_notes_out_of_excerpts_summaries_links_and_diagrams()
    {
        WriteContextMap($"""
# Context Map: Test

```meta
status: draft
```

> Test context map.

{NoteWithCodeSample("mermaid", "flowchart LR")}

## Context map

The map shows who talks to whom.

````annotation
kind: question
author: jobsc
date: 2026-09-02
body: |
  > Is .domain/other/domain.md still upstream?
  ```mermaid
  flowchart LR
      Sample[Only a sample]
  ```
````

```mermaid
flowchart LR
    Inbox[Inbox]
```
""");

        var view = LoadDomain();

        Assert.Equal("Test context map.", view.ContextMap.Summary);

        // One diagram: the chapter's. The two quoted inside the notes are samples.
        var diagram = Assert.Single(view.ContextMap.Diagrams);
        Assert.Contains("Inbox[Inbox]", diagram.Source, StringComparison.Ordinal);

        var section = Assert.Single(view.ContextMap.Sections);
        Assert.Equal("The map shows who talks to whom.", section.Excerpt);
        Assert.DoesNotContain(".domain/other/domain.md", section.Links);
        Assert.DoesNotContain(view.ContextMap.Links, link => link.Contains("other", StringComparison.Ordinal));
    }

    [Fact]
    public void The_technology_reader_steps_over_a_note_and_draws_no_diagram_from_it()
    {
        var document = TechnologyMarkdownParser.Parse(
            ".tech/dotnet.md",
            $"# .NET\n\n{FullThread}\n\nThe runtime every host is built on.\n\n## Hosting\n\n{NoteWithCodeSample()}\n\nWhere it runs.\n");

        Assert.Empty(document.Diagrams);
        Assert.Equal("The runtime every host is built on.", document.Summary);

        var chapter = Assert.Single(document.Chapters);
        Assert.Equal("Where it runs.", chapter.Summary);
    }

    [Fact]
    public async Task Ask_AI_sends_the_chapter_without_its_notes_or_its_review_triad()
    {
        var chapter = $"""
# Context and scope

```meta
status: accepted
review: requested
reviewer: jobsc
review-at: 2026-09-02
related: [".arc42/01-introduction-and-goals.md"]
```

The system in its surroundings.

{FullThread}

{NoteWithCodeSample()}

The sync service beside it.

```csharp
var kept = true;
```
""";
        var (folders, alias) = Folder(chapter);
        var open = new DevbookOpenChapter();
        open.Set(alias, "arc42", "03-context-and-scope.md");

        var content = await new DevbookAiContentSource(new NoSearch(), folders, open)
            .ComposeAsync(new AiContentRequest("sync", 6000), TestContext.Current.CancellationToken);

        Assert.DoesNotContain("annotation", content.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("one indexed range read", content.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(SampleLead, content.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(SampleTail, content.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("review", content.Body, StringComparison.Ordinal);

        // Everything else is the chapter and stays: the status, the relations,
        // the prose on both sides of the notes, and a listing that is not a note.
        Assert.Contains("status: accepted", content.Body, StringComparison.Ordinal);
        Assert.Contains("related: [\".arc42/01-introduction-and-goals.md\"]", content.Body, StringComparison.Ordinal);
        Assert.Contains("The system in its surroundings.", content.Body, StringComparison.Ordinal);
        Assert.Contains("The sync service beside it.", content.Body, StringComparison.Ordinal);
        Assert.Contains("```csharp\nvar kept = true;\n```", content.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_context_cut_leaves_a_chapter_without_notes_as_it_was()
    {
        const string chapter = "# Decisions\n\n```meta\nstatus: accepted\n```\n\nWhy the index is a generated database.\n";

        Assert.Equal(chapter, DevbookAiContentSource.ForContext(chapter));
    }

    [Fact]
    public void The_context_cut_takes_a_list_valued_review_field_with_its_continuation_lines()
    {
        const string chapter = "```meta\nstatus: active\nreviewer:\n  - jobsc\n  - claude\nrelated: [a]\n```\n";

        Assert.Equal("```meta\nstatus: active\nrelated: [a]\n```\n", DevbookAiContentSource.ForContext(chapter));
    }

    private void WriteContextMap(string markdown)
    {
        Directory.CreateDirectory(Path.Combine(_root, ".domain"));
        File.WriteAllText(Path.Combine(_root, ".domain", "context-map.md"), markdown);
    }

    private DomainDevbookView LoadDomain()
    {
        var settings = new GitHubSettingsStore(Path.Combine(_root, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        settings.SetRepositories(repositories);
        settings.SetCloneDirectory("backlog", _root);

        using var store = new DomainDevbookStore(new DevbookFolderSource(settings));
        var view = store.LoadAsync("backlog", TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        Assert.Null(view.Error);
        return view;
    }

    private (IDevbookFolderSource Folders, string Alias) Folder(string chapter)
    {
        Directory.CreateDirectory(Path.Combine(_root, ".arc42"));
        File.WriteAllText(Path.Combine(_root, ".arc42", "03-context-and-scope.md"), chapter);

        var settings = new WorkspaceSettingsStore(Path.Combine(_root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(_root, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("backlog = JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = _root,
            DevbookFolders = DevbookFolderSetting.Defaults()
        };
        Assert.Null(gitHub.SetRepositories([repository]));

        return (new DevbookFolderSource(gitHub, settings), repository.Alias);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>An index with nothing in it, so the body is the open chapter
    /// alone.</summary>
    private sealed class NoSearch : IDevbookSearch
    {
        public DevbookSearchAnswer Search(string? query, string? repositoryAlias = null, string? scope = null, int limit = 30) =>
            DevbookSearchAnswer.For([]);
    }
}
