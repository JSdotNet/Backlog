using System.Security.Cryptography;
using System.Text;

namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// The three kinds of thing in a devbook that have workflow state, per
/// <c>devbook.decision@1</c>. An ordinary chapter is none of them: merged, it is
/// in force, and its state is the branch it is on.
/// </summary>
public enum DevbookSubjectKind
{
    /// <summary>A change proposal under <c>openspec/changes/</c>, keyed by its
    /// folder name. Wire name <c>proposal</c>.</summary>
    Proposal,

    /// <summary>A decision record, keyed by its path and the pull request it is
    /// being decided in. Wire name <c>decision</c>.</summary>
    Decision,

    /// <summary>A technical debt record, keyed by its path. Wire name
    /// <c>debt</c>.</summary>
    Debt
}

/// <summary>
/// One subject whose workflow state Backlog keeps outside git, named the way
/// <c>devbook.decision@1</c> names it.
/// <para>
/// Under devbook's branch-true decision the state of a proposal, a decision
/// record or a debt record no longer lives in the chapter's <c>meta</c> block: a
/// branch states what is true of its code, and "approved", "planned" or
/// "decided" is a fact about the work around the chapter, not about the chapter.
/// So the host that runs the workflow keeps it, keyed by what the contract says a
/// subject is called — and this record is that name, nothing more.
/// </para>
/// <para>
/// A record path is spelled one way here (forward slashes, no anchor, no leading
/// <c>./</c> or <c>/</c>, trimmed) and is otherwise the path in the repository,
/// not <see cref="DevbookChapterKey.Canonical"/>'s conventional-folder key. The
/// contract's caller passes the repository path and is answered in it, and a
/// record's path is the same on every clone; the per-machine folder relocation
/// that key exists for is a reading concern, settled by whichever panel maps its
/// document path to a subject.
/// </para>
/// <para>
/// The constructor refuses a subject the contract could not name — a blank key,
/// a decision record with no pull request, a pull request on anything else, a
/// proposal name that is no folder name (<see cref="IsProposalName"/>) —
/// because a store keyed by a malformed name would file state nobody can ever ask
/// for again. Callers that take a name from outside (the MCP tools, the wire)
/// check it before they build one.
/// </para>
/// </summary>
public sealed record DevbookSubject
{
    private static readonly IReadOnlyList<string> ProposalStates = ["draft", "approved", "accepted"];
    private static readonly IReadOnlyList<string> DecisionStates = ["proposed", "decided", "declined"];
    private static readonly IReadOnlyList<string> DebtStates = ["identified", "planned", "in-progress", "resolved"];

    /// <param name="kind">Which of the contract's three subjects this is.</param>
    /// <param name="key">The change's folder name for a proposal, or the record's
    /// repository path for a decision or debt record.</param>
    /// <param name="pullRequest">The pull request a decision record is decided in;
    /// required for a decision, refused for the other two.</param>
    public DevbookSubject(DevbookSubjectKind kind, string key, int? pullRequest = null)
    {
        var spelled = kind == DevbookSubjectKind.Proposal ? key?.Trim() : DevbookChapterKey.Normalize(key);
        if (string.IsNullOrEmpty(spelled))
        {
            throw new ArgumentException("A subject needs a name or a path.", nameof(key));
        }

        if (kind == DevbookSubjectKind.Proposal && !IsProposalName(spelled))
        {
            throw new ArgumentException(
                "A proposal is named by its change's folder name under openspec/changes/: no path, no '..', not 'archive'.",
                nameof(key));
        }

        if (kind == DevbookSubjectKind.Decision)
        {
            if (pullRequest is null) throw new ArgumentException("A decision record is keyed by its pull request too.", nameof(pullRequest));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pullRequest.Value, nameof(pullRequest));
        }
        else if (pullRequest is not null)
        {
            throw new ArgumentException("Only a decision record is keyed by a pull request.", nameof(pullRequest));
        }

