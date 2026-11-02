using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Mcp;

// The arguments and answers of DecisionTools, in a file of their own rather than
// in Payloads.cs: they are devbook.decision@1's shapes, owned by that contract,
// and keeping them apart keeps a change to the contract out of the file every
// other group's payloads share.

/// <summary>
/// A subject as <c>devbook.decision@1</c> names one on the wire:
/// <c>{ kind, name }</c> for a proposal, <c>{ kind, path }</c> for a debt record,
/// <c>{ kind, path, pullRequest }</c> for a decision record.
/// <para>
/// One shape with every key optional but the kind, rather than one shape per
/// kind, because that is how the contract writes it and how a caller composes
/// it; which keys a kind takes is <see cref="DecisionTools"/>' to check, and a
/// subject that does not fit is answered <c>bad-state</c> rather than failing in
/// the SDK's binding, whose message names nothing.
/// </para>
/// </summary>
public sealed record DevbookSubjectArgument(
    [property: Description("proposal, decision or debt.")]
    string Kind,
    [property: Description("A proposal's change folder name under openspec/changes/, e.g. add-decision-tools. A proposal only.")]
    string? Name = null,
    [property: Description(
        "A decision or debt record's repository path, e.g. .devbook/arc42/adr/0044-x.md or .devbook/arc42/tdr/0003-x.md. "
        + "A decision or debt record only.")]
    string? Path = null,
    [property: Description("The pull request a decision record is decided in. A decision record only, and required for one.")]
    int? PullRequest = null);

/// <summary>
/// One person's decision on a proposal as the contract answers it:
/// <c>{ by, at, fingerprint }</c>, <c>at</c> in ISO 8601 UTC.
/// </summary>
public sealed record DevbookDecisionPayload(string By, string At, string Fingerprint)
{
    /// <summary>The store's record as the wire spells it, or null for none.</summary>
    internal static DevbookDecisionPayload? Of(DevbookSubjectDecision? decision) =>
        decision is null ? null : new(decision.By, Utc(decision.At), decision.Fingerprint);

    /// <summary>
    /// ISO 8601 with a <c>Z</c>, and no fraction when there is none.
    /// <para>
    /// Spelled here rather than left to the serializer, whose
    /// <see cref="DateTimeOffset"/> carries an offset — <c>+00:00</c> at best, and
    /// the machine's own at worst when a stamp came off a local clock. The
    /// contract says UTC, so the answer says it the one way nobody misreads.
    /// </para>
    /// </summary>
    internal static string Utc(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
}

/// <summary>
/// What every one of the four state operations answers: the subject's state, as
/// <c>devbook_get_state</c> answers it, or a refusal.
/// <para>
/// <b>Three shapes, one type.</b> The contract answers <c>{ state }</c> for a
/// decision or debt record, <c>{ state, approval, acceptance }</c> for a proposal
/// — both decisions present, each an object or <c>null</c> — and
/// <c>{ refused, message }</c> for a refusal. Which keys appear is part of the
/// answer, and a property that is null is not a property that is absent, so
/// <see cref="DevbookStatePayloadConverter"/> writes the shape rather than the
/// serializer's habits deciding it. The properties are here for the code and the
/// tests that read an answer without parsing it.
/// </para>
/// </summary>
[JsonConverter(typeof(DevbookStatePayloadConverter))]
public sealed record DevbookStatePayload
{
    /// <summary>The subject's state, or null when the host keeps none.</summary>
    public string? State { get; init; }

    /// <summary>Whether the subject is a proposal, whose answer carries
    /// <see cref="Approval"/> and <see cref="Acceptance"/> even when both are
    /// null.</summary>
    public bool CarriesDecisions { get; init; }

    public DevbookDecisionPayload? Approval { get; init; }

    public DevbookDecisionPayload? Acceptance { get; init; }

    /// <summary>The contract's refusal code — <c>open-question</c>,
    /// <c>not-approved</c>, <c>fingerprint-mismatch</c> or <c>bad-state</c> — or
    /// null for an answer.</summary>
    public string? Refused { get; init; }

    public string? Message { get; init; }

    public bool IsRefused => Refused is not null;

