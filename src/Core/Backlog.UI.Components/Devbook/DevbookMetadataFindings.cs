using Backlog.UI.Components.Metadata;

namespace Backlog.UI.Components.Devbook;

/// <summary>How much a finding matters: a warning is something to tidy, an error
/// is a block the convention's check would refuse.</summary>
public enum DevbookFindingSeverity
{
    Warning,
    Error
}

/// <summary>
/// One thing a metadata block says that the convention reports.
/// </summary>
/// <param name="Severity">Whether the convention warns about it or refuses it.</param>
/// <param name="Field">The field the finding is about, as the file spells it —
/// <c>status</c>, <c>approved-by</c>, <c>kind</c>.</param>
/// <param name="Message">What is wrong and, where there is one, what to do about
/// it. One short sentence, written for the author of the block.</param>
public sealed record DevbookMetadataFinding(DevbookFindingSeverity Severity, string Field, string Message);

/// <summary>
/// What a single metadata block states that contract 16 of the devbook convention
/// reports, judged against the folder and level it was written at.
///
/// <para>The product reads blocks forgivingly — see <see cref="MetadataReader"/> —
/// and that is right for a reader: what a file says is not something a viewer gets
/// to refuse. It is not the same as saying nothing. An explicit <c>status:
/// active</c> where the folder rests by omission, an approval record in a folder
/// with no decision rungs, a <c>tech/</c> block still spelling <c>type</c> as
/// <c>kind</c> — CI's check reports each of those, and an author reading the chapter
/// in the product should see the same report rather than find out from a red
/// build.</para>
///
/// <para>Deliberately one block at a time. The checks that need two blocks — a
/// context map's <c>deployment</c> agreeing with its <c>context.md</c>
/// (<see cref="DevbookSchema.DeploymentDisagrees"/>), an <c>approved-hash</c>
/// against the content it fingerprints, a <c>review</c> verdict against the open
/// annotations under it — need the file or the folder, and belong to whoever
/// holds those.</para>
///
/// <para><c>ext.*</c> keys are never looked at. The convention carries them
/// untouched and unvalidated, and a report about one would be this product reading
/// another plugin's state as schema — which is the one thing the rule forbids.</para>
///
/// <para><c>.backlog</c> and a folder nobody named get no findings at all.
/// <c>.backlog</c> is a legacy folder the product still reads and the contract no
/// longer describes, so there is no rule to report it against; a caller outside the
/// devbook folders has no contract either.</para>
/// </summary>
public static class DevbookMetadataFindings
{
    private const string DomainOnly = "A decision record field is domain/ only, and is not in this folder's vocabulary.";

    /// <summary>
    /// Every finding for one block. Empty for a block with nothing to report, for
    /// <see cref="DevbookFolder.Unknown"/> and for <see cref="DevbookFolder.Backlog"/>.
    /// </summary>
    /// <param name="folder">The folder the block was read from.</param>
    /// <param name="level">Whether it is the file's own block, under the <c>#</c>
    /// title, or a chapter's.</param>
    /// <param name="record">The block, as <see cref="MetadataReader"/> read it.</param>
    /// <param name="fileName">The file it came from, by name or repository path.
    /// Needed for two answers only: a <c>domain/</c> additional page's own
    /// <c>type</c>, and whether a file-level <c>deployment</c> sits on a
    /// <c>context.md</c>. Null leaves both unjudged rather than guessed.</param>
    public static IReadOnlyList<DevbookMetadataFinding> For(
        DevbookFolder folder,
        DevbookMetadataLevel level,
        MetadataRecord record,
        string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (folder is DevbookFolder.Unknown or DevbookFolder.Backlog) return [];

        // Judged as the block states it, not as the surface draws it: a heading
        // drawing the `type` as a mark has taken it out of the record it shows,
        // and without it a bounded-context chapter's `deployment` would read as
        // misplaced.
        record = record.BeforeMark ?? record;

        var findings = new List<DevbookMetadataFinding>();

        Status(findings, folder, record);
        Decision(findings, folder, record);
        Review(findings, record.State);
        Type(findings, folder, level, record, fileName);
        Deployment(findings, level, record, fileName);
        FileLevelOnly(findings, level, record);

        return findings;
    }

    private static void Status(List<DevbookMetadataFinding> findings, DevbookFolder folder, MetadataRecord record)
    {
        var status = record.Status;

        if (DevbookSchema.IsResting(folder, status))
        {
            // One state, one spelling: the resting value is written by leaving the
            // line out, so an explicit one is the second spelling the rule exists
            // to prevent.
            findings.Add(Warn("status", $"“{DevbookSchema.RestingStatus}” is this folder's resting value: omit the field instead of writing it."));
        }

        if (DevbookSchema.RequiresStatus(folder) && string.IsNullOrWhiteSpace(status))
        {
            findings.Add(Error("status", "Required in this folder: an unrated entry is not the bottom rung."));
        }

        if (DevbookSchema.IsDecisionRung(status) && !DevbookSchema.AllowsDecisionRungs(folder))
        {
            findings.Add(Error("status", $"“{status!.Trim()}” is a domain/ decision rung and is not in this folder's vocabulary."));
        }
    }

