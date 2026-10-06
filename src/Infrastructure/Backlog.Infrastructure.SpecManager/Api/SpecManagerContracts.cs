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

/// <summary><c>GET /api/producten/{slug}/backlog</c>. Not paged.</summary>
internal sealed record BacklogResponse(IReadOnlyList<BacklogitemDto>? Items);

/// <summary>
/// One backlog item as the REST list answers it. Ids are kept as the strings they
/// arrive as: the connector compares them and never parses one.
/// </summary>
/// <param name="Prioriteit">The item's rank in the backlog — a number that moves
/// with every drag, which is why it is never part of the display key.</param>
/// <param name="Nummer">The item's fixed number within its product.</param>
/// <param name="WachtOp">The waited-on items written out, filled only for an agent's
/// tool and null on the REST list; read for nothing here.</param>
/// <param name="Geblokkeerd">The server's own judgement, null on the REST list; the
/// connector derives one when it is absent.</param>
internal sealed record BacklogitemDto(
    string Id,
    string Titel,
    string? Omschrijving,
    string StatusId,
    int Prioriteit,
    int Nummer,
    IReadOnlyList<string>? Verwijzingen,
    IReadOnlyList<string>? LabelIds,
    string? ToegewezenAanId,
    JiraherkomstDto? Jira,
    bool IsGearchiveerd,
    int? Inspanning,
    IReadOnlyList<string>? WachtOpIds,
    IReadOnlyList<AfgewachtItemDto>? WachtOp,
    bool? Geblokkeerd,
    BelemmeringDto? Belemmering,
    string? Issuetype,
    DateOnly? Deadline,
    DateTimeOffset? AfgerondOp,
    DateTimeOffset BijgewerktOp,
    string? SprintId,
    string? Sprintnaam,
    string? ExterneSleutel);

/// <summary>Where a Jira-linked item came from. <see cref="LosgeraaktOp"/> is set
/// once the link is broken.</summary>
internal sealed record JiraherkomstDto(string? Sleutel, string? Url, DateTimeOffset? LosgeraaktOp);

/// <summary>Why an item is stuck, said on the item itself. The reason is
/// optional.</summary>
internal sealed record BelemmeringDto(string? Reden, string? BelemmerdDoorId, DateTimeOffset? BelemmerdOp);

internal sealed record AfgewachtItemDto(string? Id, int Nummer, string? Titel, bool Afgerond);

/// <summary><c>GET /api/producten/{slug}/backlogstatussen</c>.</summary>
internal sealed record StatusesResponse(IReadOnlyList<BacklogstatusDto>? Statussen);

internal sealed record BacklogstatusDto(
    string Id,
    string Naam,
    int Volgorde,
    bool IsEindstatus,
    bool IsAistatus,
    bool IsReviewstatus,
    bool IsOntwikkelstatus)
{
    /// <summary>Whether the status marks work as begun: development, an agent, or
    /// review.</summary>
    public bool IsWorkStatus => IsOntwikkelstatus || IsAistatus || IsReviewstatus;
}

/// <summary><c>GET /api/producten/{slug}/backloglabels</c>.</summary>
internal sealed record LabelsResponse(IReadOnlyList<BackloglabelDto>? Labels);

internal sealed record BackloglabelDto(string Id, string Naam);

/// <summary><c>GET /api/producten/{slug}/backlogleden</c>.</summary>
internal sealed record MembersResponse(IReadOnlyList<BacklogledDto>? Leden);

/// <param name="GebruikerId">The id <see cref="BacklogitemDto.ToegewezenAanId"/>
/// names a member by.</param>
/// <param name="IsJij">Whether this is the member behind the token that asked.</param>
internal sealed record BacklogledDto(string GebruikerId, string? Naam, bool IsJij, bool IsActief, bool IsServiceAccount);

/// <summary>The body of <c>PUT /api/producten/{slug}/backlog/{id}/status</c>.</summary>
internal sealed record SetStatusRequest(string StatusId);

/// <summary>The problem details spec-manager answers an error with; only what a
/// person is shown is read.</summary>
internal sealed record ProblemResponse(string? Title, string? Detail);
