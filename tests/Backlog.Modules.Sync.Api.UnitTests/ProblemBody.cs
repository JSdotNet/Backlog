using System.Text.Json.Serialization;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// The RFC 7807 body the sync service returns, as much of it as these tests
/// read. Written out rather than deserialized into
/// <c>Microsoft.AspNetCore.Mvc.ProblemDetails</c>, because the extensions are
/// what the assertions are about and a typed record says so.
/// </summary>
internal sealed record ProblemBody(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("status")] int? Status,
    [property: JsonPropertyName("detail")] string? Detail,
    [property: JsonPropertyName("traceId")] string? TraceId,
    [property: JsonPropertyName("code")] string? Code);
