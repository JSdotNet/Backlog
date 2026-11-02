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
    private static string Utc(DateTimeOffset at) =>
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
