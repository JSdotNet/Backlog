using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel.Markdown;
using Backlog.SharedKernel.Metadata;
using Backlog.UI.Components.Devbook;
using Backlog.UI.Components.Markdown;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// What a repository's default branch still writes into its Markdown that Backlog
/// now keeps in its own stores, read once into a plan: the workflow state of each
/// subject, from its <c>meta</c> block, and each open <c>annotation</c> fence, as a
/// remark.
/// <para>
/// It writes nothing anywhere and asks no network and no store; the files come
/// through <see cref="DevbookImportFiles"/>, the adapter that opens them, since a
/// module screen opens none itself. The two things it cannot know are left to the
/// caller, said so on each planned row: the pull request a decision record is keyed
/// by (<see cref="DevbookImportedSubject.NeedsPullRequest"/>) and the commit time a
/// row with no authored time is stamped with
/// (<see cref="DevbookImportedSubject.NeedsCommitTime"/>,
/// <see cref="DevbookImportedNote.NeedsCommitTime"/>). Anything it reads and will not
/// import comes back in <see cref="DevbookImportPlan.Skipped"/> with the reason, as
/// a result rather than an exception (guideline ADR 0004).
/// </para>
/// <para>
/// It reads with the parsers the app already reads with — the file-level
/// <c>meta</c> block through <see cref="MarkdownFileMetadata"/>, the fences through
/// <see cref="DevbookAnnotationFence"/>, the blocks through
/// <see cref="MarkdownPreview.ParseDocument"/> and their anchor through
/// <see cref="MdBlockDigest"/> — so a remark lands on the block a read view draws
/// at that index, and the digest re-anchors it once the fences are gone.
/// </para>
/// </summary>
public static class DevbookMarkdownImport
{
    /// <summary>Where a repository's change proposals live, one folder each.</summary>
    public const string ChangesFolder = "openspec/changes";

    /// <summary>Who an imported state is set by when no approval or acceptance
    /// record names anyone: the import, which says where the state came from, and
    /// never this host's name — nobody on this machine set it.</summary>
    public const string ImportAuthor = "devbook import";

    /// <summary>Who a folded fence reply is by when the fence names
    /// nobody.</summary>
    public const string UnknownReplyAuthor = "unknown";

    /// <summary>The folder finished changes move into, which imports nothing.</summary>
    public const string ArchiveFolder = "archive";

    /// <summary>The file a change's state is written in.</summary>
    public const string ProposalFile = "proposal.md";

    private const string Resting = "active";
    private const string Proposed = "proposed";
    private const string Approved = "approved";
    private const string Accepted = "accepted";

    /// <summary>
    /// What the import reads out of a branch snapshot, in
    /// <c>IDevbookSnapshotCache.EnsureAsync</c>'s selection language: each
    /// configured devbook folder in both layouts — <c>.devbook/&lt;name&gt;</c> and
    /// the legacy root <c>.&lt;name&gt;</c> — and the change folders, but nothing
    /// under an <c>archive</c> folder, and none of the rendered diagram artifacts the
    /// area stores leave out too: the import reads Markdown only.
    /// </summary>
    public static IReadOnlyList<string> Selection(IReadOnlyList<DevbookFolderSetting>? folders) =>
    [
        .. FolderPaths(folders).Select(DevbookSnapshotSelection.Subtree),
        DevbookSnapshotSelection.Subtree(ChangesFolder),
        DevbookSnapshotSelection.Exclude(ArchiveFolder),
        DevbookSnapshotSelection.Exclude(DevbookSnapshotSelection.RenderedArtifactsFolder),
    ];

    /// <summary>
    /// Reads a repository's tree at <paramref name="root"/> into the plan the
    /// import writes from.
    /// </summary>
    /// <param name="repositoryAlias">The repository every planned row is keyed
    /// by.</param>
    /// <param name="root">The repository's root on disk — the default branch's
    /// snapshot.</param>
    /// <param name="folders">The repository's configured devbook folders; null or
    /// empty reads the conventional ones.</param>
    public static DevbookImportPlan Plan(string? repositoryAlias, string root, IReadOnlyList<DevbookFolderSetting>? folders)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var configured = DevbookFolderSetting.Normalize(folders);
        var subjects = new List<DevbookImportedSubject>();
        var notes = new List<DevbookImportedNote>();
        var skipped = new List<DevbookImportSkip>();

