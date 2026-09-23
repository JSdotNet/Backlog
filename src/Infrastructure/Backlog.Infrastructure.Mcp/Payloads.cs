namespace Backlog.Infrastructure.Mcp;

/*
    What a tool answers with.

    This project's own records rather than the modules' DTOs, and the difference
    is not ceremony. A DTO is a module's published shape and changes when the
    module's screens need it to; a tool's answer is a contract with a session
    that has no way to be told it moved. Projecting also lets a payload leave out
    the fields a reading session has no use for — an attachment, a recurrence
    rule, a band colour — which is the whole of what keeps a tool answer readable
    in a transcript.

    Enums cross as their published wire tokens (EnumMap) where the module has
    one, and as their member name where it does not. Never as an ordinal: a
    number would silently change meaning the day somebody inserts a member.
*/

/// <summary>One backlog entry, as a session reads it.</summary>
/// <param name="Repositories">The <c>owner/name</c> ids the entry is filed
/// against — <c>repo_ids</c>, not aliases.</param>
/// <param name="PlanId">The import plan tag the entry was born under, sigil and
/// all (<c>+backlog-mcp-server</c>), or null for an entry typed by hand.</param>
public sealed record EntryPayload(
    Guid Id,
    string Title,
    string Body,
    string Type,
    string Status,
    string Priority,
    string? Area,
    IReadOnlyList<string> Tags,
    int Order,
    int TotalSubItems,
    int CompletedSubItems,
    DateOnly? DueOn,
    DateOnly? CompletedOn,
    int? Effort,
    IReadOnlyList<string> Repositories,
    IReadOnlyList<string> DependsOn,
    string? PlanId,
    string? PlanItemId,
    DateTimeOffset? CreatedAt);

/// <summary>The entries filed against one repository, in rank order.</summary>
public sealed record EntryListPayload(string Repository, string RepositoryAlias, int Count, IReadOnlyList<EntryPayload> Entries);

/// <summary>The entries one import plan produced, in rank order.</summary>
public sealed record PlanItemsPayload(string PlanId, int Count, IReadOnlyList<EntryPayload> Entries);

/// <summary>One piece of planned work. Both days are inclusive — "through the
/// 31st" means the 31st.</summary>
public sealed record RoadmapItemPayload(
    Guid Id,
    string Title,
    string Tag,
    DateOnly Start,
    DateOnly End,
    string Priority,
    IReadOnlyList<string> RepositoryAliases,
    string? Lane,
    Guid? TaskId,
    IReadOnlyList<Guid> DependsOn,
    string? Notes,
    IReadOnlyList<string> KnowledgeRefs);

/// <summary>A single day the plan is read against.</summary>
public sealed record RoadmapMilestonePayload(
    Guid Id,
    string Title,
    DateOnly On,
    string Kind,
    IReadOnlyList<string> RepositoryAliases,
    string? Lane,
    IReadOnlyList<Guid> DependsOn,
    bool IsPlanWide);

/// <summary>Somewhere the plan disagrees with itself about dates. Reported,
/// never corrected.</summary>
public sealed record RoadmapContradictionPayload(Guid NodeId, Guid DependsOnId, string Reason);

/// <summary>One repository's slice of the plan.</summary>
public sealed record RoadmapPayload(
    string Repository,
    string RepositoryAlias,
    IReadOnlyList<RoadmapItemPayload> Items,
    IReadOnlyList<RoadmapMilestonePayload> Milestones,
    IReadOnlyList<RoadmapContradictionPayload> Contradictions);

/// <summary>One configured knowledge folder, and where it resolved to.</summary>
/// <param name="Message">Why it is unavailable, in the words the panels show.
/// Null while it is available.</param>
/// <param name="Pending">The folder is on its way rather than absent — a branch
/// whose first fetch is running. News, not an error.</param>
public sealed record KnowledgeContextPayload(
    string Key,
    string DisplayName,
    string Path,
    bool Available,
    string? Message,
    string? ScopeLabel,
    string Source,
    bool Pending);

/// <summary>The knowledge folders configured for one repository.</summary>
public sealed record KnowledgeContextsPayload(string Repository, string RepositoryAlias, IReadOnlyList<KnowledgeContextPayload> Contexts);

/// <summary>
/// One private reading note (local ADR 0011), never a devbook <c>annotation</c>
/// fence. Drafts and tombstones never reach this shape.
/// </summary>
public sealed record ChapterNotePayload(
    Guid Id,
    int BlockIndex,
    string Body,
    string Author,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool Resolved);

