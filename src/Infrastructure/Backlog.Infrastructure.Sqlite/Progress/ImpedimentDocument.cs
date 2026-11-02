using System.Text.Json;
using System.Text.Json.Serialization;

using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.DomainModels;

namespace Backlog.Infrastructure.Sqlite.Progress;

/// <summary>
/// The stored shape of one <see cref="Impediment"/>: one JSON document per row of
/// <c>progress_impediments</c>, and the <c>impediment</c> document that travels between
/// devices (local ADR 0035 §9). Dates as <c>yyyy-MM-dd</c> and the state by name, the
/// conventions <see cref="GoalDocument"/> keeps.
/// <para>
/// <b>Version 2</b> holds back tasks, pull requests and plans: every item in link order
/// under <c>heldBack</c>, and the task ids under <c>holdsBack</c> as version 1 wrote them,
/// so a version 1 build — which ignores a member it does not know — still reads the tasks.
/// A version 1 build writes <c>version: 1</c> and drops <c>heldBack</c>; <see cref="CarryOver"/>
/// is how the store keeps what it dropped.
/// </para>
/// </summary>
internal sealed record ImpedimentDocument
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>The shape written now: version 1 held back tasks alone.</summary>
    public const int CurrentVersion = 2;

    public int Version { get; init; } = CurrentVersion;

    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? ReportedTo { get; init; }

    public string? HandledBy { get; init; }

    public DateOnly? CheckAgain { get; init; }

    public string? Note { get; init; }

    public List<ImpedimentStateChangeDocument> History { get; init; } = [];

    /// <summary>The ids of the tasks it holds back, in link order — what version 1 reads.</summary>
    public List<Guid> HoldsBack { get; init; } = [];

    /// <summary>Every item it holds back, in link order; null in a version 1 document.</summary>
    public List<HeldBackItemDocument?>? HeldBack { get; init; }

    /// <summary>When the impediment was raised; orders the impediments alike on every device.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Whether a version 1 build wrote it: it knows the held-back tasks alone.</summary>
    private bool KnowsTasksOnly => Version < 2 || HeldBack is null;

    public static ImpedimentDocument From(Impediment impediment) =>
        new ImpedimentDocument
        {
            Id = impediment.Id,
            Title = impediment.Title,
            ReportedTo = impediment.ReportedTo,
            HandledBy = impediment.HandledBy,
            CheckAgain = impediment.CheckAgain,
            Note = impediment.Note,
            History = [.. impediment.History.Select(step => new ImpedimentStateChangeDocument { State = step.State, On = step.On, With = step.With })],
            CreatedAt = impediment.CreatedAt
        }.Holding(impediment.HoldsBack);

    /// <summary>This document at version 2 holding back <paramref name="items"/>: every item
    /// under <c>heldBack</c>, and the tasks again under <c>holdsBack</c> for a version 1 reader.</summary>
    private ImpedimentDocument Holding(IReadOnlyList<HeldBackItem> items) =>
        this with
        {
            Version = CurrentVersion,
            HoldsBack = [.. items.Where(item => item.Kind == HeldBackKind.Task).Select(item => item.TaskId!.Value)],
            HeldBack = [.. items.Select(HeldBackItemDocument.From)],
        };

    /// <summary>The impediment this document holds. Throws <see cref="JsonException"/> for
    /// one that does not read as an impediment — no title, or no step in its history — so
    /// the store refuses it as it refuses text that will not parse. A held-back item of a
    /// kind this build does not know, or without its whole identity, is left out.</summary>
    public Impediment ToImpediment()
    {
        if (string.IsNullOrWhiteSpace(Title)) throw new JsonException("An impediment document has no title.");
        if (History is not { Count: > 0 }) throw new JsonException("An impediment document has no history.");

        return Impediment.Restore(
            Id,
            Title,
            ReportedTo,
            HandledBy,
            CheckAgain,
            Note,
            History.Select(step => new ImpedimentStateChange(step.State, step.On, step.With)),
            Items(),
            CreatedAt,
            History[0].On);
    }

    /// <summary>
    /// What to store when another device's copy <paramref name="inboundJson"/> arrives over
    /// the local one, <paramref name="localJson"/> (local ADR 0035 §9). A version 2 copy is
    /// taken verbatim, as is any copy over a local one that holds back nothing but tasks.
    /// A version 1 copy over one that holds back pull requests or plans keeps them: the
    /// inbound fields and the inbound set of tasks, with the local items in their link order
    /// — a task the inbound copy no longer holds left out, one it newly holds added at the end.
    /// <para>
    /// The store keeps the answer at the inbound stamp, so it is not pushed back as a change
    /// of its own. Pure, and refusing nothing: text that will not read comes back as given.
    /// </para>
    /// <para>
    /// Only a device whose own copy held the pull requests and plans keeps them. Another
    /// version 2 device that never held them stores the version 1 text, at the same stamp, and
    /// an equal stamp is an echo to the replication — so the two read differently until the
    /// impediment is next saved, as does the phone, which reads the feed's version 1 copy.
    /// This is the gap ADR 0035 §9 accepts for mixed versions, not a fault of the merge.
    /// </para>
    /// </summary>
    public static string CarryOver(string? localJson, string inboundJson)
    {
        if (Read(inboundJson) is not { KnowsTasksOnly: true } inbound) return inboundJson;
        if (Read(localJson) is not { } local || local.KnowsTasksOnly) return inboundJson;

        var localItems = local.Items().ToList();
        if (localItems.All(item => item.Kind == HeldBackKind.Task)) return inboundJson;

        var tasks = inbound.Items().ToList();
        List<HeldBackItem> merged =
        [
            .. localItems.Where(item => item.Kind != HeldBackKind.Task || tasks.Contains(item)),
            .. tasks.Where(task => !localItems.Contains(task)),
        ];

        return JsonSerializer.Serialize(inbound.Holding(merged), JsonOptions);
    }

    /// <summary>What it holds back, each item whole: the <c>heldBack</c> list, or the tasks of
    /// <c>holdsBack</c> for a version 1 document.</summary>
    private IEnumerable<HeldBackItem> Items()
    {
        var items = KnowsTasksOnly
            ? (HoldsBack ?? []).Select(HeldBackItem.ForTask)
            : HeldBack!.OfType<HeldBackItemDocument>().Select(item => item.ToItem());

        return items.Where(item => item.IsSuccess).Select(item => item.Value);
    }

    private static ImpedimentDocument? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<ImpedimentDocument>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal sealed record ImpedimentStateChangeDocument
{
    public ImpedimentState State { get; init; }

    public DateOnly On { get; init; }

    public string? With { get; init; }
}

