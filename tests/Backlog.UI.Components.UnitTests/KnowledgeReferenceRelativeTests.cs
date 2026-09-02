namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Reading a reference that was written the way an author actually writes one.
///
/// <para>The knowledge folders almost never spell a link out from the repository
/// root. A chapter links to its sibling as <c>domain.md#roadmap-item-gathering</c>
/// and to another context as <c>../tasks/domain.md#task</c>, because
/// that is the form that works in every markdown viewer the folders are also read
/// in. The parser that only accepted the rooted form therefore accepted almost
/// nothing that had been written, which is why the pane sent a hundred-odd links
/// to the reader's browser instead of to the chapter beside them.</para>
///
/// <para>What is worth pinning here is both halves: the forms that must resolve,
/// and the forms that must keep resolving to nothing. A link is prose too, and a
/// resolver that answered "somewhere" for every string would put a destination on
/// an image, on a path that climbs out of the repository, and on a template
/// placeholder.</para>
/// </summary>
public sealed class KnowledgeReferenceRelativeTests
{
    [Fact]
    public void A_link_to_another_context_resolves_against_the_document_it_was_written_in()
    {
        // The single most common form in the folders: ~110 of them, and not one
        // resolved before.
        var reference = KnowledgeReference.ParseKnowledgePath(
            "../tasks/domain.md#task",
            ".domain/roadmap/features.md");

        Assert.NotNull(reference);
        Assert.Equal(".domain/tasks/domain.md", reference.Path);
        Assert.Equal("task", reference.Slug);
        Assert.Equal(KnowledgeFolder.Domain, reference.Folder);
    }

    [Fact]
    public void A_sibling_link_resolves_inside_the_documents_own_folder()
    {
        var reference = KnowledgeReference.ParseKnowledgePath(
            "domain.md#roadmap-item-gathering",
            ".domain/roadmap/features.md");

        Assert.NotNull(reference);
        Assert.Equal(".domain/roadmap/domain.md", reference.Path);
        Assert.Equal("roadmap-item-gathering", reference.Slug);
    }

    [Fact]
    public void A_link_into_a_subfolder_resolves_below_the_document()
    {
        var reference = KnowledgeReference.ParseKnowledgePath(
            "adr/0001-desktop-stack-maui-blazor-hybrid.md",
            ".arc42/09-architecture-decisions.md");

        Assert.NotNull(reference);
        Assert.Equal(".arc42/adr/0001-desktop-stack-maui-blazor-hybrid.md", reference.Path);
        Assert.Null(reference.Slug);
    }

    [Fact]
    public void A_link_that_climbs_out_of_one_folder_and_into_another_crosses_areas()
    {
        // `.backlog/domain-backlog.md` writes it this way: up to the repository
        // root and back down into a different knowledge folder.
        var reference = KnowledgeReference.ParseKnowledgePath(
            "../.domain/roadmap/domain.md#roadmap-plan",
            ".backlog/domain-backlog.md");

        Assert.NotNull(reference);
        Assert.Equal(".domain/roadmap/domain.md", reference.Path);
        Assert.Equal("roadmap-plan", reference.Slug);
    }

    [Fact]
    public void A_link_already_written_from_the_repository_root_still_resolves_to_itself()
    {
        // The rooted form is what a `meta` fence holds and what the code spans in
        // prose use, and it has to keep meaning exactly what it meant before a
        // base document was ever handed in.
        var reference = KnowledgeReference.ParseKnowledgePath(".tech/shared.md", ".design/component-libraries.md");

        Assert.NotNull(reference);
        Assert.Equal(".tech/shared.md", reference.Path);
    }

    [Fact]
    public void An_anchor_on_its_own_addresses_the_document_it_was_written_in()
    {
        // `#surface-and-border-deviation` names a heading of this chapter. Nothing
        // scrolls to it — see KnowledgeChapterLink.Anchor — but a reader who
        // clicks it must not land in a browser.
        var reference = KnowledgeReference.ParseKnowledgePath(
            "#surface-and-border-deviation",
            ".design/component-libraries.md");

        Assert.NotNull(reference);
        Assert.Equal(".design/component-libraries.md", reference.Path);
        Assert.Equal("surface-and-border-deviation", reference.Slug);
    }