        Kind = kind;
        Key = spelled;
        PullRequest = pullRequest;
    }

    public DevbookSubjectKind Kind { get; }

    /// <summary>The change's folder name, or the record's repository path.</summary>
    public string Key { get; }

    /// <summary>The pull request a decision record is decided in; null for the
    /// other two kinds.</summary>
    public int? PullRequest { get; }

    /// <summary>A change proposal, by its folder name under
    /// <c>openspec/changes/</c>.</summary>
    public static DevbookSubject Proposal(string name) => new(DevbookSubjectKind.Proposal, name);

    /// <summary>A decision record, by its repository path and the pull request
    /// it is being decided in. A record decided again in a later pull request is
    /// another subject, which is what keeps one review's verdict from standing
    /// in for the next.</summary>
    public static DevbookSubject Decision(string path, int pullRequest) => new(DevbookSubjectKind.Decision, path, pullRequest);

    /// <summary>A technical debt record, by its repository path.</summary>
    public static DevbookSubject Debt(string path) => new(DevbookSubjectKind.Debt, path);

    /// <summary>The subject's one name as a string: <c>proposal:&lt;name&gt;</c>,
    /// <c>decision:&lt;path&gt;#&lt;pr&gt;</c> or <c>debt:&lt;path&gt;</c>. What
    /// <see cref="IdFor"/> digests.</summary>
    public string CanonicalKey => Kind switch
    {
        DevbookSubjectKind.Proposal => $"proposal:{Key}",
        DevbookSubjectKind.Decision => $"decision:{Key}#{PullRequest}",
        DevbookSubjectKind.Debt => $"debt:{Key}",
    };

    /// <summary>The states this subject's kind can be in.</summary>
    public IReadOnlyList<string> States => StatesOf(Kind);

    /// <summary>The states the contract closes for a kind, in workflow order.
    /// Closed within <c>@1</c>: a state outside its row is refused, never
    /// stored.</summary>
    public static IReadOnlyList<string> StatesOf(DevbookSubjectKind kind) => kind switch
    {
        DevbookSubjectKind.Proposal => ProposalStates,
        DevbookSubjectKind.Decision => DecisionStates,
        DevbookSubjectKind.Debt => DebtStates,
    };

    /// <summary>
    /// The id a subject's state document has in one repository, the same on every
    /// device: the first sixteen bytes of SHA-256 over the UTF-8 of
    /// <c>&lt;alias&gt;\n&lt;canonical key&gt;</c>, the alias as
    /// <see cref="AliasKey"/> folds it, with the version and variant
    /// bits of a name-based UUID set.
    /// <para>
    /// Derived rather than drawn because two devices that each set a state on the
    /// same subject before either has synced have to land on one replica document;
    /// with a fresh Guid each, both would survive the merge and the subject would
    /// have two states with nothing to say which is current. An unscoped subject —
    /// no alias — is filed under the empty alias, as a remark is.
    /// </para>
    /// </summary>
    public static Guid IdFor(string? repositoryAlias, DevbookSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(AliasKey(repositoryAlias) + "\n" + subject.CanonicalKey));

        var bytes = digest.AsSpan(0, 16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>
    /// A repository alias as the subject state is keyed by it: trimmed and
    /// lower-cased, and the empty alias for an unscoped subject.
    /// <para>
    /// The one place an alias is folded, and every store, the rules and
    /// <see cref="IdFor"/> ask it. Case-insensitive because an alias already is
    /// wherever a store looks one up — the file store's map always matched it that
    /// way — and a digest that did not fold while the lookup did would file one
    /// subject under two ids in one file: approved as <c>JSdotNet/Backlog</c>,
    /// then refused <c>not-approved</c> when accepted as <c>jsdotnet/backlog</c>.
    /// The folded spelling is also what a state records, so the record and its id
    /// can never disagree about which repository they belong to.
    /// </para>
    /// </summary>
    public static string AliasKey(string? repositoryAlias) =>
        string.IsNullOrWhiteSpace(repositoryAlias) ? string.Empty : repositoryAlias.Trim().ToLowerInvariant();

    /// <summary>
    /// Whether a name can be a change's folder name under
    /// <c>openspec/changes/</c>: not blank, no separator or drive colon, not <c>.</c>, no <c>..</c>, and not
    /// <c>archive</c>, the folder finished changes move into.
    /// <para>
    /// Refused here rather than left to whoever reads the folder, because the same
    /// name keys the state and is read off disk to fingerprint the change: a name
    /// that climbs out of <c>openspec/changes/</c> would key a subject the contract
    /// cannot name and read a file that is not the change's.
    /// </para>
    /// </summary>
    public static bool IsProposalName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.IndexOfAny(['/', '\\', ':']) < 0
        && name.Trim() != "."
        && !name.Contains("..", StringComparison.Ordinal)
        && !string.Equals(name.Trim(), "archive", StringComparison.OrdinalIgnoreCase);
}
