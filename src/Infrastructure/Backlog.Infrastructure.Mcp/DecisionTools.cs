using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The four state operations of <c>devbook.decision@1</c> — read a subject's
/// workflow state, set it, approve a proposal, accept one — answered over
/// Backlog's own subject store.
/// <para>
/// <b>Why the state is here and not in the chapter.</b> Under devbook's
/// branch-true decision a branch states what is true of its code, and "approved",
/// "planned" or "decided" is a fact about the work around a chapter rather than
/// about the chapter, so the host that runs the workflow keeps it
/// (<see cref="IDevbookSubjectStateStore"/>). A caller resolves this group from
/// the live tool list by the <c>devbook_*</c> prefix and binds when all of the
/// contract's operations are listed; the three note operations wait for the
/// annotation kind, so this host answers the state half only.
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
/// <b>Two kinds of no.</b> What the contract refuses — a state outside the row, a
/// decision on anything but a proposal, an acceptance with no approval or at
/// another fingerprint, and a subject it could not name — is an answer, because
/// the contract says it is one and a caller reports it rather than retrying. What
/// is not the contract's question — no repository, one this host does not know,
/// the Brain, an archived repository — is thrown, as every other tool here
/// throws it, because a caller has nothing to report about a subject it never
/// reached.
/// </para>
/// <para>
/// <b>Who and when are the host's.</b> <c>by</c> is
/// <see cref="DevbookLocalAuthor.Name"/> and <c>at</c> the store's clock; neither
/// is an argument, so a caller cannot say somebody else decided. Approve and
/// accept are a person's decision, and their descriptions say so, because a model
/// reads them to decide whether to call.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class DecisionTools(
    IDevbookSubjectStateStore subjects,
    IRepositoryDirectory repositories,
    IDevbookFolderSource folders)
{
    /// <summary>The architecture folder's key, as the arc42 panel resolves
    /// it.</summary>
    private const string Arc42Key = ".arc42";

    internal const string GetState = "devbook_get_state";
    internal const string SetState = "devbook_set_state";
    internal const string Approve = "devbook_approve";
    internal const string Accept = "devbook_accept";

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
    /// rather than four more tools on <see cref="DevbookTools"/> because they are
    /// another port and another contract — that class reads chapters and notes,
    /// these keep the workflow state <c>devbook.decision@1</c> defines.
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
        [GetState, SetState, Approve, Accept]);

    private const string RepositoryDescription =
        "The repository in owner/name form, e.g. JSdotNet/Backlog. The Brain (" + BrainMcpScope.Value
        + ") keeps no workflow state and is refused.";

    private const string SubjectDescription =
        "The subject: { kind: proposal, name } for a change under openspec/changes/; { kind: decision, path, "
        + "pullRequest } for a decision record; { kind: debt, path } for a debt record. A path is the record's "
        + "repository path.";

    private const string FingerprintDescription =
        "The change's devbook.chapter.fingerprint@1, computed with chapter-hash.mjs: sha256: and eight lowercase hex "
        + "characters.";

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

    // Idempotent: setting the state a subject already has leaves it as it was —
    // the stamp moves, the state and the decisions do not. Not destructive by this
    // assembly's line, which only a delete crosses, though draft withdraws a
    // proposal's decisions: the description says so, and a person can decide again.
    [McpServerTool(
        Name = SetState,
        Title = "Set a devbook subject's workflow state",
        ReadOnly = false,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description(
        "Sets a devbook subject's workflow state (devbook.decision@1). A proposal: draft only, which withdraws its "
        + "approval and acceptance; approved and accepted are reached through devbook_approve and devbook_accept. A "
        + "decision record: proposed, decided or declined. A debt record: identified, planned, in-progress or "
        + "resolved. Answers the state as devbook_get_state does, or { refused: bad-state, message } for a state "
        + "outside the subject's row; a refusal writes nothing. Who and when are recorded by Backlog. Refused for an "
        + "archived repository.")]
    public DevbookStatePayload SetStateOf(
        [Description(RepositoryDescription)]
        string repository,
        [Description(SubjectDescription)]
        DevbookSubjectArgument subject,
        [Description("The state to set, from the subject's row.")]
        string state)
    {
        var scope = Scope(repository, writes: true);

        return TryName(scope, subject, out var named, out var refused)
            ? DevbookStatePayload.Of(named, subjects.SetState(scope.Alias, named, state, DevbookLocalAuthor.Name))
            : refused;
    }

    // Not idempotent: each call is a decision of its own, recorded with its own
    // time, and approving again withdraws an acceptance that stood on the earlier
    // approval — so no client may repeat one on its own account.
    [McpServerTool(
        Name = Approve,
        Title = "Approve a change proposal",
        ReadOnly = false,
        Idempotent = false,
        Destructive = false,
        OpenWorld = false)]
    [Description(
        "Records a person's approval of a change proposal at the change's fingerprint (devbook.decision@1): state "
        + "approved, approval { by, at, fingerprint }, any acceptance withdrawn. Call only on that person's explicit "
        + "word in this session — never on a reviewer's verdict or an unattended run's. A proposal alone; anything else "
        + "answers { refused: bad-state, message }, and a refusal writes nothing. Who and when are recorded by "
        + "Backlog. Refused for an archived repository.")]
    public DevbookStatePayload ApproveProposal(
        [Description(RepositoryDescription)]
        string repository,
        [Description(SubjectDescription)]
        DevbookSubjectArgument subject,
        [Description(FingerprintDescription)]
        string fingerprint)
    {
        var scope = Scope(repository, writes: true);

        return TryName(scope, subject, out var named, out var refused)
            ? DevbookStatePayload.Of(named, subjects.Approve(scope.Alias, named, fingerprint, DevbookLocalAuthor.Name))
            : refused;
    }

    // Not idempotent, for approve's reason: an acceptance is a decision recorded
    // with its own time.
    [McpServerTool(
        Name = Accept,
        Title = "Accept a change proposal",
        ReadOnly = false,
        Idempotent = false,
        Destructive = false,
        OpenWorld = false)]
    [Description(
        "Records a person's acceptance of an approved change proposal (devbook.decision@1): state accepted, acceptance "
        + "{ by, at, fingerprint }. It stands on an approval at the same fingerprint: with none it answers "
        + "{ refused: not-approved, message }, with one at another { refused: fingerprint-mismatch, message } — the "
        + "change moved since it was approved, so ask the person to approve it again. Call only on that person's "
        + "explicit word in this session. A proposal alone; a refusal writes nothing. Who and when are recorded by "
        + "Backlog. Refused for an archived repository.")]
    public DevbookStatePayload AcceptProposal(
        [Description(RepositoryDescription)]
        string repository,
        [Description(SubjectDescription)]
        DevbookSubjectArgument subject,
        [Description(FingerprintDescription)]
        string fingerprint)
    {
        var scope = Scope(repository, writes: true);

        return TryName(scope, subject, out var named, out var refused)
            ? DevbookStatePayload.Of(named, subjects.Accept(scope.Alias, named, fingerprint, DevbookLocalAuthor.Name))
            : refused;
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
}