    [Fact]
    public void A_dot_folder_that_is_not_a_section_is_still_read_from_the_repository_root()
    {
        // The reported trap. `.github/…` names no section, so the honest answer is
        // nothing — but walked against the chapter's own folder it became
        // `.design/.github/instructions/naming.instructions.md`, whose first segment
        // *is* a section. So it came back as a followable reference to a file that
        // has never existed, and the reader got a control that looks like every
        // other one and quietly does nothing.
        Assert.Null(KnowledgeReference.ParseKnowledgePath(
            ".github/instructions/naming.instructions.md",
            ".design/component-libraries.md"));
    }

    [Fact]
    public void A_dot_folder_reads_the_same_written_as_a_link_or_as_a_code_span()
    {
        // The two forms were already disagreeing about the same eleven characters:
        // the code span parsed rooted and correctly stayed inert, and the link
        // resolved to a chapter of whatever folder it was written in. One target,
        // one answer.
        const string target = ".claude/agents/orchestrator.md";

        Assert.Null(KnowledgeReference.ParseKnowledgePath(target));
        Assert.Null(KnowledgeReference.ParseKnowledgePath(target, ".tech/shared.md"));
    }

    [Fact]
    public void A_rooted_link_into_another_section_is_untouched_by_the_dot_rule()
    {
        // The rule has to leave the form it was written to protect exactly as it
        // was: a dot folder that *is* a section still means that section, from
        // wherever it was written.
        var reference = KnowledgeReference.ParseKnowledgePath(".tech/shared.md", ".domain/roadmap/features.md");

        Assert.NotNull(reference);
        Assert.Equal(".tech/shared.md", reference.Path);
        Assert.Equal(KnowledgeFolder.Tech, reference.Folder);
    }

    [Fact]
    public void A_dot_that_is_the_folder_the_author_is_in_stays_relative()
    {
        // `./` is not a folder name; it is the folder this file is in, which is
        // where a relative walk starts anyway. The dot rule is about a dot that
        // opens a name, and this one opens nothing.
        var reference = KnowledgeReference.ParseKnowledgePath("./domain.md#roadmap-item", ".domain/roadmap/features.md");

        Assert.NotNull(reference);
        Assert.Equal(".domain/roadmap/domain.md", reference.Path);
        Assert.Equal("roadmap-item", reference.Slug);
    }

    [Fact]
    public void A_dot_folder_behind_a_here_marker_is_rooted_too()
    {
        // `./.github/x.md` is the same target as `.github/x.md` — "here" is where a
        // relative path already starts — so it has to be the same answer. Asking
        // the question before the marker is dropped would let this one spelling
        // walk into the chapter's folder and become followable again.
        Assert.Null(KnowledgeReference.ParseKnowledgePath("./.github/copilot-instructions.md", ".design/accessibility.md"));
    }

    [Fact]
    public void Both_slash_directions_read_as_separators()
    {
        // A path written with backslashes is the same path. KnowledgeFolders
        // already tolerates it, and refusing it here would drop a folder that is
        // plainly there.
        var reference = KnowledgeReference.ParseKnowledgePath(
            @"..\tasks\domain.md#priority",
            @".domain\roadmap\features.md");

        Assert.NotNull(reference);
        Assert.Equal(".domain/tasks/domain.md", reference.Path);
        Assert.Equal("priority", reference.Slug);
    }