    /// <summary>A subject's state as the store keeps it, or none.</summary>
    internal static DevbookStatePayload Of(DevbookSubject subject, DevbookSubjectState? state) => new()
    {
        State = state?.State,
        CarriesDecisions = subject.Kind == DevbookSubjectKind.Proposal,
        Approval = DevbookDecisionPayload.Of(state?.Approval),
        Acceptance = DevbookDecisionPayload.Of(state?.Acceptance)
    };

    internal static DevbookStatePayload Refusal(DevbookSubjectRefusal refusal, string message) =>
        new() { Refused = Code(refusal), Message = message };

    /// <summary>
    /// What an operation came to, as the caller reads it: the state it left, as
    /// <c>devbook_get_state</c> answers it, or the contract's refusal.
    /// <para>
    /// An archived repository is no refusal of the contract's, so it is never
    /// put in <c>{ refused }</c>: it is thrown as <c>repository.archived</c>, the
    /// failure <see cref="DecisionTools"/> already answers before it reaches a store
    /// and every other devbook tool answers too. Mapped here as well so a store or
    /// a path that answers it as an outcome reads the same over MCP.
    /// </para>
    /// </summary>
    internal static DevbookStatePayload Of(DevbookSubject subject, DevbookSubjectOutcome outcome)
    {
        if (outcome.RepositoryArchived)
        {
            throw RepositoryScope.Failure(Error.Validation(
                DevbookTools.ArchivedCode,
                outcome.Message ?? "The repository is archived, so its devbook is read-only."));
        }

        return outcome.Refused is { } refusal
            ? Refusal(refusal, outcome.Message ?? Code(refusal))
            : Of(subject, outcome.State);
    }

    /// <summary>A refusal's wire code, as <c>devbook.decision@1</c> spells
    /// it.</summary>
    internal static string Code(DevbookSubjectRefusal refusal) => refusal switch
    {
        DevbookSubjectRefusal.OpenQuestion => "open-question",
        DevbookSubjectRefusal.NotApproved => "not-approved",
        DevbookSubjectRefusal.FingerprintMismatch => "fingerprint-mismatch",
        DevbookSubjectRefusal.BadState => "bad-state",
    };
}

/// <summary>
/// Writes a <see cref="DevbookStatePayload"/> in the contract's shape, with the
/// contract's key names whatever naming policy the options carry: those names are
/// <c>devbook.decision@1</c>'s, matched by callers this repository does not ship.
/// An answer only — nothing reads one back.
/// </summary>
internal sealed class DevbookStatePayloadConverter : JsonConverter<DevbookStatePayload>
{
    public override DevbookStatePayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("A devbook state answer is written, never read back.");

    public override void Write(Utf8JsonWriter writer, DevbookStatePayload value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        if (value.IsRefused)
        {
            writer.WriteString("refused", value.Refused);
            writer.WriteString("message", value.Message);
        }
        else
        {
            writer.WriteString("state", value.State);

            if (value.CarriesDecisions)
            {
                WriteDecision(writer, "approval", value.Approval);
                WriteDecision(writer, "acceptance", value.Acceptance);
            }
        }

        writer.WriteEndObject();
    }

