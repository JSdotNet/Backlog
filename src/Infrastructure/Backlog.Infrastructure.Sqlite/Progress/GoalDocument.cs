using System.Text.Json;
using System.Text.Json.Serialization;

using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.DomainModels;

namespace Backlog.Infrastructure.Sqlite.Progress;

/// <summary>
/// The stored shape of one <see cref="Goal"/>: one JSON document per row of
/// <c>progress_goals</c>, and the <c>goal</c> document that travels between devices
/// (local ADR 0035 §9). Dates are written as <c>yyyy-MM-dd</c> and the kind by name, so
/// the stored text reads the same on every machine. A goal without a due date has no
/// <c>dueDate</c>: never an explicit null, which a build that required a date cannot read.
/// </summary>
internal sealed record GoalDocument
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

    public string? Outcome { get; init; }

    /// <summary>The due date, or null for none; <c>0001-01-01</c> reads as none too
    /// (<see cref="ProgressDocuments.StoredDueDate"/>).</summary>
    public DateOnly? DueDate { get; init; }

    public List<GoalAttachmentDocument> Attachments { get; init; } = [];

    /// <summary>When the goal was set; orders the goals alike on every device until the person
    /// places one (<see cref="GoalOrder"/>).</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Where the person placed the goal in their order, or absent for a goal never
    /// placed — which keeps the v1 shape. A build that does not know it reads the goal by
    /// <see cref="CreatedAt"/>, the order it was set in.</summary>
    public long? Rank { get; init; }

    /// <summary>Fields a newer build wrote that this one does not know, kept so a save here
    /// writes them back rather than stripping them from that build's goal.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>When a person deleted the goal, or absent while it exists. A document
    /// carrying it is a <b>tombstone</b>: the goal's id and title and nothing it held, kept
    /// so the deletion travels as the goal's last change and no older copy brings the goal
    /// back.</summary>
    public DateTimeOffset? DeletedAt { get; init; }

    [JsonIgnore]
    public bool IsTombstone => DeletedAt is not null;

    public static GoalDocument Tombstone(Guid id, string title, DateTimeOffset deletedAt) =>
        new() { Id = id, Title = title, DeletedAt = deletedAt };

    public static GoalDocument From(Goal goal) =>
        new()
        {
            Id = goal.Id,
            Title = goal.Title,
            Outcome = goal.Outcome,
            DueDate = goal.DueDate,
            Attachments = [.. goal.Attachments.Select(GoalAttachmentDocument.From)],
            CreatedAt = goal.CreatedAt,
            Rank = goal.Rank
        };

    /// <summary>The goal this document holds. Throws <see cref="JsonException"/> for one
    /// that does not read as a goal — no title, or an attachment of no known kind — so the
    /// store refuses it as it refuses text that will not parse. <paramref name="setAt"/> is
    /// when the goal reads as set when the document does not say.</summary>
    public Goal ToGoal(DateTimeOffset? setAt = null)
    {
        if (string.IsNullOrWhiteSpace(Title)) throw new JsonException("A goal document has no title.");

        var attachments = new List<GoalAttachment>();
        foreach (var stored in Attachments ?? [])
        {
            var attachment = GoalAttachment.Of(stored.Kind, stored.Plan, stored.TaskId, stored.Repository, stored.Number, stored.AttachedOn);
            if (attachment.IsFailure) throw new JsonException($"A goal document holds an attachment that does not read: {attachment.Error.Code}.");
            attachments.Add(attachment.Value);
        }

        return Goal.Restore(Id, Title, Outcome, ProgressDocuments.StoredDueDate(DueDate), attachments, CreatedAt ?? setAt, Rank);
    }
}

internal sealed record GoalAttachmentDocument
{
    public GoalAttachmentKind Kind { get; init; }

    public string? Plan { get; init; }

    public Guid? TaskId { get; init; }

    public string? Repository { get; init; }

    public int? Number { get; init; }

    public DateOnly AttachedOn { get; init; }

    public static GoalAttachmentDocument From(GoalAttachment attachment) =>
        new()
        {
            Kind = attachment.Kind,
            Plan = attachment.Plan,
            TaskId = attachment.TaskId,
            Repository = attachment.Repository,
            Number = attachment.Number,
            AttachedOn = attachment.AttachedOn
        };
}