/// <summary>
/// One block of a chapter, at the index its notes are anchored to.
/// <para>
/// The index is assigned over the full parse in both modes, so <c>review:false</c>
/// omitting a block never renumbers the one after it. That is the property that
/// stops the two modes disagreeing about what block 7 is.
/// </para>
/// </summary>
/// <param name="Kind">The block's shape — <c>heading</c>, <c>paragraph</c>,
/// <c>code</c>, and so on.</param>
/// <param name="StartLine">The block's first line, zero-based, in <b>the file</b>
/// — not in <c>Markdown</c>. The two differ: outside review mode the private
/// fences have been cut out, and inside it the spliced notes have been added. File
/// coordinates are what an index means everywhere else here, since a note's
/// <c>BlockIndex</c> is an index into the parse of the file — so a session asking
/// to edit block 7 is told where block 7 lives in the thing it would edit.
/// <para>
/// <b>Null when the block names no lines</b>, which today is the footnote block
/// and only it: its definitions are collected from wherever in the body they were
/// written, so it sits at no line range at all. Null rather than a pair of zeroes
/// — the earlier spelling, <c>0</c> to <c>0</c>, was indistinguishable from a real
/// empty block at the top of the file, and a caller slicing the chapter by it
/// would cut the wrong thing and never know.</para></param>
/// <param name="EndLineExclusive">One past the block's last line, on the same
/// coordinates, and null exactly when <paramref name="StartLine"/> is. The two are
/// null together or neither: a half-known span is not something the parse
/// produces.</param>
/// <param name="Language">A code fence's language, and null for everything
/// else.</param>
/// <param name="Notes">The private notes anchored here. Always empty outside
/// review mode.</param>
/// <remarks>
/// The block's own text is deliberately absent. <c>Markdown</c> already carries the
/// chapter, so repeating each block's bytes here would return the whole document
/// twice — a cost paid on every read, by a caller that pays per token.
/// </remarks>
public sealed record ChapterBlockPayload(
    int Index,
    string Kind,
    int? StartLine,
    int? EndLineExclusive,
    string? Language,
    IReadOnlyList<ChapterNotePayload> Notes);

/// <summary>
/// A chapter as a session reads it.
/// </summary>
/// <param name="ContextKey">The knowledge folder the chapter was found in.</param>
/// <param name="Markdown">
/// The chapter itself. Outside review mode it is the file with the lines of its
/// <c>meta</c> and <c>annotation</c> fences cut out and every other byte
/// untouched; in review mode it is the file unmodified with each private note
/// spliced in after the block it is anchored to, delimited by
/// <c>&lt;!-- backlog:private-note … --&gt;</c> comment markers so a session can
/// tell a note from the author's prose.
/// <para>
/// This is what a session should read and edit against. <see cref="Blocks"/> is
/// the index beside it: which block is number seven, and what is said about it.
/// </para>
/// </param>
/// <param name="BlockCount">Blocks in the full parse, which is what a block index
/// runs over — not the number of blocks emitted.</param>
/// <param name="OrphanedNotes">Notes whose block index has gone out of range,
/// mirroring what the read view does with them: shown at the end rather than
/// dropped, because a lost note is worse than a stray one.</param>
public sealed record ChapterPayload(
    string Repository,
    string RepositoryAlias,
    string ContextKey,
    string ChapterPath,
    bool Review,
    string Markdown,
    int BlockCount,
    IReadOnlyList<ChapterBlockPayload> Blocks,
    IReadOnlyList<ChapterNotePayload> OrphanedNotes);

/// <summary>The live, typed-into notes on one chapter.</summary>
public sealed record AnnotationsPayload(
    string Repository,
    string RepositoryAlias,
    string ChapterPath,
    int Count,
    IReadOnlyList<ChapterNotePayload> Annotations);

/// <summary>
/// One agent session.
/// </summary>
/// <param name="Repository">What the agent recorded, and null when it recorded
/// nothing.</param>
/// <param name="ResolvedRepository">What this machine worked out from the working
/// folder lying inside a registered clone. A separate field from
/// <paramref name="Repository"/> and never folded into it: one is a fact about
/// the session and the other a fact about this machine's Repositories screen, and
/// they fail differently.</param>
public sealed record SessionPayload(
    string Id,
    string Agent,
    string Environment,
    string Title,
    string WorkingFolder,
    string? Repository,
    string? ResolvedRepository,
    string? Branch,
    DateTimeOffset? StartedAt,
    DateTimeOffset LastActivityAt,
    string State,
    int? TurnCount,
    string Origin);

/// <summary>
/// The sessions this machine can describe.
/// </summary>
/// <param name="Repository">The <c>owner/name</c> the list was narrowed to, on
/// <see cref="SessionPayload.ResolvedRepository"/>, or null when it was not
/// narrowed at all.</param>
/// <param name="Count">How many sessions are in <paramref name="Sessions"/> —
/// after the filter, where there was one.</param>
/// <param name="Discovered">How many existed before the cap. The catalog's own
/// number, over every agent and every repository: it is not
/// <paramref name="Count"/>'s total, and with a
/// <paramref name="Repository"/> asked for the two are not comparable.</param>
/// <param name="Capped">Whether the cap took anything — the catalog's own
/// truncation and never this list's filtering. A truncated list has to say so:
/// showing 200 of 842 without mentioning the 842 would present a truncated list
/// as the whole history. It stays true under a filter because the cap ran first,
/// which is exactly when it matters — a repository's oldest sessions can be
/// missing from a capped catalog and nothing downstream can tell.</param>
/// <param name="Unreadable">The sources that could not be read, by name. Not
/// filtered: a source that could not be read might have held sessions for any
/// repository, this one included.</param>
public sealed record SessionsPayload(
    string? Repository,
    int Count,
    int Discovered,
    bool Capped,
    IReadOnlyList<string> Unreadable,
    IReadOnlyList<SessionPayload> Sessions);