    private static void WriteDecision(Utf8JsonWriter writer, string name, DevbookDecisionPayload? decision)
    {
        if (decision is null)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteStartObject(name);
        writer.WriteString("by", decision.By);
        writer.WriteString("at", decision.At);
        writer.WriteString("fingerprint", decision.Fingerprint);
        writer.WriteEndObject();
    }
}

/// <summary>
/// One reply under a note, as <c>devbook.decision@1</c> spells it:
/// <c>{ by, at, body }</c>.
/// <para>
/// Backlog's remark has no thread, so a reply is kept folded into its note's
/// body (<see cref="DevbookNoteReplies"/>) — by <see cref="DecisionTools.PostNoteOn"/>
/// and by the one-time import alike — and read back out into these when the note
/// is answered.
/// </para>
/// </summary>
public sealed record DevbookNoteReplyPayload(
    [property: JsonPropertyName("by")] string By,
    [property: JsonPropertyName("at")] string At,
    [property: JsonPropertyName("body")] string Body);

/// <summary>
/// One note as <c>devbook.decision@1</c> answers it:
/// <c>{ id, address, kind, status, body, by, at, replies }</c>.
/// <para>
/// The names are pinned with <see cref="JsonPropertyNameAttribute"/> rather than
/// left to the options' naming policy, for the reason
/// <see cref="DevbookStatePayloadConverter"/> gives: they are the contract's,
/// matched by callers this repository does not ship.
/// </para>
/// </summary>
/// <param name="Id">The remark's id.</param>
/// <param name="Address">Where the note is: <c>&lt;path&gt;</c>, or
/// <c>&lt;path&gt;#&lt;slug&gt;</c> for a note inside a section.</param>
/// <param name="Kind"><c>comment</c>, <c>question</c>, <c>suggestion</c> or
/// <c>flag</c>.</param>
/// <param name="Status"><c>open</c> or <c>resolved</c>.</param>
/// <param name="At">When it was posted, ISO 8601 UTC.</param>
public sealed record DevbookNotePayload(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("by")] string By,
    [property: JsonPropertyName("at")] string At,
    [property: JsonPropertyName("replies")] IReadOnlyList<DevbookNoteReplyPayload> Replies)
{
    internal const string Open = "open";
    internal const string Resolved = "resolved";

    /// <summary>A remark as the contract answers it, at the address it is at:
    /// the stored body split into the note's own text and the replies folded
    /// below it.</summary>
    internal static DevbookNotePayload Of(DevbookAnnotation remark, string address)
    {
        var (body, replies) = DevbookNoteReplies.Split(remark.Body);

        return new(
            remark.Id.ToString(),
            address,
            DevbookAnnotationKinds.Name(remark.Kind),
            remark.Resolved ? Resolved : Open,
            body,
            remark.Author,
            DevbookDecisionPayload.Utc(remark.CreatedAt),
            [.. replies.Select(reply => new DevbookNoteReplyPayload(reply.By, reply.At, reply.Body))]);
    }
}

/// <summary><c>devbook_list_notes</c>' answer: <c>{ notes }</c>.</summary>
public sealed record DevbookNotesPayload(
    [property: JsonPropertyName("notes")] IReadOnlyList<DevbookNotePayload> Notes);

/// <summary>
/// What <c>devbook_post_note</c> and <c>devbook_resolve_note</c> answer: the note,
/// or <c>{ refused, message }</c>.
/// <para>
/// One type with a converter, as <see cref="DevbookStatePayload"/> is, because
/// which keys appear is part of the answer: a note is its eight keys and nothing
/// else, a refusal its two.
/// </para>
/// </summary>
[JsonConverter(typeof(DevbookNoteAnswerConverter))]
public sealed record DevbookNoteAnswer
{
    /// <summary>The refusal a note id this host does not keep — in this
    /// repository, on the default branch — is answered with.</summary>
    internal const string UnknownNote = "unknown-note";

    public DevbookNotePayload? Note { get; init; }

    /// <summary>The contract's refusal code, or null for an answer.</summary>
    public string? Refused { get; init; }

    public string? Message { get; init; }

    public bool IsRefused => Refused is not null;

    internal static DevbookNoteAnswer Of(DevbookNotePayload note) => new() { Note = note };

    internal static DevbookNoteAnswer Unknown(string? id) => new()
    {
        Refused = UnknownNote,
        Message = $"No note with id '{id}' in this repository. Ask devbook_list_notes for the address's notes and use an id from there."
    };
}

/// <summary>Writes a <see cref="DevbookNoteAnswer"/> as the note itself or as
/// <c>{ refused, message }</c>. An answer only — nothing reads one back.</summary>
internal sealed class DevbookNoteAnswerConverter : JsonConverter<DevbookNoteAnswer>
{
    public override DevbookNoteAnswer Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("A devbook note answer is written, never read back.");

    public override void Write(Utf8JsonWriter writer, DevbookNoteAnswer value, JsonSerializerOptions options)
    {
        if (value.Note is { } note && !value.IsRefused)
        {
            JsonSerializer.Serialize(writer, note, options);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("refused", value.Refused);
        writer.WriteString("message", value.Message);
        writer.WriteEndObject();
    }
}
