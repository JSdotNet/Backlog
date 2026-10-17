using System.Text.Json;
using System.Text.Json.Serialization;

using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.SharedKernel;

namespace Backlog.Modules.Progress.Abstractions;

/// <summary>
/// The JSON of the Progress documents a head reads without the module behind it: the
/// <c>goal</c> and <c>impediment</c> documents the desktop's store writes (local ADR 0035 §9),
/// read here in the shape that store keeps — camel case, dates as <c>yyyy-MM-dd</c>, kinds
/// and states by name — and the two documents this surface defines whole,
/// <see cref="StandUpContextDto"/> and <see cref="ImpedimentEditDto"/>. Each reader answers
/// null for text it cannot read rather than throwing: a document a newer build wrote is
/// passed over, never fatal.
/// </summary>
public static class ProgressDocuments
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(), new WorkingWeekConverter() }
    };

    /// <summary>The goal a <c>goal</c> document holds, or null when it does not read as one —
    /// not JSON, no title, or naming another id than <paramref name="id"/> when one is given.
    /// An attachment of a kind this build does not know, or missing what its kind needs, is
    /// left out rather than refusing the goal.</summary>
    public static GoalDocumentDto? ReadGoal(string? content, Guid? id = null)
    {
        if (Parse<GoalShape>(content) is not { } shape) return null;
        if (string.IsNullOrWhiteSpace(shape.Title) || (id is { } expected && shape.Id != expected)) return null;

        var deleted = shape.DeletedAt is not null;
        var attachments = deleted
            ? []
            : (shape.Attachments ?? [])
                .Select(AttachmentOf)
                .OfType<GoalAttachmentDto>()
                .ToList();

        return new GoalDocumentDto(
            new GoalDto(shape.Id, shape.Title, shape.Outcome, shape.DueDate, attachments),
            shape.CreatedAt,
            deleted);
    }

    /// <summary>The impediment an <c>impediment</c> document holds, or null when it does not
    /// read as one — not JSON, no title, no history, or another id than
    /// <paramref name="id"/>.</summary>
    public static ImpedimentDocumentDto? ReadImpediment(string? content, Guid? id = null)
    {
        if (Parse<ImpedimentShape>(content) is not { } shape) return null;
        if (string.IsNullOrWhiteSpace(shape.Title) || shape.History is not { Count: > 0 }) return null;
        if (id is { } expected && shape.Id != expected) return null;

        var history = shape.History
            .Select(step => new ImpedimentStateChangeDto(step.State, step.On, Clean(step.With)))
            .ToList();

        // The state is the last step's, as the aggregate keeps it.
        return new ImpedimentDocumentDto(
            new ImpedimentDto(
                shape.Id,
                shape.Title,
                history[^1].State,
                Clean(shape.ReportedTo),
                Clean(shape.HandledBy),
                shape.CheckAgain,
                Clean(shape.Note),
                history,
                [.. (shape.HoldsBack ?? []).Where(task => task != Guid.Empty).Distinct()]),
            shape.CreatedAt);
    }

    /// <summary>The <see cref="StandUpContextDto"/> as its document's JSON.</summary>
    public static string WriteContext(StandUpContextDto context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return JsonSerializer.Serialize(context, Json);
    }

    /// <summary>The stand-up context a document holds, or null when it does not read as one.</summary>
    public static StandUpContextDto? ReadContext(string? content)
    {
        if (Parse<StandUpContextDto>(content) is not { } context) return null;
        return context with { PullRequests = [.. (context.PullRequests ?? []).Where(pull => pull is { Repository: not null, Number: not null })] };
    }

    /// <summary>The <see cref="ImpedimentEditDto"/> as its document's JSON.</summary>
    public static string WriteEdit(ImpedimentEditDto edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return JsonSerializer.Serialize(edit, Json);
    }

    /// <summary>The edit a document holds, or null when it does not read as one: not JSON,
    /// no impediment, a kind this build does not know, or a state change naming no state.</summary>
    public static ImpedimentEditDto? ReadEdit(string? content)
    {
        if (Parse<ImpedimentEditDto>(content) is not { } edit) return null;
        if (edit.ImpedimentId == Guid.Empty || !Enum.IsDefined(edit.Kind)) return null;
        if (edit.Kind == ImpedimentEditKind.State && edit.State is not { } state) return null;
        if (edit.State is { } named && !Enum.IsDefined(named)) return null;

        return edit;
    }

    /// <summary>Blank as none and trimmed, as the aggregate keeps its words.</summary>
    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static T? Parse<T>(string? content)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        try
        {
            return JsonSerializer.Deserialize<T>(content, Json);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static GoalAttachmentDto? AttachmentOf(AttachmentShape? stored)
    {
        if (stored is null) return null;

        GoalItemDto? item = stored.Kind switch
        {
            GoalAttachmentKind.Plan when GoalItems.Bare(stored.Plan) is { Length: > 0 } plan => GoalItemDto.ForPlan(plan),
            GoalAttachmentKind.Task when stored.TaskId is { } task && task != Guid.Empty => GoalItemDto.ForTask(task),
            GoalAttachmentKind.PullRequest when !string.IsNullOrWhiteSpace(stored.Repository) && stored.Number is > 0 =>
                GoalItemDto.ForPullRequest(stored.Repository.Trim(), stored.Number.Value),
            _ => null,
        };

        return item is null ? null : new GoalAttachmentDto(item, stored.AttachedOn);
    }

    // The stored shapes, in the words the desktop's store writes them. Kinds and states that
    // do not read fail the whole parse, which is why an attachment's kind is read leniently.
    private sealed record GoalShape(
        Guid Id,
        string? Title,
        string? Outcome,
        DateOnly DueDate,
        List<AttachmentShape?>? Attachments,
        DateTimeOffset? CreatedAt,
        DateTimeOffset? DeletedAt);

    private sealed record AttachmentShape(
        [property: JsonConverter(typeof(LenientKindConverter))] GoalAttachmentKind? Kind,
        string? Plan,
        Guid? TaskId,
        string? Repository,
        int? Number,
        DateOnly AttachedOn);

    private sealed record ImpedimentShape(
        Guid Id,
        string? Title,
        string? ReportedTo,
        string? HandledBy,
        DateOnly? CheckAgain,
        string? Note,
        List<StepShape>? History,
        List<Guid>? HoldsBack,
        DateTimeOffset? CreatedAt);

    private sealed record StepShape(ImpedimentState State, DateOnly On, string? With);

    /// <summary>A working week as its days and its one-off days, nothing derived: the
    /// week's computed views refer back to the week and would never end.</summary>
    private sealed class WorkingWeekConverter : JsonConverter<WorkingHours>
    {
        public override WorkingHours? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var shape = JsonSerializer.Deserialize<WeekShape>(ref reader, options);
            return shape is null ? null : new WorkingHours { Days = shape.Days ?? [], Overrides = shape.Overrides ?? [] };
        }

        public override void Write(Utf8JsonWriter writer, WorkingHours value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new WeekShape([.. value.Days], [.. value.Overrides]), options);
    }

    private sealed record WeekShape(List<WorkingDay>? Days, List<DayOverride>? Overrides);

    /// <summary>An attachment kind by name, or null for a name this build does not know, so
    /// one new kind leaves that attachment out rather than the goal.</summary>
    private sealed class LenientKindConverter : JsonConverter<GoalAttachmentKind?>
    {
        public override GoalAttachmentKind? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && Enum.TryParse<GoalAttachmentKind>(reader.GetString(), ignoreCase: true, out var kind)
                && Enum.IsDefined(kind)
                ? kind
                : null;

        public override void Write(Utf8JsonWriter writer, GoalAttachmentKind? value, JsonSerializerOptions options)
        {
            if (value is { } kind) writer.WriteStringValue(kind.ToString());
            else writer.WriteNullValue();
        }
    }
}
