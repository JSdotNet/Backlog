using System.ComponentModel;

using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The devbook a session reads: which knowledge folders a repository has, one
/// chapter out of one of them, and the private reading notes against it.
/// <para>
/// One class because they are one switchable group
/// (<see cref="BacklogMcpTools.Devbook"/>), and because the three are one
/// sequence: find the areas, open a chapter, see what was said about it.
/// </para>
/// <para>
/// One write, and one only: <see cref="ResolveAnnotationFor"/> over
/// <see cref="IDevbookAnnotationStore.SetResolved"/>, which is how a session says
/// a note has been answered. <c>Add</c>, <c>Edit</c>, <c>Delete</c> and
/// <c>Apply</c> are never called from this assembly, and no chapter file is
/// opened for anything but reading — local ADR 0012 §6: the MCP server never
/// writes a fence. The answer itself is a devbook <c>annotation</c> fence the
/// session writes in its own checkout, with the plugin's own tool, and this
/// marks the inbox afterwards.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class DevbookTools(
    IDevbookFolderSource folders,
    IDevbookAnnotationStore annotations,
    IRepositoryDirectory repositories)
{
    internal const string ListKnowledgeContexts = "list_knowledge_contexts";
    internal const string ReadKnowledgeChapter = "read_knowledge_chapter";
    internal const string ListAnnotations = "list_annotations";
    internal const string ResolveAnnotation = "resolve_annotation";

    // OpenWorld, unlike every tool outside this class. A repository whose devbook
    // is read from a branch snapshot (local ADR 0008) has no clone to resolve
    // against, and resolving one asks DevbookSnapshotAutoFetch to make sure the
    // branch has been fetched — so this answer depends on GitHub and can set a
    // download going. It never waits on one, which is why it is still Idempotent
    // and ReadOnly; what it is not is closed.
    [McpServerTool(Name = ListKnowledgeContexts, Title = "List a repository's knowledge folders", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("The knowledge folders configured for one repository — architecture, domain, technology, design, AI adoption, instructions — and whether each one is readable right now. Read-only.")]
    public KnowledgeContextsPayload ListKnowledgeContextsFor(
        [Description("The repository in owner/name form, e.g. JSdotNet/Backlog.")]
        string repository)
    {
        var scope = RepositoryScope.Resolve(repositories, repository).ValueOrThrow();

        // Resolve rather than Prepare: this is a catalog, and a call that fetched
        // a branch snapshot per folder would put GitHub in front of the question
        // "what is configured here". An unfetched branch answers unavailable with
        // Pending set, which is news rather than an error.
        var contexts = folders.Folders(scope.Alias)
            .Where(folder => folder.Enabled)
            .Select(folder => Projections.KnowledgeContext(folder, folders.Resolve(folder.Key, scope.Alias)))
            .ToList();

        return new KnowledgeContextsPayload(scope.Id, scope.Alias, contexts);
    }

    // OpenWorld for the reason the tool below is not: PrepareContentAsync fetches
    // the named file from GitHub when the folder is a branch snapshot, and writes
    // it into the snapshot cache on the way past. ReadOnly still holds — that
    // cache is this product's copy of somebody else's commit, not a change to
    // anything a caller reasons about, and nothing in this assembly writes a
    // chapter, an entry or a note — but "does not talk to the outside world" was
    // never true of it, and a client acts on these hints.
    [McpServerTool(Name = ReadKnowledgeChapter, Title = "Read a knowledge chapter", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description(
        "One knowledge chapter, as the author wrote it — the file's own text, never reformatted — plus an index of "
        + "its blocks. Ordinarily the file without the lines of its `meta` record or any `annotation` fence, and with "
        + "no private notes. With review set, the file unmodified, with the reader's private notes spliced in after "
        + "the block each one is anchored to, between `<!-- backlog:private-note ... -->` comment markers. Block "
        + "indices are the same in both modes. Read-only.")]
    public async Task<ChapterPayload> ReadKnowledgeChapterAsync(
        [Description("The repository in owner/name form, e.g. JSdotNet/Backlog.")]
        string repository,
        [Description("The chapter, repository-relative and with its area folder on the front, e.g. .arc42/adr/0012-backlog-is-an-mcp-server-inside-the-desktop-app.md.")]
        string chapterPath,
        [Description("Read it as a reviewer: keep the meta and annotation fences, and interleave the reader's private notes. Defaults to false.")]
        bool review = false,
        CancellationToken cancellationToken = default)
    {
        var scope = RepositoryScope.Resolve(repositories, repository).ValueOrThrow();

        var normalized = ChapterPaths.Normalize(chapterPath);

        if (normalized.Length == 0)
        {
            throw RepositoryScope.Failure(Error.Validation(
                "chapter.required",
                "A chapter path is required, repository-relative — e.g. .arc42/05-building-block-view.md."));
        }

        // Every configured folder and not only the enabled ones, so a path under
        // a switched-off area is refused as switched off rather than falling
        // through to the folder whose root is the repository itself — which would
        // find the file and answer for an area somebody turned off.
        var configured = folders.Folders(scope.Alias);

        if (ChapterPaths.Split(configured, normalized) is not { } split)
        {
            throw RepositoryScope.Failure(Error.NotFound(
                "chapter.no_context",
                $"No knowledge folder configured for '{scope.Id}' claims '{normalized}'. "
                + $"Call {ListKnowledgeContexts} to see which are configured."));
        }

        var (folder, relative) = split;

        if (!folder.Enabled)
        {
            throw RepositoryScope.Failure(Error.NotFound(
                "chapter.context_disabled",
                $"The '{folder.DisplayName}' folder is switched off for '{scope.Id}'."));
        }

        // Before anything is fetched or opened, because this is the check that
        // keeps the tool from being a file read over the whole clone: the
        // Instructions folder's root is the repository itself, so containment
        // alone admits `.git/config`, a `.env` and every build output beside
        // them. See ChapterPaths.IsReadableChapter.
        if (!ChapterPaths.IsReadableChapter(folder, relative))
        {
            throw RepositoryScope.Failure(Error.Validation(
                "chapter.not_a_chapter",
                $"'{normalized}' is not a knowledge chapter. A chapter is a Markdown file inside a configured "
                + "knowledge folder — or, for the Instructions folder whose area is the repository itself, one of "
                + "the instruction documents the product recognises: CLAUDE.md or AGENTS.md at any depth, Markdown "
                + "under .claude/, .github/copilot-instructions.md, .github/instructions/*.instructions.md, or a "
                + "SKILL.md under .github/skills/ or .agents/skills/. "
                + $"Call {ListKnowledgeContexts} to see which folders are configured."));
        }

        // Prepared and not resolved, and named down to the one file: for a branch
        // snapshot this is the moment that chapter is fetched, and asking for the
        // whole area would download a folder to read a page of it.
        var location = await folders
            .PrepareContentAsync(folder.Key, scope.Alias, [relative], cancellationToken)
            .ConfigureAwait(false);

        if (!location.Available || location.FullPath is null)
        {
            throw RepositoryScope.Failure(Error.NotFound(
                "chapter.unavailable",
                location.Message ?? $"The '{folder.DisplayName}' folder of '{scope.Id}' is not available."));
        }

        var fullPath = ChapterPaths.ResolveWithin(location.FullPath, relative);

        if (fullPath is null || !File.Exists(fullPath))
        {
            throw RepositoryScope.Failure(Error.NotFound(
                "chapter.not_found",
                $"No chapter at '{normalized}' in '{scope.Id}'."));
        }

        string markdown;

        try
        {
            markdown = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A chapter this can list and cannot read is the habit every panel in
            // the product already has: say so rather than answer with an empty
            // chapter, which a session would read as a chapter with nothing in it.
            throw RepositoryScope.Failure(Error.Unexpected(
                "chapter.unreadable",
                $"'{normalized}' could not be read: {ex.Message}"));
        }

        var notes = review
            ? ChapterReading.Notes(annotations, scope.Alias, ChapterReading.Spellings(normalized, relative))
            : [];

        return ChapterReading.Build(scope.Id, scope.Alias, folder.Key, normalized, markdown, review, notes);
    }

    // Closed, and the one tool here that is: the notes are Backlog's own store and
    // the folder settings are read without resolving anything, so nothing on this
    // path can reach GitHub.
    [McpServerTool(Name = ListAnnotations, Title = "List the private notes on a chapter", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "The reader's own private notes on one knowledge chapter — Backlog's remarks, not the devbook `annotation` "
        + "fences in the file. Drafts nobody has typed into and deleted notes are left out. Read-only.")]
    public AnnotationsPayload ListAnnotationsOn(
        [Description("The repository in owner/name form, e.g. JSdotNet/Backlog.")]
        string repository,
        [Description("The chapter, repository-relative and with its area folder on the front.")]
        string chapterPath)
    {
        var scope = RepositoryScope.Resolve(repositories, repository).ValueOrThrow();

        var normalized = ChapterPaths.Normalize(chapterPath);

        if (normalized.Length == 0)
        {
            throw RepositoryScope.Failure(Error.Validation(
                "chapter.required",
                "A chapter path is required, repository-relative — e.g. .arc42/05-building-block-view.md."));
        }

        // The area-relative spelling too, where a configured folder claims the
        // path — see ChapterReading.Spellings for why there are two. No file is
        // touched here and the folder's own switch is not read: an unavailable or
        // switched-off area still has notes, and a note about a chapter somebody
        // has stopped showing is still what somebody said.
        var relative = ChapterPaths.Split(folders.Folders(scope.Alias), normalized)?.RelativePath;

        var notes = ChapterReading
            .Notes(annotations, scope.Alias, ChapterReading.Spellings(normalized, relative))
            .Select(Projections.Note)
            .ToList();

        return new AnnotationsPayload(scope.Id, scope.Alias, normalized, notes.Count, notes);
    }

    // Not read-only, and the only tool here that is not. Idempotent because
    // resolving an already-resolved note is the state it is already in, and not
    // destructive because nothing is lost: a resolved remark stays visible and
    // quiet, and the person can reopen it in the app. Closed, like
    // ListAnnotations and for the same reason — the notes are Backlog's own
    // store and nothing on this path can reach GitHub.
    [McpServerTool(
        Name = ResolveAnnotation,
        Title = "Mark a private note answered",
        ReadOnly = false,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description(
        "Marks one of the reader's own private notes on a knowledge chapter as dealt with. Write the answer into the "
        + "chapter first, as a devbook `annotation` fence, with the devbook tooling in your own checkout — then call "
        + "this. That order matters: a fence nobody resolved can still be found, while a note resolved without its "
        + "answer written has lost the only record of where the answer is. Resolving a note that is already resolved "
        + "changes nothing.")]
    public ChapterNotePayload ResolveAnnotationFor(
        [Description("The repository in owner/name form, e.g. JSdotNet/Backlog.")]
        string repository,
        [Description("The note's id, exactly as list_annotations reports it.")]
        Guid note)
    {
        var scope = RepositoryScope.Resolve(repositories, repository).ValueOrThrow();

        if (annotations.Find(note) is not { IsLive: true } existing)
        {
            throw RepositoryScope.Failure(Error.NotFound(
                "annotation.notFound",
                $"No live note with id {note}. Ask list_annotations for the chapter's notes and use an id from there."));
        }

        // A draft is a remark nobody has typed into yet — it is on the person's
        // screen, has never been replicated, and there is nothing to have
        // answered. Resolving one would quietly retire an empty note.
        if (existing.IsDraft)
        {
            throw RepositoryScope.Failure(Error.Validation(
                "annotation.draft",
                $"Note {note} is an empty draft the reader has not written yet, so there is nothing to resolve."));
        }

        // The repository is named on the call and checked against the note rather
        // than taken from it. A session works in one checkout, and resolving
        // another repository's note from it is a mistake worth refusing rather
        // than a shortcut worth honouring.
        if (!string.Equals(existing.RepositoryAlias, scope.Alias, StringComparison.OrdinalIgnoreCase))
        {
            throw RepositoryScope.Failure(Error.Validation(
                "annotation.otherRepository",
                $"Note {note} belongs to {existing.RepositoryAlias}, not {scope.Alias}."));
        }

        if (!existing.Resolved)
        {
            annotations.SetResolved(note, resolved: true);
        }

        // Read back rather than returned from the record above, so the caller is
        // told what the store now holds — the stamp the write moved included.
        return Projections.Note(annotations.Find(note) ?? existing with { Resolved = true });
    }
}
