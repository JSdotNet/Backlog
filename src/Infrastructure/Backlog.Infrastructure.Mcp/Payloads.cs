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

/// <summary>
/// One entry named: its id and where it stands.
/// <para>
/// What every tracker operation that is not a read of the text answers with, so
/// a session that found, moved or created an entry has the two facts it needs to
/// do the next thing — the id it will pass back, and the status it is now
/// reasoning from. The whole <see cref="EntryPayload"/> would be a page of
/// scheduling fields to say "it is in progress"; <c>read_item</c> is the tool
/// for a session that wants the entry itself.
/// </para>
/// </summary>
public sealed record EntryRefPayload(Guid Id, string Title, string Status);

/// <summary>
/// One entry as the text it is, which in this product is the entry itself.
/// </summary>
/// <param name="Markdown">Exactly what <c>EntryTextParser.ToRawText</c> composes,
/// metadata line and all — byte for byte what the pane would save. A session
/// reading anything less would be editing against a shape it could not write
/// back.</param>
public sealed record EntryTextPayload(Guid Id, string Title, string Status, string Markdown);

/// <summary>
/// What a requested status change did, or did not do.
/// </summary>
/// <param name="Status">Where the entry stands <em>now</em>. On a refusal this is
/// the status it kept, not the one that was asked for — a refusal that echoed
/// the target back would read as a success to anything skimming the field.</param>
/// <param name="Changed">Whether the entry moved. False both for a refused move
/// and for one that asked for the status the entry already had, which are
/// different events and the <paramref name="Refusal"/> field is what tells them
/// apart.</param>
/// <param name="Refusal">Why the move was refused, in words, or null when there
/// was nothing to refuse.</param>
/// <param name="NextStatuses">Where the entry may go from
/// <paramref name="Status"/>, off the module's own graph. Present on every
/// answer rather than only on a refusal: a session that has just started work
/// wants to know that <c>done</c> is the next step as much as one that was
/// stopped wants to know why.</param>
public sealed record TransitionPayload(
    Guid Id,
    string Status,
    bool Changed,
    string? Refusal,
    IReadOnlyList<string> NextStatuses);

/// <summary>
/// A note left on an entry, and the sub-items that survived it.
/// </summary>
/// <param name="SubItems">How many the entry has now. Reported because the
/// failure this tool's construction guards against — rewriting the entry's own
/// prose and taking its steps with it — is invisible in a success message, and
/// a session that appended one line has a right to see that it cost nothing.</param>
public sealed record CommentPayload(Guid Id, string Title, string Status, int SubItems);

/// <summary>
/// The external object an entry's work produced, recorded against the entry.
/// </summary>
/// <param name="TargetType">The vocabulary this context keeps for what was
/// linked — <c>pull-request</c> here, never <c>issue</c>, which is a different
/// object with its own screen reading it.</param>
public sealed record LinkPayload(Guid Id, string Repository, string ExternalId, string TargetType);

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

/*
    The delivery surface's answers, and the three shapes its one rich argument
    takes.

    Inputs get project-local records here for the same reason answers do. The
    preamble above argues it about answers because that was all this file held,
    but the hazard is the argument's: a module DTO accepted as a tool parameter
    is a published schema a caller has already been given, and the module is free
    to reshape it for its own screens. An engine that wrote `update_stage` against
    last month's schema is exactly the caller that cannot be told it moved.
*/

/// <summary>What <c>open_dashboard</c> did.</summary>
/// <param name="Activation">The member name of the activation — <c>Shown</c>,
/// <c>Unattached</c> or <c>Disabled</c> — never its ordinal.</param>
/// <param name="Answer">The sentence to report, already written for a person.
/// It carries the reason an unattached or switched-off surface gives, which is
/// the part a caller is expected to pass on rather than summarize.</param>
public sealed record SurfaceOpenedPayload(string Activation, string Answer);

/// <summary>What <c>start_run</c> answered.</summary>
/// <param name="Resumed">True when this reattached to a run already under way
/// rather than starting one. A caller that ignores it restarts a flow from its
/// first stage and redoes work the run has already recorded as done.</param>
/// <param name="SessionTitle">A title the host may set on the session, where the
/// run has one to suggest, else null.</param>
public sealed record RunStartedPayload(string RunId, bool Resumed, string? SessionTitle);

/// <summary>What <c>update_stage</c> answered.</summary>
/// <param name="DoneCount">How many times this stage has completed. It
/// increments on every transition to <c>done</c>, so a second pass after
/// requested changes is visible rather than indistinguishable from the
/// first.</param>
public sealed record StageUpdatedPayload(
    string RunId,
    int StageIndex,
    string Status,
    int DoneCount,
    string? SessionTitle);

/// <summary>One stage of a run, as a session reads it back.</summary>
public sealed record RunStagePayload(string Name, string Status, long? DurationMs, int DoneCount);

/// <summary>
/// One run, as a session reads it back.
/// <para>
/// Deliberately less than the pane shows. The run a flow reads back is the one
/// it is driving, and what it needs is what it is and where it has got to —
/// while token usage, the context gauge and the per-tool insight are measured by
/// watching a session's own tool calls, never authored. Handing those figures to
/// the author invites them into a summary as if the run had claimed them, which
/// is the one thing the engine's contract says a summary must never do.
/// </para>
/// </summary>
/// <param name="InProgress">Whether the run is still open. Derived from
/// <paramref name="Status"/> and carried anyway, so a caller does not have to
/// know which spelling of it counts.</param>
/// <param name="SessionIds">The sessions that drove the run, where the writer
/// recorded any.</param>
public sealed record RunPayload(
    string Id,
    string Worktree,
    string SkillId,
    string Title,
    string Status,
    string? ChangeKind,
    bool InProgress,
    DateTimeOffset? StartedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<RunStagePayload> Stages,
    IReadOnlyList<string> SessionIds);

/// <summary>The runs of one worktree.</summary>
public sealed record RunsPayload(string Worktree, int Count, IReadOnlyList<RunPayload> Runs);

/// <summary>A link to show against a stage — the started application, a review
/// target — so a gate renders a button rather than asking a person to copy a
/// command.</summary>
public sealed record StageLinkInput(string? Label, string? Url, string? Description);

/// <summary>One QA scenario and how it went.</summary>
/// <param name="Evidence">Paths to the evidence that settles it, relative to the
/// worktree the run is in.</param>
public sealed record ScenarioInput(string Name, string Status, string? Notes, IReadOnlyList<string>? Evidence);

/// <summary>What a runtime monitor observed while a stage ran.</summary>
public sealed record MonitoringInput(string? Summary, IReadOnlyList<string>? Findings);
