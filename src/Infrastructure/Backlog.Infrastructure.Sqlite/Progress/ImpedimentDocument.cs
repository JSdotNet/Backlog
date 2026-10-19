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
/// </summary>
internal sealed record ImpedimentDocument
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>The only shape written so far.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? ReportedTo { get; init; }

    public string? HandledBy { get; init; }

    public DateOnly? CheckAgain { get; init; }

    public string? Note { get; init; }

    public List<ImpedimentStateChangeDocument> History { get; init; } = [];

    public List<Guid> HoldsBack { get; init; } = [];

    /// <summary>When the impediment was raised; orders the impediments alike on every device.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    public static ImpedimentDocument From(Impediment impediment) =>
        new()
        {
            Id = impediment.Id,
            Title = impediment.Title,
            ReportedTo = impediment.ReportedTo,
            HandledBy = impediment.HandledBy,
            CheckAgain = impediment.CheckAgain,
            Note = impediment.Note,
            History = [.. impediment.History.Select(step => new ImpedimentStateChangeDocument { State = step.State, On = step.On, With = step.With })],
            HoldsBack = [.. impediment.HoldsBack],
            CreatedAt = impediment.CreatedAt
        };

    /// <summary>The impediment this document holds. Throws <see cref="JsonException"/> for
    /// one that does not read as an impediment — no title, or no step in its history — so
    /// the store refuses it as it refuses text that will not parse.</summary>
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
            HoldsBack ?? [],
            CreatedAt,
            History[0].On);
    }
}

internal sealed record ImpedimentStateChangeDocument
{
    public ImpedimentState State { get; init; }

    public DateOnly On { get; init; }

    public string? With { get; init; }
}
