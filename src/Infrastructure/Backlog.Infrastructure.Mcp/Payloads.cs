using System.Text.Json;

using Backlog.Modules.Sessions.Abstractions;

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
/// <param name="StartedOn">The day work first moved to in progress, or null
/// when it never has.</param>
/// <param name="BlockedSince">The day a person marked the entry blocked by hand,
/// or null when it is not marked. Not a status and not the derived "blocked" of an
/// unfinished <c>after:</c> chain, which is never stored: a marked entry keeps its
/// status and still counts as ready to the entries waiting on it.</param>
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
    DateOnly? StartedOn,
    DateOnly? CompletedOn,
    DateOnly? BlockedSince,
    int? Effort,
    IReadOnlyList<string> Repositories,
    IReadOnlyList<string> DependsOn,
    string? PlanId,
    string? PlanItemId,
    DateTimeOffset? CreatedAt);

/// <summary>The entries filed against one repository, in rank order.</summary>
public sealed record EntryListPayload(string Repository, string RepositoryAlias, int Count, IReadOnlyList<EntryPayload> Entries);

/// <summary>The entries of one import plan — imported or filed under its tag since — in rank order.</summary>
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
/// <param name="DevbookReferences">The Devbook pages and chapters the entry
/// points at, as stored (<c>path</c> or <c>path#anchor</c>). Beside the text
/// rather than in it, because they are never part of it: saving the markdown
/// leaves them alone, and <c>set_devbook_references</c> is how they change.</param>
public sealed record EntryTextPayload(Guid Id, string Title, string Status, string Markdown, IReadOnlyList<string> DevbookReferences);

/// <summary>
/// An entry's Devbook references, each resolved against the repository's
/// devbook as it stands now.
/// </summary>
/// <param name="Repository">The <c>owner/name</c> the references were resolved
/// in.</param>
/// <param name="References">In the order the entry keeps them.</param>
public sealed record DevbookReferencesPayload(Guid Id, string Repository, IReadOnlyList<DevbookReferencePayload> References)
{
    /// <summary>
    /// The entry's acceptance evidence over every reference: the scenario parts
    /// they stand for, how many of them pass, and whether the entry is proved —
    /// every part passed on a current signature. Null when no reference stands for
    /// a scenario part. A signal only: a proved entry keeps its status until
    /// somebody moves it.
    /// </summary>
    public AcceptancePayload? Acceptance { get; init; }
}

/// <summary>An entry's scenario parts, counted.</summary>
/// <param name="Passing">How many parts passed on a current signature.</param>
/// <param name="Total">How many distinct parts the references stand for.</param>
/// <param name="Proved">Whether every part passed — and there is at least one.</param>
/// <param name="Summary">"2 of 3 passing".</param>
public sealed record AcceptancePayload(int Passing, int Total, bool Proved, string Summary);

/// <summary>One scenario part a reference stands for, with the state of its last
/// run.</summary>
/// <param name="Reference">The part as <c>&lt;scenarios file&gt;#&lt;anchor&gt;</c>.</param>
/// <param name="Page">The scenarios file the part's journey is in, repository-relative.</param>
/// <param name="PageTitle">The journey's heading.</param>
/// <param name="Stem">The journey's anchor: its run folder's name.</param>
/// <param name="Part">The part's anchor.</param>
/// <param name="Title">The part's heading.</param>
/// <param name="State"><c>passed</c>, <c>failed</c>, <c>stale</c> — the journey
/// changed since its last run — or <c>never-run</c>.</param>
/// <param name="LastRun">When the journey last ran, or null.</param>
public sealed record ScenarioPartPayload(
    string Reference,
    string Page,
    string PageTitle,
    string Stem,
    string Part,
    string Title,
    string State,
    DateTimeOffset? LastRun)
{
    /// <summary>The 1-based step the last run stopped on, for a <c>failed</c> part
    /// whose run recorded one; null otherwise. Added beside the positional fields
    /// so a caller that never asked for it reads the payload as before.</summary>
    public int? FailedStep { get; init; }
}

