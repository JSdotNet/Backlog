using System.Reflection;
using System.Text.Json;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Api.Endpoints;
using Backlog.SharedKernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// The one mapping from an error <c>Result</c> to a response (org ADR 0004), over every code
/// the service names and every <see cref="ErrorType"/>: each becomes a Problem Details body
/// with the code's <c>type</c>, a title, the status the mapping chose, the message as
/// <c>detail</c>, the code itself and a trace id. The endpoint probes in
/// <see cref="SyncProblemDetailsCoverageTests"/> reach only the failures a probe can provoke;
/// this reaches the rest.
/// </summary>
public sealed class SyncResultsProblemTests : IDisposable
{
    private readonly SyncServiceFactory _service = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string> Codes =>
    [
        .. typeof(SyncErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(constant => constant.IsLiteral && constant.FieldType == typeof(string))
            .Select(constant => (string)constant.GetRawConstantValue()!),
    ];

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task Every_error_code_becomes_a_problem_body(string code)
    {
        foreach (var type in Enum.GetValues<ErrorType>())
        {
            var (status, root) = await Render(new Error(code, "What went wrong.", type));

            Assert.InRange(status, 400, 599);
            Assert.Equal(SyncResults.ProblemTypeBase + code, root.GetProperty("type").GetString());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
            Assert.Equal(status, root.GetProperty("status").GetInt32());
            Assert.Equal("What went wrong.", root.GetProperty("detail").GetString());
            Assert.Equal(code, root.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        }
    }

    /// <summary>The org table for a code the service does not single out: the type decides.</summary>
    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.Failure, 500)]
    [InlineData(ErrorType.Unexpected, 500)]
    public async Task An_unnamed_code_takes_its_status_from_its_type(ErrorType type, int expected)
    {
        var (status, _) = await Render(new Error("test.unnamed", "No such thing.", type));

        Assert.Equal(expected, status);
    }

    private async Task<(int Status, JsonElement Root)> Render(Error error)
    {
        using var scope = _service.Services.CreateScope();
        using var body = new MemoryStream();

        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Response.Body = body;

        await SyncResults.From(context, Result.Failure(error), () => Results.NoContent()).ExecuteAsync(context);

        Assert.Equal("application/problem+json", context.Response.ContentType?.Split(';')[0]);

        body.Position = 0;
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: Cancellation);

        return (context.Response.StatusCode, document.RootElement.Clone());
    }
}