/// <summary>One held-back item as stored: its kind by name — the names
/// <see cref="GoalAttachmentDocument"/> gives a goal's — and the fields that kind reads.</summary>
internal sealed record HeldBackItemDocument
{
    /// <summary>Null for a kind this build does not know, which leaves the item out rather
    /// than the impediment.</summary>
    [JsonConverter(typeof(LenientHeldBackKindConverter))]
    public HeldBackKind? Kind { get; init; }

    public Guid? TaskId { get; init; }

    public string? Repository { get; init; }

    public int? Number { get; init; }

    public string? Plan { get; init; }

    public static HeldBackItemDocument From(HeldBackItem item) =>
        new()
        {
            Kind = item.Kind,
            TaskId = item.TaskId,
            Repository = item.Repository,
            Number = item.Number,
            Plan = item.PlanTag
        };

    public Backlog.SharedKernel.Results.Result<HeldBackItem> ToItem() =>
        Kind is { } kind
            ? HeldBackItem.Of(kind, TaskId, Repository, Number, Plan)
            : ProgressErrors.HeldBackItemWithoutIdentity();

    private sealed class LenientHeldBackKindConverter : JsonConverter<HeldBackKind?>
    {
        public override HeldBackKind? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && Enum.TryParse<HeldBackKind>(reader.GetString(), ignoreCase: true, out var kind)
                && Enum.IsDefined(kind)
                ? kind
                : null;

        public override void Write(Utf8JsonWriter writer, HeldBackKind? value, JsonSerializerOptions options)
        {
            if (value is { } kind) writer.WriteStringValue(kind.ToString());
            else writer.WriteNullValue();
        }
    }
}