/// <summary>One Devbook reference and what it points at.</summary>
/// <param name="Reference">As the entry stores it.</param>
/// <param name="Path">The page, repository-relative.</param>
/// <param name="Anchor">The heading slug, or null for a whole page.</param>
/// <param name="State"><c>chapter</c> or <c>page</c> when found;
/// <c>unknown-heading</c>, <c>unknown-page</c> or <c>outside-devbook</c> when
/// broken; <c>unverified</c> when the devbook could not be read.</param>
/// <param name="Title">The heading or page title, else the reference.</param>
/// <param name="Status">The chapter's <c>meta</c> status, or null.</param>
/// <param name="Folder">The devbook folder (<c>domain</c>, <c>arc42</c>, …), or
/// null outside every one.</param>
public sealed record DevbookReferencePayload(
    string Reference,
    string Path,
    string? Anchor,
    string State,
    string Title,
    string? Status,
    string? Folder)
{
    /// <summary>The scenario parts this reference stands for — the part, every part
    /// of a journey or of a scenarios file, or the parts a requirement's
    /// <c>Proved by:</c> lines name — each with its run state. Null when it stands for none.</summary>
    public IReadOnlyList<ScenarioPartPayload>? Parts { get; init; }
}

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
/// Whether an entry is marked blocked after a <c>set_blocked</c> call, and
/// whether the call changed anything.
/// </summary>
/// <param name="Status">The lifecycle status, unchanged by the call and reported
/// so a session can see that it was: marking an entry blocked is not a status
/// move.</param>
/// <param name="BlockedSince">The day the entry was marked blocked, or null when
/// it is not marked. An entry that was already marked keeps its original
/// day.</param>
/// <param name="Changed">False when the entry was already where the call asked —
/// the call was then a read, and nothing was saved.</param>
public sealed record BlockedPayload(
    Guid Id,
    string Title,
    string Status,
    DateOnly? BlockedSince,
    bool Changed);

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

/// <summary>
/// The AI session recorded against an entry.
/// </summary>
/// <param name="AlreadyLinked">True when the entry already named this session and
/// nothing was written, so a session that records itself again learns it was a
/// no-op rather than wondering whether it now appears twice.</param>
public sealed record SessionLinkPayload(Guid Id, string Repository, string SessionId, bool AlreadyLinked);

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

/// <summary>One configured devbook folder, and where it resolved to.</summary>
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

/// <summary>The devbook folders configured for one repository.</summary>
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
/// <param name="ContextKey">The devbook folder the chapter was found in.</param>
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
/// <param name="Demos">The click demos the page has, by path — never their HTML.</param>
public sealed record ChapterPayload(
    string Repository,
    string RepositoryAlias,
    string ContextKey,
    string ChapterPath,
    bool Review,
    string Markdown,
    int BlockCount,
    IReadOnlyList<ChapterBlockPayload> Blocks,
    IReadOnlyList<ChapterNotePayload> OrphanedNotes,
    IReadOnlyList<ChapterDemoPayload> Demos);

/// <summary>
/// One click demo a chapter's page has.
/// </summary>
/// <param name="Path">The <c>*.demo.html</c>, repository-relative.</param>
/// <param name="Address">What follows the <c>#</c> of the address a <c>demo</c>
/// field wrote — a screen, <c>screen/anchor?role=…</c>, or
/// <c>walkthrough/&lt;id&gt;</c> — and null for the demo as a whole.</param>
/// <param name="PairedBy"><c>name</c> for a demo beside the page whose name pairs
/// it with the page (<c>features.demo.html</c> with <c>features.md</c>,
/// <c>demo.html</c> with <c>context.md</c>); <c>field</c> for a place a
/// <c>demo</c> field names.</param>
/// <param name="Block">The <c>meta</c> block whose field names it, by the index the
/// blocks run over; null for a pairing by name.</param>
/// <param name="Exists">Whether the demo file is there; null when that cannot be
/// told from here.</param>
public sealed record ChapterDemoPayload(
    string Path,
    string? Address,
    string PairedBy,
    int? Block,
    bool? Exists);

/// <summary>
/// A note <c>capture_note</c> wrote into the Brain's inbox area.
/// </summary>
/// <param name="Path">The note's path relative to the Brain's root, with <c>/</c>
/// between segments (<c>_inbox/ideas-for-the-sync.md</c>) — the spelling
/// <c>read_knowledge_chapter</c> takes under <c>#brain</c>, so a session can read
/// back what it wrote without translating it.</param>
/// <param name="Title">The title as the note's heading carries it: one line, the
/// whitespace the writer folds already folded.</param>
public sealed record CapturedNotePayload(string Path, string Title);

