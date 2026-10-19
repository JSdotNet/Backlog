using System.Text.Json;

namespace Backlog.Infrastructure.SpecManager.Api;

/// <summary>
/// The serializer settings every spec-manager answer is read with: camelCase and
/// case-insensitive, and a field this build does not know is ignored rather than
/// refused — the installation is deployed on its own schedule and adds fields.
/// </summary>
internal static class SpecManagerJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

/// <summary>
/// One product of <c>GET /api/products</c>, which answers the products the account
/// is a member of, archived ones left out by the server. Only the two fields the
/// settings page shows and stores are read; the rest are ignored.
/// </summary>
/// <param name="Slug">What every other path names the product by, and what a
/// connected target stores.</param>
/// <param name="Name">The product's name, which the person knows it by.</param>
internal sealed record ProductDto(string? Slug, string? Name);

/// <summary><c>GET /api/products/{slug}/backlog</c>. Not paged.</summary>
internal sealed record BacklogResponse(IReadOnlyList<BacklogItemDto>? Items);

/// <summary>
/// One backlog item as the REST list answers it. Ids are kept as the strings they
/// arrive as: the connector compares them and never parses one.
/// </summary>
/// <param name="Priority">The item's rank in the backlog — a number that moves
/// with every drag, which is why it is never part of the display key.</param>
/// <param name="Number">The item's fixed number within its product.</param>
/// <param name="WaitsFor">The waited-on items written out, filled only for an agent's
/// tool and null on the REST list; read for nothing here.</param>
/// <param name="Blocked">The server's own judgement, null on the REST list; the
/// connector derives one when it is absent.</param>
internal sealed record BacklogItemDto(
    string Id,
    string Title,
    string? Description,
    string StatusId,
    int Priority,
    int Number,
    IReadOnlyList<string>? References,
    IReadOnlyList<string>? LabelIds,
    string? AssigneeId,
    JiraOriginDto? Jira,
    bool IsArchived,
    int? Effort,
    IReadOnlyList<string>? WaitsForIds,
    IReadOnlyList<AwaitedItemDto>? WaitsFor,
    bool? Blocked,
    ObstacleDto? Obstacle,
    string? IssueType,
    DateOnly? Deadline,
    DateTimeOffset? FinishedAt,
    DateTimeOffset UpdatedAt,
    string? SprintId,
    string? SprintName,
    string? ExternalKey);

/// <summary>Where a Jira-linked item came from. <see cref="DetachedAt"/> is set
/// once the link is broken.</summary>
internal sealed record JiraOriginDto(string? Key, string? Url, DateTimeOffset? DetachedAt);

/// <summary>Why an item is stuck, said on the item itself. The reason is
/// optional.</summary>
internal sealed record ObstacleDto(string? Reason, string? ImpededById, DateTimeOffset? ImpededAt);

internal sealed record AwaitedItemDto(string? Id, int Number, string? Title, bool Finished);

/// <summary><c>GET /api/products/{slug}/backlog-statuses</c>.</summary>
internal sealed record StatusesResponse(IReadOnlyList<BacklogStatusDto>? Statuses);

internal sealed record BacklogStatusDto(
    string Id,
    string Name,
    int Order,
    bool IsEndStatus,
    bool IsAiStatus,
    bool IsReviewStatus,
    bool IsDevelopmentStatus)
{
    /// <summary>Whether the status marks work as begun: development, an agent, or
    /// review.</summary>
    public bool IsWorkStatus => IsDevelopmentStatus || IsAiStatus || IsReviewStatus;
}

/// <summary><c>GET /api/products/{slug}/backlog-labels</c>.</summary>
internal sealed record LabelsResponse(IReadOnlyList<BacklogLabelDto>? Labels);

internal sealed record BacklogLabelDto(string Id, string Name);

/// <summary><c>GET /api/products/{slug}/backlog-members</c>.</summary>
internal sealed record MembersResponse(IReadOnlyList<BacklogMemberDto>? Members);

/// <param name="UserId">The id <see cref="BacklogItemDto.AssigneeId"/>
/// names a member by.</param>
/// <param name="IsYou">Whether this is the member behind the token that asked.</param>
internal sealed record BacklogMemberDto(string UserId, string? Name, bool IsYou, bool IsActive, bool IsServiceAccount);

/// <summary>The body of <c>PUT /api/products/{slug}/backlog/{id}/status</c>.</summary>
internal sealed record SetStatusRequest(string StatusId);

/// <summary>
/// What a refusal's body says, in either shape spec-manager answers one with: its
/// own <c>{ message, kind }</c>, or the problem details the framework answers with
/// before that handling is reached. Only what a person is shown is read.
/// </summary>
internal sealed record ProblemResponse(string? Message, string? Title, string? Detail);
