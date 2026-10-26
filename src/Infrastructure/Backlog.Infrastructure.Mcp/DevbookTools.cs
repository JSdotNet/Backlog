using System.ComponentModel;

using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The devbook a session reads: which devbook folders a repository has, one
/// chapter out of one of them, and the private reading notes against it — and
/// the same for the Brain, the person's own notes outside every repository.
/// <para>
/// One class because they are one switchable group
/// (<see cref="BacklogMcpTools.Devbook"/>), and because the three are one
/// sequence: find the areas, open a chapter, see what was said about it.
/// </para>
/// <para>
/// <b>The Brain is a scope, not a second set of tools</b>
/// (<c>.devbook/domain/devbook/features.md#mcp-brain-scope</c>, local ADR 0027
/// §5). Every tool that takes a <c>repository</c> also takes
/// <see cref="BrainMcpScope.Value"/>, matched before the directory is asked, and
/// answers for the Brain: its one folder from <see cref="IBrainFolderSource"/>,
/// a note by its path from the Brain's root, and the remarks the store keeps
/// under <see cref="BrainScope.Value"/>. The two Brain ports are optional, so a
/// host that composes no Brain still constructs this class and answers the
/// Brain unavailable in words rather than failing every devbook tool.
/// </para>
/// <para>
/// Two writes, and two only. <see cref="ResolveAnnotationFor"/> over
/// <see cref="IDevbookAnnotationStore.SetResolved"/> is how a session says a note
/// has been answered. <c>Add</c>, <c>Edit</c>, <c>Delete</c> and <c>Apply</c> are
/// never called from this assembly, and no chapter file is opened for anything
/// but reading — local ADR 0012 §6: the MCP server never writes a fence. The
/// answer itself is a devbook <c>annotation</c> fence the session writes in its
/// own checkout, with the plugin's own tool, and this marks the inbox afterwards.
/// <see cref="CaptureNoteAsync"/> is the other: a <em>new</em> note in the
/// Brain's inbox area, through <see cref="IBrainNoteWriter"/> — the writer the
/// pane's Capture and Inbox triage's To Brain go through — which creates a file
/// and never opens an existing one, so no chapter and no fence is touched by it
/// either.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class DevbookTools(
    IDevbookFolderSource folders,
    IDevbookAnnotationStore annotations,
    IRepositoryDirectory repositories,
    IBrainFolderSource? brain = null,
    IBrainNoteWriter? brainNotes = null)
{
    internal const string ListKnowledgeContexts = "list_knowledge_contexts";
    internal const string ReadKnowledgeChapter = "read_knowledge_chapter";
    internal const string ListAnnotations = "list_annotations";
    internal const string ResolveAnnotation = "resolve_annotation";
    internal const string CaptureNote = "capture_note";

    /// <summary>The code <c>resolve_annotation</c>'s refusal for an archived
    /// repository carries (local ADR 0029 §4).</summary>
    internal const string ArchivedCode = "repository.archived";

    /// <summary>The <c>repository</c> parameter every scoped tool here takes,
    /// described once so the four cannot say four things.</summary>
    private const string RepositoryDescription =
        "The repository in owner/name form, e.g. JSdotNet/Backlog — or " + BrainMcpScope.Value
        + " for the Brain, the person's own notes outside every repository.";

    /// <summary>What a Brain answer says when this host composed no Brain.</summary>
    private const string NoBrainHere = "The Brain is not available in this host.";

    /// <summary>The Brain's folder as a catalog names it, for the answer given
    /// when no source is there to name it: the one definition the folder source
    /// answers too.</summary>
    private static DevbookFolderSetting BrainFolder => BrainConventions.Folder;

    // OpenWorld, unlike every tool outside this class. A repository whose devbook
    // is read from a branch snapshot (local ADR 0008) has no clone to resolve
    // against, and resolving one asks DevbookSnapshotAutoFetch to make sure the
    // branch has been fetched — so this answer depends on GitHub and can set a
    // download going. It never waits on one, which is why it is still Idempotent
    // and ReadOnly; what it is not is closed.
    [McpServerTool(Name = ListKnowledgeContexts, Title = "List a repository's devbook folders", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description(
        "The devbook folders configured for one repository — architecture, domain, technology, design, AI "
        + "adoption, instructions — and whether each one is readable right now. For " + BrainMcpScope.Value
        + ", the Brain's one folder. Read-only.")]
    public KnowledgeContextsPayload ListKnowledgeContextsFor(
        [Description(RepositoryDescription)]
        string repository)
    {
        if (BrainMcpScope.Is(repository)) return BrainContexts();

        var scope = RepositoryScope.Resolve(repositories, repository, acceptsBrain: true).ValueOrThrow();

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

    /// <summary>
    /// The Brain's one folder, resolved — or, where this host composed no Brain,
    /// that folder answered unavailable.
    /// <para>
    /// Listed even when it cannot be read, as a repository's switched-on folder
    /// is: "no Brain is set" is the answer a session passes on to the person,
    /// and an empty list would say nothing about why.
    /// </para>
    /// </summary>
    private KnowledgeContextsPayload BrainContexts()
    {
        IReadOnlyList<KnowledgeContextPayload> contexts = brain is null
            ?
            [
                Projections.KnowledgeContext(
                    BrainFolder,
                    DevbookFolderLocation.Unavailable(
                        BrainConventions.FolderKey, NoBrainHere, folder: BrainFolder, scopeLabel: BrainScope.Name))
            ]
            :
            [
                .. brain.Folders(BrainScope.Value)
                    .Where(folder => folder.Enabled)
                    .Select(folder => Projections.KnowledgeContext(folder, brain.Resolve(folder.Key, BrainScope.Value)))
            ];

        return new KnowledgeContextsPayload(BrainMcpScope.Value, BrainScope.Value, contexts);
    }

    // OpenWorld for the reason the tool below is not: PrepareContentAsync fetches
    // the named file from GitHub when the folder is a branch snapshot, and writes
    // it into the snapshot cache on the way past. ReadOnly still holds — that
    // cache is this product's copy of somebody else's commit, not a change to
    // anything a caller reasons about, and nothing in this assembly writes a
    // chapter, an entry or a note — but "does not talk to the outside world" was
    // never true of it, and a client acts on these hints.
    [McpServerTool(Name = ReadKnowledgeChapter, Title = "Read a devbook chapter", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description(
        "One devbook chapter, as the author wrote it — the file's own text, never reformatted — plus an index of "
        + "its blocks. Ordinarily the file without the lines of its `meta` record or any `annotation` fence, and with "
        + "no private notes. With review set, the file unmodified, with the reader's private notes spliced in after "
        + "the block each one is anchored to, between `<!-- backlog:private-note ... -->` comment markers. Block "
        + "indices are the same in both modes. `demos` lists the page's click demos by path — the `*.demo.html` "
        + "beside it whose name pairs it with the page, and every address a `demo` field names — never their HTML. "
        + "For " + BrainMcpScope.Value + ", one Brain note by its path from the Brain's root. Read-only.")]
    public async Task<ChapterPayload> ReadKnowledgeChapterAsync(
        [Description(RepositoryDescription)]
        string repository,
        [Description(
            "The chapter, repository-relative and with its area folder on the front, e.g. "
            + ".devbook/arc42/adr/0012-backlog-is-an-mcp-server-inside-the-desktop-app.md. For " + BrainMcpScope.Value
            + ", the note's path from the Brain's root, e.g. projects/plan.md.")]
        string chapterPath,
        [Description("Read it as a reviewer: keep the meta and annotation fences, and interleave the reader's private notes. Defaults to false.")]
        bool review = false,
        CancellationToken cancellationToken = default)
    {
        if (BrainMcpScope.Is(repository))
        {
            return await ReadBrainNoteAsync(chapterPath, review, cancellationToken).ConfigureAwait(false);
        }

        var scope = RepositoryScope.Resolve(repositories, repository, acceptsBrain: true).ValueOrThrow();

        var normalized = ChapterPaths.Normalize(chapterPath);

        if (normalized.Length == 0)
        {
            throw RepositoryScope.Failure(Error.Validation(
                "chapter.required",
                "A chapter path is required, repository-relative — e.g. .devbook/arc42/05-building-block-view.md."));
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
                $"No devbook folder configured for '{scope.Id}' claims '{normalized}'. "
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
                $"'{normalized}' is not a devbook chapter. A chapter is a Markdown file inside a configured "
                + "devbook folder — or, for the Instructions folder whose area is the repository itself, one of "
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

        // A clone can be listed and asked about any file in it. A branch snapshot
        // fetched this one chapter and nothing beside it, so a listing there would
        // report the demos as missing when what is missing is the fetch: it pairs
        // nothing by name and says nothing about whether a named demo is there.
        var clone = location.Source is DevbookSourceKind.LocalFolder ? location.RootPath : null;

        return await BuildChapterAsync(
            scope.Id, scope.Alias, folder.Key, normalized, fullPath, clone, review, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// One Brain note, read the way a chapter is.
    /// <para>
    /// Its own path through because the Brain's folder is its own root and its
    /// conventional path is empty — which <see cref="ChapterPaths.Split"/> and
    /// <see cref="ChapterPaths.IsReadableChapter"/> read as the Instructions
    /// folder, admitting <c>CLAUDE.md</c> and refusing every note. So a note is
    /// named by its path from the Brain's root, held to
    /// <see cref="ChapterPaths.IsReadableBrainNote"/>, and contained by
    /// <see cref="ChapterPaths.ResolveWithin"/> against that root. Resolved and
    /// not prepared: the Brain is always a local folder, with nothing to fetch.
    /// </para>
    /// </summary>
    private async Task<ChapterPayload> ReadBrainNoteAsync(string chapterPath, bool review, CancellationToken cancellationToken)
    {
        if (brain is null)
        {
            throw RepositoryScope.Failure(Error.NotFound("brain.unavailable", NoBrainHere));
        }

        var normalized = ChapterPaths.Normalize(chapterPath);

        if (normalized.Length == 0)
        {
            throw RepositoryScope.Failure(Error.Validation(
                "chapter.required",
                "A note path is required, from the Brain's root — e.g. projects/plan.md."));
        }

        // Before the folder is resolved, for the reason the repository read checks
        // first: this is what keeps the tool from being a file read over the whole
        // Brain folder, a notes app's own state included.
        if (!ChapterPaths.IsReadableBrainNote(normalized))
        {
            throw RepositoryScope.Failure(Error.Validation(
                "chapter.not_a_chapter",
                $"'{normalized}' is not a Brain note. A note is a Markdown file inside the Brain folder, named by its "
                + "path from the Brain's root, and never under a folder whose name starts with a dot."));
        }

        var location = brain.Resolve(BrainConventions.FolderKey, BrainScope.Value);

        if (!location.Available || location.FullPath is null)
        {
            throw RepositoryScope.Failure(Error.NotFound(
                "chapter.unavailable",
                location.Message ?? "The Brain is not available."));
        }

        var fullPath = ChapterPaths.ResolveWithin(location.FullPath, normalized);

        if (fullPath is null || !File.Exists(fullPath))
        {
            throw RepositoryScope.Failure(Error.NotFound(
                "chapter.not_found",
                $"No note at '{normalized}' in the Brain."));
        }

        // Containment is decided on the spelling, and a link makes the spelling
        // lie: a folder in the Brain that links to the user profile would pass
        // ResolveWithin and serve whatever is behind it as a note.
        if (ChapterPaths.PassesThroughLink(location.FullPath, fullPath))
        {
            throw RepositoryScope.Failure(Error.Validation(
                "chapter.not_a_chapter",
                $"'{normalized}' is reached through a link inside the Brain folder, and a link can lead out of it, "
                + "so it is not read."));
        }

        return await BuildChapterAsync(
            BrainMcpScope.Value, BrainScope.Value, BrainConventions.FolderKey, normalized, fullPath,
            location.FullPath, review, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A chapter file already found, read and answered: the step a repository
    /// chapter and a Brain note share once each has decided which file it is.
    /// </summary>
    /// <param name="clone">The local root a chapter's demos are looked for
    /// under, or null where the folder is a branch snapshot and nothing beside
    /// the chapter was fetched.</param>
    private async Task<ChapterPayload> BuildChapterAsync(
        string repositoryId,
        string repositoryAlias,
        string contextKey,
        string normalized,
        string fullPath,
        string? clone,
        bool review,
        CancellationToken cancellationToken)
    {
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
            ? ChapterReading.Notes(annotations, repositoryAlias, normalized)
            : [];

        var besidePage = clone is null ? [] : ChapterDemos.BesidePage(normalized, fullPath);

        return ChapterReading.Build(
            repositoryId, repositoryAlias, contextKey, normalized, markdown, review, notes,
            besidePage,
            demoPath => clone is null ? null : ChapterPaths.ResolveWithin(clone, demoPath) is { } file && File.Exists(file));
    }

    // Closed, and the one tool here that is: the notes are Backlog's own store and
    // the folder settings are read without resolving anything, so nothing on this
    // path can reach GitHub.
    [McpServerTool(Name = ListAnnotations, Title = "List the private notes on a chapter", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "The reader's own private notes on one devbook chapter — Backlog's remarks, not the devbook `annotation` "
        + "fences in the file. Drafts nobody has typed into and deleted notes are left out. For " + BrainMcpScope.Value
        + ", the notes on one Brain note. `archived` is true when the repository is archived: its devbook is "
        + "read-only, so write no `annotation` fence into it and do not resolve its notes. Read-only.")]
    public AnnotationsPayload ListAnnotationsOn(
        [Description(RepositoryDescription)]
        string repository,
        [Description(
            "The chapter, repository-relative and with its area folder on the front. For " + BrainMcpScope.Value
            + ", the note's path from the Brain's root.")]
        string chapterPath)
    {
        // The Brain needs no source here: its remarks are the store's, under the
        // empty alias, and a Brain nobody can open right now still has them.
        var scope = ScopeOf(repository);

        var normalized = ChapterPaths.Normalize(chapterPath);

        if (normalized.Length == 0)
        {
            throw RepositoryScope.Failure(Error.Validation(
                "chapter.required",
                "A chapter path is required, repository-relative — e.g. .devbook/arc42/05-building-block-view.md."));
        }

        // One question, and the store canonicalizes it. No file is touched here
        // and no folder is resolved: an unavailable or switched-off area still has
        // notes, and a note about a chapter somebody has stopped showing is still
        // what somebody said.
        var notes = ChapterReading
            .Notes(annotations, scope.Alias, normalized)
            .Select(Projections.Note)
            .ToList();

        return new AnnotationsPayload(scope.Id, scope.Alias, normalized, notes.Count, notes, scope.Archived);
    }

    // Not read-only, and one of the two tools here that are not. Idempotent because
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
        "Marks one of the reader's own private notes on a devbook chapter as dealt with. Write the answer into the "
        + "chapter first, as a devbook `annotation` fence, with the devbook tooling in your own checkout — then call "
        + "this. That order matters: a fence nobody resolved can still be found, while a note resolved without its "
        + "answer written has lost the only record of where the answer is. Resolving a note that is already resolved "
        + "changes nothing. A note left on a Brain note is resolved under " + BrainMcpScope.Value + ". Refused for an "
        + "archived repository, whose devbook is read-only.")]
    public ChapterNotePayload ResolveAnnotationFor(
        [Description(RepositoryDescription)]
        string repository,
        [Description("The note's id, exactly as list_annotations reports it.")]
        Guid note)
    {
        var scope = ScopeOf(repository);

        // Local ADR 0029 §4: an archived repository's devbook is read-only, so no
        // fence answering a note can have been written there — and resolving a
        // note with no answer written loses the only record of the remark. Asked
        // before the note, so the refusal is the same whichever id was sent.
        if (scope.Archived)
        {
            throw RepositoryScope.Failure(Error.Validation(
                ArchivedCode,
                $"{scope.Id} is archived, so its devbook is read-only and its notes are not resolved. "
                + "Unarchive it in Backlog's Settings first."));
        }

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
        // than a shortcut worth honouring. The Brain is one more scope to this
        // comparison: its remarks are stored under the empty alias, which no
        // repository's is, so a Brain note asked under a repository and a
        // repository's asked under #brain are both refused here.
        if (!string.Equals(existing.RepositoryAlias, scope.Alias, StringComparison.OrdinalIgnoreCase))
        {
            throw RepositoryScope.Failure(Error.Validation(
                "annotation.otherRepository",
                $"Note {note} belongs to {BrainMcpScope.Display(existing.RepositoryAlias)}, "
                + $"not {BrainMcpScope.Display(scope.Alias)}."));
        }

        if (!existing.Resolved)
        {
            annotations.SetResolved(note, resolved: true);
        }

        // Read back rather than returned from the record above, so the caller is
        // told what the store now holds — the stamp the write moved included.
        return Projections.Note(annotations.Find(note) ?? existing with { Resolved = true });
    }

    // A write, and neither idempotent nor destructive. Not idempotent because a
    // second call with the same title is a second note — the writer never
    // overwrites, it takes the next free name — so a client must not retry one
    // that may have landed. Not destructive for the same reason: nothing that was
    // there is changed. Closed, because the Brain is a folder on this machine and
    // nothing on this path can reach GitHub.
    [McpServerTool(
        Name = CaptureNote,
        Title = "Capture a note into the Brain",
        ReadOnly = false,
        Idempotent = false,
        Destructive = false,
        OpenWorld = false)]
    [Description(
        "Writes a new note into the Brain's inbox area — the person's own notes outside every repository — with the "
        + "title as its heading and the body below it, as the app's own Capture does. Never overwrites: a title whose "
        + "file name is taken gets the next free one, so calling this twice writes two notes. Answers the note's path "
        + "from the Brain's root, which read_knowledge_chapter takes under " + BrainMcpScope.Value + ".")]
    public async Task<CapturedNotePayload> CaptureNoteAsync(
        [Description("The note's title: one line, which is also what its file name is made from.")]
        string title,
        [Description("The note's body, in Markdown. Optional. A line that would become a heading or a fence is kept as text.")]
        string? body = null,
        CancellationToken cancellationToken = default)
    {
        if (brainNotes is null)
        {
            throw RepositoryScope.Failure(Error.NotFound("brain.unavailable", NoBrainHere));
        }

        // Manual: BrainNoteSource names the pane's Capture and an assistant's
        // capture tool as one source, because both are somebody deciding to write
        // a note rather than a note arriving from somewhere else.
        var written = await brainNotes
            .WriteAsync(new BrainNoteDraft(title, body, BrainNoteSource.Manual), cancellationToken)
            .ConfigureAwait(false);

        // In the writer's own words: a blank title, no Brain set, a folder that
        // cannot be written are each something the person can fix, and the
        // writer already says how. Checked here rather than before the call, so
        // the guard on the text stays the one the pane's Capture meets.
        if (written.RelativePath is not { } path)
        {
            throw RepositoryScope.Failure(Error.Validation(
                "brain.capture_refused",
                written.Error ?? "The note was not written."));
        }

        return new CapturedNotePayload(path, BrainNoteText.TitleOf(title) ?? title);
    }

    /// <summary>
    /// The scope a call names, as the id its answer carries and the alias the
    /// note store keys by: <see cref="BrainMcpScope.Value"/> and
    /// <see cref="BrainScope.Value"/> for the Brain, matched before the
    /// directory is asked, else the repository the directory resolves, with
    /// whether it is archived — or the refusal it gives.
    /// </summary>
    private (string Id, string Alias, bool Archived) ScopeOf(string repository)
    {
        if (BrainMcpScope.Is(repository)) return (BrainMcpScope.Value, BrainScope.Value, false);

        var resolved = RepositoryScope.Resolve(repositories, repository, acceptsBrain: true).ValueOrThrow();

        return (resolved.Id, resolved.Alias, resolved.IsArchived);
    }
}