    private static void Decision(List<DevbookMetadataFinding> findings, DevbookFolder folder, MetadataRecord record)
    {
        var state = record.State;

        if (!DevbookSchema.AllowsDecisionRungs(folder))
        {
            foreach (var field in DevbookSchema.DecisionRecordFields)
            {
                if (Stated(state[field])) findings.Add(Error(field, DomainOnly));
            }

            return;
        }

        var status = record.Status?.Trim();
        var approved = string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase);
        var accepted = string.Equals(status, "accepted", StringComparison.OrdinalIgnoreCase);
        var signed = Stated(state.ApprovedBy) && Stated(state.ApprovedAt);

        if ((approved || accepted) && !signed)
        {
            findings.Add(Error("approved-by", "An approval nobody signed and dated: write approved-by and approved-at with the rung."));
        }

        if (accepted && !(Stated(state.AcceptedBy) && Stated(state.AcceptedAt)))
        {
            findings.Add(Error("accepted-by", "An acceptance nobody signed and dated: write accepted-by and accepted-at with the rung."));
        }

        // An acceptance stands on an approval. Without one there is nothing saying
        // the chapter the build was accepted against was ever agreed.
        if ((accepted || state.HasAcceptance) && !state.HasApproval)
        {
            findings.Add(Error("accepted-by", "An acceptance written over no approval: nothing says the chapter was ever agreed."));
        }

        // Left behind: the rung lapsed and the record did not go with it. Under
        // `accepted` the approval record is not orphaned — the acceptance stands on
        // it — so only a status that is neither rung counts.
        if (!approved && !accepted && state.HasApproval)
        {
            findings.Add(Warn("approved-by", "An approval record left behind on a chapter that no longer claims the rung: delete it with the rung."));
        }

        if (!accepted && state.HasAcceptance)
        {
            findings.Add(Warn("accepted-by", "An acceptance record left behind on a chapter that no longer claims the rung: delete it with the rung."));
        }
    }

    private static void Review(List<DevbookMetadataFinding> findings, MetadataState state)
    {
        if (Stated(state.Review)
            && !DevbookSchema.ReviewStates.Contains(state.Review!.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            findings.Add(Error("review", $"“{state.Review.Trim()}” is not a review state: {string.Join(", ", DevbookSchema.ReviewStates)}."));
        }

        var written = DevbookSchema.ReviewFields.Count(field => Stated(state[field]));
        if (written > 0 && written < DevbookSchema.ReviewFields.Count)
        {
            findings.Add(Warn("review", "review, reviewer and review-at are written together or not at all."));
        }
    }

    private static void Type(
        List<DevbookMetadataFinding> findings,
        DevbookFolder folder,
        DevbookMetadataLevel level,
        MetadataRecord record,
        string? fileName)
    {
        if (string.IsNullOrWhiteSpace(record.Type)) return;

        // Named by the spelling the file used, so the finding points at a line the
        // author can find.
        var field = record.TypeReadFromKind ? DevbookSchema.LegacyTechTypeField : "type";

        if (!DevbookSchema.DefinesTypes(folder))
        {
            findings.Add(Warn(field, "This folder defines no type: omit the field."));
            return;
        }

        if (record.TypeReadFromKind)
        {
            findings.Add(Warn(field, "The old spelling of type: rename it to type."));
        }

        if (!DevbookSchema.IsKnownType(folder, level, record.Type, fileName))
        {
            var where = level == DevbookMetadataLevel.File ? "file" : "chapter";
            findings.Add(Warn(field, $"“{record.Type.Trim()}” is not a {where} type this folder defines."));
        }
    }

    private static void Deployment(
        List<DevbookMetadataFinding> findings,
        DevbookMetadataLevel level,
        MetadataRecord record,
        string? fileName)
    {
        if (string.IsNullOrWhiteSpace(record.Deployment)) return;

        var value = record.Deployment.Trim();
        if (!DevbookSchema.DeploymentValues.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            findings.Add(Error("deployment", $"“{value}” is not a deployment: {string.Join(" or ", DevbookSchema.DeploymentValues)}."));
        }

        if (level == DevbookMetadataLevel.Chapter
            && !string.Equals(record.Type?.Trim(), "bounded-context", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(Warn("deployment", "Only a bounded-context chapter carries it: an aggregate or a feature ships with its context."));
        }

        // Unjudged without a name, rather than guessed: the rule is about which file
        // this is, and a caller that does not know has not said.
        if (level == DevbookMetadataLevel.File
            && DevbookSchema.OwnFileType(fileName) is { } own
            && !string.Equals(own, "context", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(Warn("deployment", "Only a context's context.md carries it at file level."));
        }
    }

    private static void FileLevelOnly(List<DevbookMetadataFinding> findings, DevbookMetadataLevel level, MetadataRecord record)
    {
        if (level == DevbookMetadataLevel.Chapter)
        {
            // A chapter's position is its position in the document; these two place
            // the document in its directory.
            if (record.Number is not null) findings.Add(Warn("number", "File-level only: a chapter's place is its place in the document."));
            if (Stated(record.Index)) findings.Add(Warn("index", "File-level only: a chapter's place is its place in the document."));
        }

        if (Stated(record.Index)
            && !DevbookSchema.IndexValues.Contains(record.Index!.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            findings.Add(Error("index", $"“{record.Index.Trim()}” is not an index value: {string.Join(" or ", DevbookSchema.IndexValues)}."));
        }
    }

    private static bool Stated(string? value) => !string.IsNullOrWhiteSpace(value);

    private static DevbookMetadataFinding Warn(string field, string message) =>
        new(DevbookFindingSeverity.Warning, field, message);

    private static DevbookMetadataFinding Error(string field, string message) =>
        new(DevbookFindingSeverity.Error, field, message);
}
