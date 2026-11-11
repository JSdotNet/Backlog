using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;

using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Markdown;
using Backlog.SharedKernel.Results;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// Five of the seven operations of <c>devbook.decision@1</c>: two on state — read a
/// subject's workflow state, set a proposal back to draft — answered over
/// Backlog's own subject store, and the three on review notes — list them at an
/// address, post one, resolve one — answered over its remark store.
/// <para>
/// <b>The decisions are the person's (local ADR 0052 §7).</b> <c>devbook_approve</c>
/// and <c>devbook_accept</c> are not served: an agent holding the MCP token could
/// approve its own proposal, and the record named the person. The person approves
/// and accepts in the devbook view, through <see cref="DevbookProposalDecision"/>.
/// <c>devbook_set_state</c> takes <c>draft</c> alone, so no other state — decided,
/// declined, planned — is set over MCP either.
/// </para>
/// <para>
/// <b>Why the state is here and not in the chapter.</b> Under devbook's
/// branch-true decision a branch states what is true of its code, and "approved",
/// "planned" or "decided" is a fact about the work around a chapter rather than
/// about the chapter, so the host that runs the workflow keeps it
/// (<see cref="IDevbookSubjectStateStore"/>). A caller resolves this group from
/// the live tool list by the <c>devbook_*</c> prefix; with two operations unlisted
/// it finds Backlog's read and note half only.
/// </para>
/// <para>
/// <b>A note is a remark.</b> The contract's note is Backlog's
/// <see cref="DevbookAnnotation"/>, the same record the chapter view draws and
/// <c>list_annotations</c> reads, kept on the default branch because the contract
/// carries none. An open <c>question</c> under a change's folder makes the
/// person's approve and accept refuse <c>open-question</c>
/// (<see cref="DevbookSubjectRules.OpenQuestion"/>).
/// </para>
/// <para>
/// <b>The names, arguments and answers are the contract's</b>, matched by callers
/// this repository does not ship. Every call carries <c>repository</c> as
/// <c>owner/name</c>, resolved through <see cref="RepositoryScope"/> to the alias
/// the store keys by. The answer is <see cref="DevbookStatePayload"/>:
/// <c>{ state }</c>, with <c>approval</c> and <c>acceptance</c> for a proposal, or
/// <c>{ refused, message }</c>.
/// </para>
/// <para>
/// <b>Two kinds of no.</b> What the contract refuses — a state other than draft,
/// and a subject it could not name — is an answer, because
/// the contract says it is one and a caller reports it rather than retrying. What
/// is not the contract's question — no repository, one this host does not know,
/// the Brain, an archived repository — is thrown, as every other tool here
/// throws it, because a caller has nothing to report about a subject it never
/// reached.
/// </para>
/// <para>
/// <b>Who and when are the host's.</b> <c>by</c> is
/// <see cref="DevbookLocalAuthor.AgentViaMcp"/> on every write here — the token is
/// an agent's, so nothing written through it is recorded as the person's — and
/// <c>at</c> the store's clock; neither is an argument.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class DecisionTools(
    IDevbookSubjectStateStore subjects,
    IRepositoryDirectory repositories,
    IDevbookFolderSource folders,
    IDevbookAnnotationStore annotations,
    TimeProvider? clock = null)
{
    /// <summary>The architecture folder's key, as the arc42 panel resolves
    /// it.</summary>
    private const string Arc42Key = ".arc42";

    /// <summary>The instructions folder's key: its root is the repository
    /// itself, which is where a change's files are read from.</summary>
    private const string InstructionsKey = "instructions";

    internal const string GetState = "devbook_get_state";
    internal const string SetState = "devbook_set_state";
    internal const string ListNotes = "devbook_list_notes";
    internal const string PostNote = "devbook_post_note";
    internal const string ResolveNote = "devbook_resolve_note";

    /// <summary>The one state <see cref="SetStateOf"/> sets.</summary>
    private const string Draft = "draft";

    /// <summary>Why every other state is refused, as the caller reads it.</summary>
    internal const string DecisionsInTheAppMessage =
        "Decisions are made in the Backlog app: over MCP a subject's state can only be set to draft. Ask the person "
        + "to approve, accept or decide it in the devbook view.";

    /// <summary>The code the refusal of the Brain carries.</summary>
    internal const string BrainCode = "brain.no_workflow_state";

    /// <summary>
    /// This class as a switchable group, behind <c>repository-devbook</c> — the key
    /// <see cref="BacklogMcpTools.Devbook"/> reads.
    /// <para>
    /// <b>The shared key is the decision.</b> Local ADR 0012 §7 asks for one check
    /// per group, each behind its area's key, and these are the Devbook's: somebody
    /// switching the devbook off has switched off the records whose state these
    /// set as much as the chapters the other group reads. A group of its own
    /// rather than five more tools on <see cref="DevbookTools"/> because they are
    /// another contract — that class reads chapters and a reader's notes, these
    /// answer <c>devbook.decision@1</c>, whose names and shapes are devbook's.
    /// </para>
    /// <para>
    /// Declared here rather than beside the other groups so that adding it to
    /// <see cref="BacklogMcpTools.Groups"/> is the one shared line this class
    /// touches.
    /// </para>
    /// </summary>
    internal static BacklogMcpTools.McpToolGroup Group { get; } = new(
        DevbookFeatures.RepositoryDevbook,
        typeof(DecisionTools),
        [GetState, SetState, ListNotes, PostNote, ResolveNote]);

    private const string RepositoryDescription =
        "The repository in owner/name form, e.g. JSdotNet/Backlog. The Brain (" + BrainMcpScope.Value
        + ") keeps no workflow state and is refused.";

    private const string SubjectDescription =
        "The subject: { kind: proposal, name } for a change under openspec/changes/; { kind: decision, path, "
        + "pullRequest } for a decision record; { kind: debt, path } for a debt record. A path is the record's "
        + "repository path.";

    // Closed: the state is Backlog's own store and nothing on this path can reach
    // GitHub.
    [McpServerTool(Name = GetState, Title = "Read a devbook subject's workflow state", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "The workflow state Backlog keeps for one devbook subject (devbook.decision@1): { state }, null when none is "
        + "kept; for a proposal also approval and acceptance, each { by, at, fingerprint } or null. A decision whose "
        + "fingerprint differs from the change's current one has lapsed. A subject the contract cannot name answers "
        + "{ refused: bad-state, message }. Read-only.")]
    public DevbookStatePayload GetStateOf(
        [Description(RepositoryDescription)]
        string repository,
        [Description(SubjectDescription)]
        DevbookSubjectArgument subject)
    {
        var scope = Scope(repository, writes: false);

        return TryName(scope, subject, out var named, out var refused)
            ? DevbookStatePayload.Of(named, subjects.Get(scope.Alias, named))
            : refused;
    }

    // Idempotent: setting draft on a draft leaves it as it was — the stamp moves,
    // the state does not. Not destructive by this assembly's line, which only a
    // delete crosses, though draft withdraws a proposal's decisions: the
    // description says so, and the person can decide again in the app.
    [McpServerTool(
        Name = SetState,
        Title = "Set a devbook subject's workflow state",
        ReadOnly = false,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description(
        "Sets a change proposal back to draft (devbook.decision@1), which withdraws its approval and acceptance. "
        + "Decisions are made in the Backlog app: every other state, and every state of a decision or debt record, "
        + "answers { refused: bad-state, message }. Answers the state as devbook_get_state does; a refusal writes "
        + "nothing. Recorded by Backlog as agent via MCP, at its own clock. Refused for an archived repository.")]
    public DevbookStatePayload SetStateOf(
        [Description(RepositoryDescription)]
        string repository,
        [Description(SubjectDescription)]
        DevbookSubjectArgument subject,
        [Description("draft, the one state set over MCP.")]
        string state)
    {
        var scope = Scope(repository, writes: true);

        if (!TryName(scope, subject, out var named, out var refused)) return refused;

        // Ordinal, as the rules compare a state: "Draft" is no state in any row.
        return string.Equals(state, Draft, StringComparison.Ordinal)
            ? DevbookStatePayload.Of(named, subjects.SetState(scope.Alias, named, state, DevbookLocalAuthor.AgentViaMcp))
            : DevbookStatePayload.Refusal(DevbookSubjectRefusal.BadState, DecisionsInTheAppMessage);
    }

    private const string AddressDescription =
        "A chapter address: <path>, or <path>#<slug> for a section, the path repository-relative — in a change, "
        + "openspec/changes/<name>/proposal.md or a delta's path.";

    /// <summary>The block a note on a chapter as a whole is anchored to: the
    /// first, where the one-time import anchors a fence with nothing above it, and
    /// where the chapter view draws it at the top rather than as adrift.</summary>
    private const int ChapterBlock = 0;

    // Closed: the notes are Backlog's own store, and the chapter is read only from
    // what this machine already holds.
    [McpServerTool(Name = ListNotes, Title = "List the review notes at a devbook address", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "The review notes Backlog keeps at one chapter address on the default branch (devbook.decision@1): { notes }, "
        + "each { id, address, kind, status, body, by, at, replies }. A <path> answers every note on the chapter, a "
        + "<path>#<slug> the notes in that section. kind is comment, question, suggestion or flag; status open or "
        + "resolved; at ISO 8601 UTC. Each reply is { by, at, body } in replies, oldest first. Deleted notes and notes "
        + "nobody has typed into are left out. Read-only.")]
    public DevbookNotesPayload ListNotesOn(
        [Description(RepositoryDescription)]
        string repository,
        [Description(AddressDescription)]
        string address)
    {
        var scope = Scope(repository, writes: false);
        var (path, slug) = Address(address);
        var chapter = Chapter(scope.Alias, path);
        var section = slug is null ? null : $"{chapter.Path}#{slug}";

        return new DevbookNotesPayload(
        [
            .. ChapterReading.Notes(annotations, scope.Alias, path, branch: null)
                .Select(note => DevbookNotePayload.Of(note, chapter.AddressOf(note.BlockIndex)))
                .Where(note => section is null || string.Equals(note.Address, section, StringComparison.OrdinalIgnoreCase))
        ]);
    }

    // Not idempotent: each call is a note of its own, or one more reply line in
    // one. Not destructive: nothing that was there is changed but by appending.
    [McpServerTool(
        Name = PostNote,
        Title = "Post a review note at a devbook address",
        ReadOnly = false,
        Idempotent = false,
        Destructive = false,
        OpenWorld = false)]
    [Description(
        "Posts a review note at a chapter address on the default branch (devbook.decision@1) and answers it: "
        + "{ id, address, kind, status, body, by, at, replies }. Give kind (comment by default, question, suggestion or "
        + "flag) for a new note, or replyTo with a note's id to reply to it — the note is answered, the reply the last "
        + "of its replies, each { by, at, body }. A reply to a note this repository does not keep answers "
        + "{ refused: unknown-note, message } and writes nothing. An open question under openspec/changes/<name>/ "
        + "blocks the person's approval and acceptance of that change in the app until it is resolved. Recorded by "
        + "Backlog as agent via MCP, at its own clock. Refused for an archived repository.")]
    public DevbookNoteAnswer PostNoteOn(
        [Description(RepositoryDescription)]
        string repository,
        [Description(AddressDescription)]
        string address,
        [Description("The note, or the reply, in Markdown.")]
        string body,
        [Description("comment (the default), question, suggestion or flag. A new note only; not with replyTo.")]
        string? kind = null,
        [Description("The id of the note this replies to, as devbook_list_notes reports it. Not with kind.")]
        string? replyTo = null)
    {
        var scope = Scope(repository, writes: true);
        var (path, slug) = Address(address);

        if (string.IsNullOrWhiteSpace(body))
        {
            throw RepositoryScope.Failure(Error.Validation("note.body_required", "A note has a body."));
        }

        var said = body.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

        if (replyTo is not null)
        {
            if (kind is not null)
            {
                throw RepositoryScope.Failure(Error.Validation(
                    "note.reply_has_kind",
                    "A reply takes its note's kind: send kind for a new note or replyTo for a reply, not both."));
            }

            return Reply(scope, path, replyTo, said);
        }

        var noteKind = DevbookAnnotationKind.Comment;

        if (kind is not null && !DevbookAnnotationKinds.TryParse(kind, out noteKind))
        {
            throw RepositoryScope.Failure(Error.Validation(
                "note.bad_kind",
                $"'{kind}' is no note kind; a note is one of {string.Join(", ", DevbookAnnotationKinds.Names)}."));
        }

        var chapter = Chapter(scope.Alias, path);
        var block = slug is null ? ChapterBlock : chapter.IndexOf(slug) ?? ChapterBlock;

        // No block hash: the digest the panels anchor by is the component
        // library's, which this assembly does not reference, so a note posted here
        // is anchored by index alone — what a host without the parsed chapter
        // already gets (IDevbookAnnotationStore.Add).
        var added = annotations.Add(scope.Alias, chapter.Path, block, DevbookLocalAuthor.AgentViaMcp, blockHash: null, noteKind, branch: null);
        annotations.Edit(added.Id, said);

        return Answer(scope, annotations.Find(added.Id) ?? added with { Body = said });
    }

    // Idempotent, as resolve_annotation is: resolving a resolved note is the state
    // it is already in. Not destructive: a resolved note stays and can be reopened
    // in the app.
    [McpServerTool(
        Name = ResolveNote,
        Title = "Resolve a review note",
        ReadOnly = false,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description(
        "Marks a review note resolved (devbook.decision@1) and answers it with status resolved. A note this "
        + "repository does not keep on the default branch answers { refused: unknown-note, message } and writes "
        + "nothing. Resolving a resolved note changes nothing. Refused for an archived repository.")]
    public DevbookNoteAnswer ResolveNoteFor(
        [Description(RepositoryDescription)]
        string repository,
        [Description("The note's id, as devbook_list_notes or devbook_post_note reports it.")]
        string id)
    {
        var scope = Scope(repository, writes: true);

        if (Held(scope, id) is not { } note) return DevbookNoteAnswer.Unknown(id);

        if (!note.Resolved) annotations.SetResolved(note.Id, resolved: true);

        return Answer(scope, annotations.Find(note.Id) ?? note with { Resolved = true });
    }

    /// <summary>
    /// Folds a reply into the note it answers (<see cref="DevbookNoteReplies.Fold"/>,
    /// the shape the one-time import folds a fence's replies in) and answers the
    /// note, the reply among its <c>replies</c>.
    /// <para>
    /// Backlog's remark has no thread, and a record per reply would be a note of
    /// its own in every panel. A note at another chapter than the address names is
    /// not the one the caller meant, and is <c>unknown-note</c> there.
    /// </para>
    /// </summary>
    private DevbookNoteAnswer Reply(TasksRepositoryRef scope, string path, string replyTo, string said)
    {
        if (Held(scope, replyTo) is not { } note) return DevbookNoteAnswer.Unknown(replyTo);

        var configured = folders.Folders(scope.Alias);

        if (!string.Equals(
                DevbookChapterKey.Canonical(note.ChapterPath, configured),
                DevbookChapterKey.Canonical(path, configured),
                StringComparison.OrdinalIgnoreCase))
        {
            return DevbookNoteAnswer.Unknown(replyTo);
        }

        var at = DevbookDecisionPayload.Utc((clock ?? TimeProvider.System).GetUtcNow());
        var folded = DevbookNoteReplies.Fold(note.Body, DevbookLocalAuthor.AgentViaMcp, at, said);

        annotations.Edit(note.Id, folded);

        return Answer(scope, annotations.Find(note.Id) ?? note with { Body = folded });
    }

    /// <summary>A note as the contract answers it, at the address its block
    /// lies in.</summary>
    private DevbookNoteAnswer Answer(TasksRepositoryRef scope, DevbookAnnotation note) =>
        DevbookNoteAnswer.Of(DevbookNotePayload.Of(note, Chapter(scope.Alias, note.ChapterPath).AddressOf(note.BlockIndex)));

    /// <summary>
    /// A note this repository keeps on the default branch, by the id a caller
    /// sent, or null: not an id, no such note, deleted, a draft nobody has typed
    /// into, another repository's, or one left on a named branch — every one of
    /// them the contract's <c>unknown-note</c>.
    /// </summary>
    private DevbookAnnotation? Held(TasksRepositoryRef scope, string? id) =>
        Guid.TryParse(id, out var guid)
        && annotations.Find(guid) is { IsLive: true, IsDraft: false } note
        && DevbookAnnotation.BranchKey(note.Branch) is null
        && string.Equals(DevbookSubject.AliasKey(note.RepositoryAlias), DevbookSubject.AliasKey(scope.Alias), StringComparison.Ordinal)
            ? note
            : null;

    /// <summary>An address's chapter path and section slug — lower case, the
    /// way a heading's anchor is — or the failure of an address that names no
    /// chapter.</summary>
    private static (string Path, string? Slug) Address(string? address)
    {
        var text = (address ?? string.Empty).Trim();
        var path = ChapterPaths.Normalize(text);

        if (path.Length == 0)
        {
            throw RepositoryScope.Failure(Error.Validation(
                "note.address_required",
                "A note's address is a chapter path, repository-relative, with an optional #slug — e.g. "
                + "openspec/changes/add-decision-tools/proposal.md."));
        }

        var hash = text.IndexOf('#', StringComparison.Ordinal);
        var slug = hash < 0 ? string.Empty : text[(hash + 1)..].Trim();

        return (path, slug.Length == 0 ? null : slug.ToLowerInvariant());
    }

    /// <summary>
    /// A chapter as the note tools address it: its path as the repository spells
    /// it, and its headings when this machine holds the file.
    /// <para>
    /// A chapter in a devbook folder is found where the folder resolves — the
    /// panels' spelling, whatever layout or case the caller sent — and one in no
    /// folder, a change's proposal or delta, under the repository's root. Nothing
    /// is fetched: a chapter not on disk has no headings, and a note on one of its
    /// sections is anchored to the chapter as a whole.
    /// </para>
    /// </summary>
    private NoteChapter Chapter(string alias, string path)
    {
        var configured = folders.Folders(alias);
        var key = DevbookChapterKey.Canonical(path, configured);

        foreach (var folder in DevbookFolderSetting.Normalize(configured))
        {
            if (folder.LegacyRelativePath.Length == 0 || !DevbookChapterKey.StartsWithSegment(key, folder.LegacyRelativePath)) continue;

            var location = folders.Resolve(folder.Key, alias);
            if (!location.Available || string.IsNullOrWhiteSpace(location.FullPath)) return new NoteChapter(path, null);

            var within = key[(folder.LegacyRelativePath.Length + 1)..];
            var spelled = RepositoryFolder(location.FullPath, location.RootPath);

            return new NoteChapter(
                spelled.Length == 0 ? within : $"{spelled}/{within}",
                NoteChapter.Headings(ChapterPaths.ResolveWithin(location.FullPath, within)));
        }

        var root = folders.Resolve(InstructionsKey, alias);

        return new NoteChapter(
            key,
            root.Available && !string.IsNullOrWhiteSpace(root.FullPath)
                ? NoteChapter.Headings(ChapterPaths.ResolveWithin(root.FullPath, key))
                : null);
    }

    /// <summary>
    /// The repository a call names, or the failure that ends it: the Brain, no
    /// repository, one this host does not know, and — for a write — one that is
    /// archived.
    /// <para>
    /// The Brain first and by name, before the directory is asked, for the reason
    /// <see cref="BrainMcpScope"/> gives: no registration may shadow it. It is
    /// refused because it has no proposals, records or pull requests — its notes
    /// are in force as written. Archived second, before the subject is read, as
    /// <c>resolve_annotation</c> asks it: an archived repository's devbook is
    /// read-only (local ADR 0029 §4), and the refusal is the same whichever
    /// subject was sent.
    /// </para>
    /// </summary>
    private TasksRepositoryRef Scope(string repository, bool writes)
    {
        if (BrainMcpScope.Is(repository))
        {
            throw RepositoryScope.Failure(Error.Validation(
                BrainCode,
                $"The Brain ({BrainMcpScope.Value}) keeps no workflow state: its notes have no proposals, records or "
                + "pull requests. Name a repository in owner/name form."));
        }

        var scope = RepositoryScope.Resolve(repositories, repository).ValueOrThrow();

        if (writes && scope.IsArchived)
        {
            throw RepositoryScope.Failure(Error.Validation(
                DevbookTools.ArchivedCode,
                $"{scope.Id} is archived, so its devbook is read-only and its subjects' states are not set. "
                + "Unarchive it in Backlog's Settings first."));
        }

        return scope;
    }

    /// <summary>
    /// The subject an argument names, or the <c>bad-state</c> answer saying why
    /// it names none.
    /// <para>
    /// Checked before a <see cref="DevbookSubject"/> is built, because that
    /// constructor throws on what it cannot key, and a thrown argument reaches the
    /// caller as the SDK's "an error occurred". Stricter than the constructor in
    /// three places, each to keep one subject from being filed under two names: a
    /// kind takes only its own keys, a proposal's name is a folder name and not a
    /// path, and a record's path has to be one the panels read as a record of that
    /// kind (<see cref="DevbookSubjects.Classify"/>, through the repository's own
    /// folder settings) — otherwise a state set here would sit where no panel
    /// draws it.
    /// </para>
    /// <para>
    /// The record's key is then not the path as sent but the path as the
    /// repository spells it (<see cref="RecordPath"/>). The classification is
    /// forgiving — any case, and the legacy <c>.arc42/…</c> layout in a repository
    /// that moved to <c>.devbook/</c> — while the key is compared ordinally, so
    /// keying the path as sent would file one record under as many names as it has
    /// spellings, every one but the panel's out of sight.
    /// </para>
    /// </summary>
    private bool TryName(
        TasksRepositoryRef scope,
        DevbookSubjectArgument? argument,
        [NotNullWhen(true)] out DevbookSubject? subject,
        [NotNullWhen(false)] out DevbookStatePayload? refused)
    {
        subject = null;
        refused = null;

        if (WhyNoSubject(scope, argument, out var kind, out var key) is { } why)
        {
            refused = DevbookStatePayload.Refusal(DevbookSubjectRefusal.BadState, why);
            return false;
        }

        subject = new DevbookSubject(kind, key, argument!.PullRequest);

        return true;
    }

    /// <summary>Why an argument names no subject, or null with the kind it names
    /// and the key it is filed under.</summary>
    private string? WhyNoSubject(TasksRepositoryRef scope, DevbookSubjectArgument? argument, out DevbookSubjectKind kind, out string key)
    {
        kind = default;
        key = string.Empty;

        if (argument is null)
        {
            return "A subject is required. " + SubjectDescription;
        }

        if (!DevbookSubjects.TryParseKind(argument.Kind, out kind))
        {
            return $"'{argument.Kind}' is no subject kind; a subject is a proposal, a decision or a debt.";
        }

        if (kind == DevbookSubjectKind.Proposal)
        {
            if (string.IsNullOrWhiteSpace(argument.Name))
            {
                return "A proposal is named by its change's folder name under openspec/changes/.";
            }

            if (!DevbookSubject.IsProposalName(argument.Name))
            {
                return $"'{argument.Name}' is no change folder name; a proposal is named by its folder under "
                    + "openspec/changes/ alone — no path, no '..', not archive — e.g. add-decision-tools.";
            }

            if (argument.Path is not null || argument.PullRequest is not null)
            {
                return "A proposal is named by name alone; path and pullRequest belong to a record.";
            }

            key = argument.Name;
            return null;
        }

        var record = kind == DevbookSubjectKind.Decision ? "decision record" : "debt record";

        if (string.IsNullOrWhiteSpace(argument.Path))
        {
            return $"A {record} is named by its repository path.";
        }

        if (argument.Name is not null)
        {
            return $"A {record} is named by its path; name belongs to a proposal.";
        }

        if (kind == DevbookSubjectKind.Decision && argument.PullRequest is not > 0)
        {
            return "A decision record is keyed by the pull request it is decided in too: pullRequest, a number above 0.";
        }

        if (kind == DevbookSubjectKind.Debt && argument.PullRequest is not null)
        {
            return "A debt record is keyed by its path alone; pullRequest belongs to a decision record.";
        }

        var canonical = DevbookChapterKey.Canonical(argument.Path, folders.Folders(scope.Alias));

        if (DevbookSubjects.Classify(canonical) != kind)
        {
            return kind == DevbookSubjectKind.Decision
                ? $"'{argument.Path}' is not a decision record: one is a Markdown file directly in the architecture "
                    + "folder's adr/, never under adr/guidelines/ and never its README or index."
                : $"'{argument.Path}' is not a debt record: one is a Markdown file directly in the architecture "
                    + "folder's tdr/, never its README or index.";
        }

        key = RecordPath(scope.Alias, argument.Path, canonical, kind);
        return null;
    }

    /// <summary>
    /// A record's path as the repository spells it, which is what the arc42 panel
    /// keys a record's state by (its document path): the architecture folder where
    /// this repository actually has it, then <c>adr/</c> or <c>tdr/</c>, then the
    /// file name as the folder holds it — whatever case and layout the caller
    /// used.
    /// <para>
    /// The folder's spelling is the resolution's, measured from the repository
    /// root as the panel's reader measures it, and only its last segment when the
    /// folder is configured off the clone, where the reader spells it that way
    /// too. The file name is the one on disk when the folder holds a file of that
    /// name in any case; a record on no branch this machine has read keeps the
    /// name it was sent with. A repository whose architecture folder this machine
    /// cannot resolve at all is keyed by the path as sent: no panel here can draw
    /// it either, and there is no spelling to correct it to.
    /// </para>
    /// </summary>
    private string RecordPath(string alias, string sent, string canonical, DevbookSubjectKind kind)
    {
        var location = folders.Resolve(Arc42Key, alias);
        if (!location.Available || string.IsNullOrWhiteSpace(location.FullPath)) return DevbookChapterKey.Normalize(sent);

        var segment = kind == DevbookSubjectKind.Decision ? "adr" : "tdr";
        var fileName = canonical[(canonical.LastIndexOf('/') + 1)..];
        var directory = Path.Combine(location.FullPath, segment);
        var tree = folders.FileTree(location);

        if (tree.DirectoryExists(directory))
        {
            fileName = tree.EnumerateFiles(directory, "*.md")
                .Select(file => Path.GetFileName(file))
                .FirstOrDefault(held => string.Equals(held, fileName, StringComparison.OrdinalIgnoreCase))
                ?? fileName;
        }

        var folder = RepositoryFolder(location.FullPath, location.RootPath);

        return folder.Length == 0 ? $"{segment}/{fileName}" : $"{folder}/{segment}/{fileName}";
    }

    /// <summary>The folder as the arc42 reader spells its documents: relative to
    /// the repository root when the folder is inside it, otherwise its last
    /// segment (<c>Arc42DevbookStore.ResolveRoot</c>'s rule).</summary>
    private static string RepositoryFolder(string fullPath, string? rootPath)
    {
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath));

        if (!string.IsNullOrWhiteSpace(rootPath))
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));

            if (folder.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return DevbookChapterKey.Normalize(Path.GetRelativePath(root, folder)).TrimEnd('/');
            }
        }

        return Path.GetFileName(folder);
    }

    /// <summary>
    /// A chapter's path as the note tools answer it, and its headings — each the
    /// block index the parse gives it, its level and its anchor — or null when
    /// this machine holds no file to read them from.
    /// </summary>
    private sealed record NoteChapter(string Path, IReadOnlyList<NoteHeading>? Sections)
    {
        /// <summary>
        /// The address a note on one block has: <c>&lt;path&gt;#&lt;slug&gt;</c> of
        /// the nearest heading at or above it, or the chapter's path when that is
        /// its title — a level-1 heading is the chapter, not a section of it — when
        /// no heading is above it, or when the chapter could not be read.
        /// </summary>
        public string AddressOf(int blockIndex) =>
            Sections?.LastOrDefault(heading => heading.Index <= blockIndex) is { Level: > 1 } section
                ? $"{Path}#{section.Slug}"
                : Path;

        /// <summary>The block of the first heading with this anchor, or null when
        /// the chapter has none or could not be read.</summary>
        public int? IndexOf(string slug) =>
            Sections?.FirstOrDefault(heading => string.Equals(heading.Slug, slug, StringComparison.Ordinal))?.Index;

        /// <summary>
        /// The headings of a chapter file, numbered as every reader here numbers
        /// blocks (<see cref="MarkdownPreview.ParseDocument"/> over the whole
        /// file), or null when there is no such Markdown file or it cannot be
        /// read.
        /// </summary>
        public static IReadOnlyList<NoteHeading>? Headings(string? fullPath)
        {
            if (fullPath is null
                || !fullPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(fullPath))
            {
                return null;
            }

            string text;

            try
            {
                text = File.ReadAllText(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var blocks = MarkdownPreview.ParseDocument(text);
            var headings = new List<NoteHeading>();

            for (var index = 0; index < blocks.Count; index++)
            {
                if (blocks[index] is not MdHeading { Source.IsKnown: true } heading) continue;
                if (heading.Source.StartLine >= lines.Length) continue;

                headings.Add(new NoteHeading(index, heading.Level, Slug(lines[heading.Source.StartLine])));
            }

            return headings;
        }

        /// <summary>
        /// A heading line's anchor, by the devbook checker's rule — GitHub's:
        /// the text after the hashes, lower case and trimmed, with everything but
        /// letters, numbers, underscores, whitespace and hyphens dropped and each
        /// whitespace character turned into a hyphen. Taken from the line as
        /// written, as the checker takes it, rather than from the parse.
        /// </summary>
        private static string Slug(string headingLine)
        {
            var text = headingLine.TrimStart().TrimStart('#').ToLowerInvariant().Trim();
            var slug = new StringBuilder(text.Length);

            foreach (var character in text)
            {
                if (char.IsWhiteSpace(character)) slug.Append('-');
                else if (char.IsLetter(character) || char.IsNumber(character) || character is '_' or '-') slug.Append(character);
            }

            return slug.ToString();
        }
    }

    /// <summary>One heading of a chapter: its block index, level and
    /// anchor.</summary>
    private sealed record NoteHeading(int Index, int Level, string Slug);
}