    [Theory]
    // Not a chapter: a section selects markdown files, and an image is not one.
    [InlineData("assets/task-inline-markdown-editing.png", ".domain/tasks/features.md")]
    // Out of the repository entirely. Two `..` from a folder one deep is one too
    // many, and the answer is nothing rather than a path that starts climbing.
    [InlineData("../../../etc/passwd", ".domain/roadmap/features.md")]
    [InlineData("../../secrets.md", ".domain/features.md")]
    // Lands somewhere real but outside the knowledge folders: the sections are
    // the only thing this can address, and the repository's own README is not
    // one of them. (`../README.md` from the same document is a different answer
    // and a correct one — it is `.domain/README.md`, still inside the section.)
    [InlineData("../../README.md", ".domain/roadmap/features.md")]
    [InlineData("../src/Core/Backlog.UI.Components/README.md", ".domain/features.md")]
    // A template placeholder, left in a document waiting to be filled.
    [InlineData("$url", ".domain/roadmap/features.md")]
    // Two references written as one span is a link to neither.
    [InlineData("domain.md and model.md", ".domain/roadmap/features.md")]
    // A URL scheme is somebody else's destination; it is never a chapter.
    [InlineData("https://example.test/page", ".domain/roadmap/features.md")]
    [InlineData("mailto:someone@example.test", ".domain/roadmap/features.md")]
    [InlineData("javascript:alert(1)", ".domain/roadmap/features.md")]
    // A folder is not a chapter.
    [InlineData("../backlog", ".domain/roadmap/features.md")]
    // A dot folder that names no section is rooted at the repository, so it lands
    // outside the sections rather than inside the one it was written in.
    [InlineData(".github/instructions/context-loading.instructions.md", ".design/component-libraries.md")]
    [InlineData(".agents/skills/orch-bug.md", ".domain/roadmap/features.md")]
    [InlineData(".vscode/settings.json", ".arc42/09-architecture-decisions.md")]
    public void Everything_that_is_not_a_chapter_of_a_knowledge_folder_resolves_to_nothing(string target, string document)
    {
        Assert.Null(KnowledgeReference.ParseKnowledgePath(target, document));
    }

    [Theory]
    [InlineData("../tasks/domain.md#task")]
    [InlineData("domain.md")]
    [InlineData("#a-heading")]
    public void Without_a_document_to_resolve_against_a_relative_link_stays_unresolved(string target)
    {
        // A relative link means nothing on its own — the same three characters
        // address a different chapter from every folder in the repository — so a
        // caller that cannot say which document it is rendering gets nothing
        // rather than a guess.
        Assert.Null(KnowledgeReference.ParseKnowledgePath(target, null));
    }

    [Fact]
    public void A_base_document_outside_the_knowledge_folders_still_resolves_a_rooted_link()
    {
        // `.github/instructions/*.md` is read in the pane too, and it links into
        // the design and domain folders from the repository root. Its own folder
        // is not a section, which decides nothing about where its links point.
        var reference = KnowledgeReference.ParseKnowledgePath(
            ".design/accessibility.md",
            ".github/instructions/ui-components.instructions.md");

        Assert.NotNull(reference);
        Assert.Equal(".design/accessibility.md", reference.Path);
    }

    [Fact]
    public void A_sibling_link_from_outside_the_knowledge_folders_resolves_to_nothing()
    {
        // It resolves to a real file — `.github/instructions/context-loading…` —
        // and that file is not a chapter of any section, so there is nowhere for
        // the pane to send the reader.
        Assert.Null(KnowledgeReference.ParseKnowledgePath(
            "context-loading.instructions.md",
            ".github/instructions/ui-components.instructions.md"));
    }

    [Fact]
    public void The_document_the_link_came_from_is_read_leniently()
    {
        // A leading `./` or `/` on the base path is the same document, and a host
        // that carries one should not silently lose every link in the file.
        var reference = KnowledgeReference.ParseKnowledgePath("../tasks/domain.md", "/.domain/roadmap/features.md");

        Assert.NotNull(reference);
        Assert.Equal(".domain/tasks/domain.md", reference.Path);
    }

    [Fact]
    public void A_slug_keeps_everything_after_the_first_hash()
    {
        // The same rule the rooted form already keeps: a heading slug is full of
        // hyphens and may hold a second hash, and splitting anywhere but the
        // first one truncates the chapter to its file.
        var reference = KnowledgeReference.ParseKnowledgePath(
            "../roadmap/features.md#gathering-work-under-an-item-and-totalling-its-effort",
            ".domain/tasks/features.md");

        Assert.NotNull(reference);
        Assert.Equal(".domain/roadmap/features.md", reference.Path);
        Assert.Equal("gathering-work-under-an-item-and-totalling-its-effort", reference.Slug);
    }

    [Fact]
    public void The_reference_keeps_the_resolved_path_as_its_raw_form()
    {
        // Raw is what the title shows and what a host resolves to a route, and
        // both of those want the address the reader is being sent to rather than
        // the shorthand the author typed. The author's own words are still what
        // is printed — the link's text carries those.
        var reference = KnowledgeReference.ParseKnowledgePath("../tasks/domain.md#priority", ".domain/roadmap/features.md");

        Assert.NotNull(reference);
        Assert.Equal(".domain/tasks/domain.md#priority", reference.Raw);
    }
}