/// <summary>The live, typed-into notes on one chapter.</summary>
/// <param name="Archived">Whether the repository is archived (local ADR 0029
/// §4). Its notes still list, because a remark already kept stays readable, but
/// its devbook is read-only: no <c>annotation</c> fence is written into it and
/// <c>resolve_annotation</c> refuses its notes. False for the Brain.</param>
public sealed record AnnotationsPayload(
    string Repository,
    string RepositoryAlias,
    string ChapterPath,
    int Count,
    IReadOnlyList<ChapterNotePayload> Annotations,
    bool Archived = false);

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
/// <param name="State">The session's state; <c>Waiting</c> while it holds an open waiting
/// question (local ADR 0041), which the Manager's capacity count leaves out.</param>
/// <param name="DesktopSessionId">The Claude desktop app's id for the session — the id its
/// session tools take — where its session store holds a record on this machine, else null.
/// What the Manager matches its dispatched sessions against.</param>
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
    string Origin,
    string? DesktopSessionId = null);

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

/// <summary>
/// One session request as a dispatcher sees it (local ADR 0038), on the wait route and
/// from every dispatcher tool alike — one shape, so a request handed out by the route
/// reads the same when <c>list_session_requests</c> answers it again.
/// </summary>
/// <param name="Id">The request's id: what the dispatcher claims and marks.</param>
/// <param name="Kind"><c>entry</c> or <c>plan</c>.</param>
/// <param name="EntryId">The entry an entry request runs; null on a plan request.</param>
/// <param name="PlanTag">The plan a plan request runs, without its sigil; null on an
/// entry request.</param>
/// <param name="Repository">The repository, as <c>owner/name</c>.</param>
/// <param name="CloneFolder">That repository's clone folder on this machine: where the
/// session starts.</param>
/// <param name="State">The state it reads as now — a lapsed claim reads
/// <c>requested</c>: <c>requested</c>, <c>claimed</c>, <c>started</c>, <c>linked</c> or
/// <c>cancelled</c>.</param>
/// <param name="ClaimedBy">The dispatcher holding the claim, while claimed.</param>
/// <param name="LeaseEndsAt">When that claim's lease runs out, while claimed.</param>
/// <param name="SessionId">The session the dispatcher reported, once started.</param>
/// <param name="CreatedAt">When the person asked.</param>
/// <param name="Brief">The line to start the session with, built by the server from the
/// kind and the id alone. Passed on as it is; a dispatcher never writes its own.</param>
public sealed record SessionRequestPayload(
    Guid Id,
    string Kind,
    Guid? EntryId,
    string? PlanTag,
    string Repository,
    string CloneFolder,
    string State,
    string? ClaimedBy,
    DateTimeOffset? LeaseEndsAt,
    string? SessionId,
    DateTimeOffset CreatedAt,
    string Brief)
{
    /// <summary>The request as it reads at <paramref name="now"/>.</summary>
    public static SessionRequestPayload From(SessionRequest request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);

        var state = request.StateAt(now);
        var claimed = state == SessionRequestState.Claimed;

        return new SessionRequestPayload(
            request.Id,
            WireName(request.Kind),
            request.EntryId,
            request.PlanTag,
            request.Repository,
            request.CloneFolder,
            WireName(state),
            claimed ? request.ClaimedBy : null,
            claimed ? request.LeaseEndsAt : null,
            request.SessionId,
            request.CreatedAt,
            SessionRequests.BriefFor(request));
    }

    /// <summary>A kind or state as the wire spells it: its member name, lower-cased.</summary>
    public static string WireName<TEnum>(TEnum value)
        where TEnum : struct, Enum => value.ToString().ToLowerInvariant();
}

/// <summary>What <c>claim_session_request</c> answered.</summary>
/// <param name="Claimed">Whether this dispatcher now holds the request under the
/// 2-minute lease. False when it was taken — claimed, started, linked or cancelled
/// already — and then the dispatcher leaves it alone.</param>
/// <param name="Refusal">Why it was not claimed, or null.</param>
/// <param name="Request">The request as it now stands.</param>
public sealed record SessionRequestClaimPayload(bool Claimed, string? Refusal, SessionRequestPayload Request);

/// <summary>What <c>mark_session_request</c> answered.</summary>
/// <param name="Outcome"><c>started</c> or <c>released</c>, as asked.</param>
/// <param name="Marked">Whether the request moved. False when this dispatcher no longer
/// holds the claim — its lease ran out, or another dispatcher holds it.</param>
/// <param name="Refusal">Why it did not move, or null.</param>
/// <param name="Request">The request as it now stands.</param>
public sealed record SessionRequestMarkPayload(string Outcome, bool Marked, string? Refusal, SessionRequestPayload Request);