        foreach (var file in DevbookImportFiles.Read(root, FolderPaths(configured).Append(ChangesFolder), ArchiveFolder))
        {
            if (file.Text is not { } text)
            {
                skipped.Add(new DevbookImportSkip(file.Path, $"The file could not be read: {file.Error}"));
                continue;
            }

            PlanSubject(file.Path, text, configured, subjects, skipped);
            PlanNotes(repositoryAlias, file.Path, text, configured, notes, skipped);
        }

        return new DevbookImportPlan(subjects, notes, skipped);
    }

    /// <summary>The repository-relative folders the configured areas can sit in:
    /// every candidate of every enabled area that has a folder of its own inside
    /// the repository. The instructions area has none — its root is the repository
    /// — and a rooted override is somewhere no branch snapshot holds.</summary>
    private static IEnumerable<string> FolderPaths(IReadOnlyList<DevbookFolderSetting>? folders) =>
        DevbookFolderSetting.Normalize(folders)
            .Where(folder => folder.Enabled)
            .SelectMany(folder => folder.CandidatePaths)
            .Select(path => DevbookChapterKey.Normalize(path).TrimEnd('/'))
            .Where(path => path.Length > 0 && !Path.IsPathRooted(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>The subject a file is, if it is one, and the state its file-level
    /// <c>meta</c> block gives it.</summary>
    private static void PlanSubject(
        string path,
        string text,
        IReadOnlyList<DevbookFolderSetting> folders,
        List<DevbookImportedSubject> subjects,
        List<DevbookImportSkip> skipped)
    {
        var proposal = ProposalName(path);
        var kind = proposal is not null
            ? DevbookSubjectKind.Proposal
            : DevbookSubjects.Classify(DevbookChapterKey.Canonical(path, folders));

        if (kind is not { } subjectKind) return;

        var metadata = MarkdownFileMetadata.Read(text)?.Metadata;
        var status = metadata?.Status?.Trim().ToLowerInvariant();

        // A record that writes no status, or the resting value, is in force and
        // has no workflow state: nothing to import and nothing to report.
        if (string.IsNullOrEmpty(status) || status == Resting) return;

        switch (subjectKind)
        {
            case DevbookSubjectKind.Decision when status != Proposed:
                skipped.Add(new DevbookImportSkip(
                    path,
                    $"'{status}' is no state the import takes for a decision record: only 'proposed' is imported."));
                return;

            case DevbookSubjectKind.Debt when !DevbookSubject.StatesOf(DevbookSubjectKind.Debt).Contains(status, StringComparer.Ordinal):
                skipped.Add(new DevbookImportSkip(
                    path,
                    $"'{status}' is no state of a debt record; it is one of {string.Join(", ", DevbookSubject.StatesOf(DevbookSubjectKind.Debt))}."));
                return;

            case DevbookSubjectKind.Proposal:
                PlanProposal(path, proposal!, status, metadata!, subjects, skipped);
                return;
        }

        subjects.Add(new DevbookImportedSubject(subjectKind, DevbookChapterKey.Normalize(path), path, status, null, null));
    }

    private static void PlanProposal(
        string path,
        string name,
        string status,
        MetadataRecord metadata,
        List<DevbookImportedSubject> subjects,
        List<DevbookImportSkip> skipped)
    {
        if (!DevbookSubject.StatesOf(DevbookSubjectKind.Proposal).Contains(status, StringComparer.Ordinal))
        {
            skipped.Add(new DevbookImportSkip(
                path,
                $"'{status}' is no state of a proposal; it is one of {string.Join(", ", DevbookSubject.StatesOf(DevbookSubjectKind.Proposal))}."));
            return;
        }

        DevbookSubjectDecision? approval = null;
        DevbookSubjectDecision? acceptance = null;

        if (status is Approved or Accepted)
        {
            var state = metadata.State;

            if (Decision(state.ApprovedBy, state.ApprovedAt, state.ApprovedHash) is not { } approved)
            {
                skipped.Add(new DevbookImportSkip(path, Incomplete(status, "approval", state.ApprovedBy, state.ApprovedAt, state.ApprovedHash)));
                return;
            }

            approval = approved;

            if (status == Accepted)
            {
                if (Decision(state.AcceptedBy, state.AcceptedAt, state.AcceptedHash) is not { } accepted)
                {
                    skipped.Add(new DevbookImportSkip(path, Incomplete(status, "acceptance", state.AcceptedBy, state.AcceptedAt, state.AcceptedHash)));
                    return;
                }

                acceptance = accepted;
            }
        }

        subjects.Add(new DevbookImportedSubject(DevbookSubjectKind.Proposal, name, path, status, approval, acceptance));
    }

    /// <summary>A record the repository authored, kept verbatim — the fingerprint
    /// is <c>approved-hash</c> or <c>accepted-hash</c> as written, never
    /// recomputed — or null when any of the three is missing or unreadable.</summary>
    private static DevbookSubjectDecision? Decision(string? by, string? at, string? hash) =>
        string.IsNullOrWhiteSpace(by) || ParseTime(at) is not { } when || !DevbookSubjectRules.IsFingerprint(hash?.Trim())
            ? null
            : new DevbookSubjectDecision(by.Trim(), when, hash!.Trim());

    private static string Incomplete(string status, string record, string? by, string? at, string? hash)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(by)) missing.Add("who");
        if (ParseTime(at) is null) missing.Add("a readable date");
        if (!DevbookSubjectRules.IsFingerprint(hash?.Trim())) missing.Add("a fingerprint");

        return $"'{status}' with an {record} record that lacks {string.Join(", ", missing)}: nothing says what the {record} covers.";
    }

    /// <summary>The change's folder name when <paramref name="path"/> is a change's
    /// <c>proposal.md</c> directly under <c>openspec/changes/&lt;name&gt;/</c>, or
    /// null.</summary>
    private static string? ProposalName(string path)
    {
        var prefix = ChangesFolder + "/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return null;

        var parts = path[prefix.Length..].Split('/');

        return parts.Length == 2
            && string.Equals(parts[1], ProposalFile, StringComparison.OrdinalIgnoreCase)
            && DevbookSubject.IsProposalName(parts[0])
                ? parts[0]
                : null;
    }

    /// <summary>Each open fence in a file, as a remark anchored to the block above
    /// it.</summary>
    private static void PlanNotes(
        string? repositoryAlias,
        string path,
        string text,
        IReadOnlyList<DevbookFolderSetting> folders,
        List<DevbookImportedNote> notes,
        List<DevbookImportSkip> skipped)
    {
        var blocks = MarkdownPreview.ParseDocument(text);
        var chapter = DevbookChapterKey.Canonical(path, folders);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var index = 0; index < blocks.Count; index++)
        {
            if (blocks[index] is not MdCode code || !DevbookAnnotationFence.IsAnnotationBlock(code.Language)) continue;

            var fence = DevbookAnnotationFence.Parse(code.Text);
            if (!fence.IsOpen) continue;

            var kind = DevbookAnnotationKinds.Parse(fence.Kind);
            var identity = Identity(fence, kind);
            var ordinal = seen.GetValueOrDefault(identity);
            seen[identity] = ordinal + 1;

            if (string.IsNullOrWhiteSpace(fence.Body))
            {
                skipped.Add(new DevbookImportSkip(path, $"An open note by {fence.Author ?? "nobody named"} has no body, so there is nothing to keep."));
                continue;
            }

            var anchor = Anchor(blocks, index);

            notes.Add(new DevbookImportedNote(
                NoteId(repositoryAlias, chapter, ordinal, identity),
                chapter,
                path,
                anchor,
                MdBlockDigest.Of(blocks, anchor),
                fence.Body.Replace("\r\n", "\n", StringComparison.Ordinal).Trim(),
                fence.Author ?? string.Empty,
                kind,
                ParseTime(fence.Date),
                fence.Replies));
        }
    }

    /// <summary>The block a fence annotates: the one directly above it, stepping
    /// over the fences stacked there, since consecutive fences all attach to the
    /// same passage. A fence with nothing above it anchors to the top.</summary>
    private static int Anchor(IReadOnlyList<MdBlock> blocks, int fence)
    {
        for (var above = fence - 1; above >= 0; above--)
        {
            if (blocks[above] is MdCode code && DevbookAnnotationFence.IsAnnotationBlock(code.Language)) continue;

            return above;
        }

        return 0;
    }

    /// <summary>
    /// The id an imported note has in one repository, the same on every device: the
    /// first sixteen bytes of SHA-256 over the folded alias, the chapter's canonical
    /// path, the note's ordinal among notes with the same own fields in the chapter
    /// and those fields (<see cref="Identity"/>), with a name-based UUID's version
    /// and variant bits set — the shape <see cref="DevbookSubject.IdFor"/> has.
    /// <para>
    /// Not the block index: a device importing from a later commit, where a
    /// paragraph was added above the note, would otherwise file the same note a
    /// second time. Nor the replies, for the same reason: a reply written into the
    /// fence between two devices' imports is still the same note.
    /// </para>
    /// </summary>
    private static Guid NoteId(string? repositoryAlias, string chapter, int ordinal, string identity)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{DevbookSubject.AliasKey(repositoryAlias)}\n{chapter}\n{ordinal.ToString(CultureInfo.InvariantCulture)}\n{identity}"));

        var bytes = digest.AsSpan(0, 16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>A note's own fields — author, date, kind and body, as written, the
    /// thread under it left out — as one string: what names the note across
    /// commits.</summary>
    private static string Identity(DevbookAnnotationFence fence, DevbookAnnotationKind kind) =>
        string.Join(
            "\n",
            fence.Author?.Trim() ?? string.Empty,
            fence.Date?.Trim() ?? string.Empty,
            DevbookAnnotationKinds.Name(kind),
            fence.Body.Replace("\r\n", "\n", StringComparison.Ordinal).Trim());

    /// <summary>A date (<c>YYYY-MM-DD</c>, midnight UTC) or a timestamp, as
    /// authored; null when it is neither.</summary>
    private static DateTimeOffset? ParseTime(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && DateTimeOffset.TryParse(
            value.Trim().Trim('"', '\''),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
}

/// <summary>What the import plans for one repository: the subject states, the
/// remarks, and what it read and passed over.</summary>
public sealed record DevbookImportPlan(
    IReadOnlyList<DevbookImportedSubject> Subjects,
    IReadOnlyList<DevbookImportedNote> Notes,
    IReadOnlyList<DevbookImportSkip> Skipped);

/// <summary>One file the import read and did not import, and why.</summary>
public sealed record DevbookImportSkip(string Path, string Reason);

/// <summary>
/// One subject's state as the default branch's Markdown writes it.
/// </summary>
/// <param name="Key">The change's folder name, or the record's repository
/// path.</param>
/// <param name="Path">The file the state was read from.</param>
/// <param name="Approval">A proposal's approval, as authored.</param>
/// <param name="Acceptance">A proposal's acceptance, as authored.</param>
public sealed record DevbookImportedSubject(
    DevbookSubjectKind Kind,
    string Key,
    string Path,
    string State,
    DevbookSubjectDecision? Approval,
    DevbookSubjectDecision? Acceptance)
{
    /// <summary>A decision record is keyed by its pull request, which the caller
    /// resolves.</summary>
    public bool NeedsPullRequest => Kind == DevbookSubjectKind.Decision;

    /// <summary>No record says when the state was set, so the caller stamps it with
    /// the commit time it looks up.</summary>
    public bool NeedsCommitTime => Decided is null;

    /// <summary>The record that set the current state: the acceptance of an
    /// accepted proposal, the approval of an approved one, else none.</summary>
    private DevbookSubjectDecision? Decided => Acceptance ?? Approval;

    /// <summary>
    /// The document the store imports: under the derived id, set by whoever the
    /// record names and when, or — with no record — by
    /// <see cref="DevbookMarkdownImport.ImportAuthor"/> at the commit time, stamped
    /// with the same time so any later write by a person wins. Never this host's
    /// name: nobody on this machine set it, and the panel would say "set by You".
    /// </summary>
    /// <exception cref="InvalidOperationException">A state that
    /// <see cref="NeedsCommitTime"/> with none given.</exception>
    public DevbookSubjectState ToState(string? repositoryAlias, DateTimeOffset? committedAt, int? pullRequest = null)
    {
        var subject = new DevbookSubject(Kind, Key, NeedsPullRequest ? pullRequest : null);
        var alias = DevbookSubject.AliasKey(repositoryAlias);
        var by = Decided?.By ?? DevbookMarkdownImport.ImportAuthor;
        var at = Decided?.At ?? committedAt ?? throw new InvalidOperationException($"{Path} has no authored time, and no commit time was given.");

        return new DevbookSubjectState(DevbookSubject.IdFor(alias, subject), alias, subject, State, by, at, Approval, Acceptance, at);
    }
}

/// <summary>
/// One open <c>annotation</c> fence, as the remark it becomes.
/// </summary>
/// <param name="Id">The remark's id, derived so every device files the note
/// alike.</param>
/// <param name="ChapterPath">The chapter's canonical path, as remarks are
/// keyed.</param>
/// <param name="Path">The file the fence was read from.</param>
/// <param name="BlockIndex">The block above the fence, counted the way a read view
/// counts — the fences included.</param>
/// <param name="BlockHash">That block's <see cref="MdBlockDigest"/>.</param>
/// <param name="Body">The note's own text, its replies left out.</param>
/// <param name="AuthoredAt">The fence's <c>date</c>, or null when it has none the
/// import can read.</param>
/// <param name="Replies">The fence's replies, folded below <paramref name="Body"/>
/// when the remark is made (<see cref="DevbookNoteReplies.Fold"/>).</param>
public sealed record DevbookImportedNote(
    Guid Id,
    string ChapterPath,
    string Path,
    int BlockIndex,
    string? BlockHash,
    string Body,
    string Author,
    DevbookAnnotationKind Kind,
    DateTimeOffset? AuthoredAt,
    IReadOnlyList<DevbookAnnotationReply>? Replies = null)
{
    /// <summary>No readable date, so the caller stamps it with the commit time it
    /// looks up.</summary>
    public bool NeedsCommitTime => AuthoredAt is null;

    /// <summary>
    /// The remark the store imports: on the default branch, open, created and
    /// updated when the note was made, its replies folded below its body.
    /// <para>
    /// Every reply is folded in the shape <see cref="DevbookNoteReplies.Split"/>
    /// reads back, so a fence reply that names nobody or no readable date still
    /// answers as a reply: by <see cref="DevbookMarkdownImport.UnknownReplyAuthor"/>,
    /// and on the note's own date — the reply came no earlier.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">A note that
    /// <see cref="NeedsCommitTime"/> with none given.</exception>
    public DevbookAnnotation ToAnnotation(string? repositoryAlias, DateTimeOffset? committedAt)
    {
        var at = AuthoredAt ?? committedAt ?? throw new InvalidOperationException($"A note in {Path} has no date, and no commit time was given.");

        var body = (Replies ?? [])
            .Where(reply => !string.IsNullOrWhiteSpace(reply.Body))
            .Aggregate(Body, (folded, reply) => DevbookNoteReplies.Fold(
                folded,
                ReplyAuthor(reply.Author),
                DevbookNoteReplies.IsTimestamp(reply.Date)
                    ? reply.Date!.Trim()
                    : at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                reply.Body));

        return new DevbookAnnotation(
            Id,
            repositoryAlias ?? string.Empty,
            ChapterPath,
            BlockIndex,
            body,
            Author,
            at,
            at,
            BlockHash: BlockHash,
            Kind: Kind,
            Branch: null);
    }

    /// <summary>A reply's author as a fold can carry it: one line with no
    /// <c>": "</c>, or <see cref="DevbookMarkdownImport.UnknownReplyAuthor"/> when
    /// the fence names none.</summary>
    private static string ReplyAuthor(string? author)
    {
        var line = string.Join(' ', (author ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();

        return line.Length == 0 ? DevbookMarkdownImport.UnknownReplyAuthor : line.Replace(": ", ":", StringComparison.Ordinal);
    }
}