/// <summary>What <c>list_session_requests</c> answered: the requests, oldest first.</summary>
/// <param name="State">The state the list was narrowed to, or null for every request.</param>
public sealed record SessionRequestsPayload(string? State, int Count, IReadOnlyList<SessionRequestPayload> Requests);

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

/// <summary>What <c>open_dashboard</c> answered.</summary>
/// <param name="Activation">The member name of the activation — <c>Available</c>,
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
/// <param name="Execution">How the stage ran, as <c>update_stage</c> last reported it:
/// the object it was sent, or null where none was.</param>
public sealed record RunStagePayload(string Name, string Status, long? DurationMs, int DoneCount, JsonElement? Execution = null);

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
/// <param name="Trigger">What started the run, where the caller said.</param>
/// <param name="Schedule">The schedule that fired it, for a scheduled run.</param>
/// <param name="Repo">The repository it worked in, where the caller said.</param>
/// <param name="RunContext">The run context <c>set_run_context</c> kept, as an
/// object — authored by the flow, unlike the measured figures this leaves out, so a
/// flow reading it back reads its own words.</param>
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
    IReadOnlyList<string> SessionIds,
    string? Trigger = null,
    string? Schedule = null,
    string? Repo = null,
    JsonElement? RunContext = null);

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

/// <summary>One journey part a verify stage ran, read from the journey's
/// <c>run.json</c> beside its scenarios file.</summary>
/// <param name="Stem">The journey's anchor: its run folder's name.</param>
/// <param name="Part">The part's anchor.</param>
/// <param name="Outcome"><c>passed</c>, <c>failed</c> or <c>not-run</c>.</param>
/// <param name="RunAt">The run's <c>ranAt</c>.</param>
public sealed record EvidenceInput(string? Stem, string? Part, string? Outcome, string? RunAt);

/// <summary>What a runtime monitor observed while a stage ran.</summary>
public sealed record MonitoringInput(string? Summary, IReadOnlyList<string>? Findings);

/// <summary>One chapter of a sync unit's verdict row.</summary>
/// <param name="Chapter">The chapter id, <c>path#anchor</c>.</param>
/// <param name="Verdict">aligned, code-ahead, spec-ahead, conflict or unresolved.</param>
/// <param name="Evidence">One line of evidence.</param>
public sealed record SyncChapterVerdictInput(string Chapter, string? Verdict, string? Evidence);

/// <summary>One unit row of a devbook sweep's <c>devbook-sync-report</c> block, as
/// <c>finish_run</c> takes it — the block's own keys.</summary>
/// <param name="Unit">The unit's root chapter id.</param>
/// <param name="Kind">The unit kind.</param>
/// <param name="Sync">The effective sync direction.</param>
/// <param name="SyncFrom">The block the direction came from, or null.</param>
/// <param name="Verdict">The rolled-up verdict.</param>
/// <param name="Action">pr, issue, flagged, waiting, deferred, skipped, failed,
/// not-assessed or none.</param>
/// <param name="Link">The pull request or drift issue URL, or null.</param>
/// <param name="Chapters">Each chapter's own verdict.</param>
public sealed record SyncUnitVerdictInput(
    string Unit,
    string? Kind,
    string? Sync,
    string? SyncFrom,
    string? Verdict,
    string? Action,
    string? Link,
    IReadOnlyList<SyncChapterVerdictInput>? Chapters);

/// <summary>What <c>open_window</c> answered.</summary>
/// <param name="Surface">The surface's id, as the catalog holds it.</param>
/// <param name="Target"><c>desktop</c> or <c>web</c>.</param>
/// <param name="Opened">True when a native window was opened. Always false for
/// <c>web</c>, which opens nothing.</param>
/// <param name="Url">For <c>web</c>, the page's full address with its single-use link
/// token, or null when no page is served now. Null for <c>desktop</c>.</param>
/// <param name="BacklogLink">The <c>backlog://window/{surfaceId}?…</c> link that opens the
/// same surface in the installed app. It carries no token and stays valid.</param>
/// <param name="Answer">The sentence to report, already written for a person.</param>
public sealed record WindowOpenedPayload(
    string Surface,
    string Target,
    bool Opened,
    string? Url,
    string BacklogLink,
    string Answer);
