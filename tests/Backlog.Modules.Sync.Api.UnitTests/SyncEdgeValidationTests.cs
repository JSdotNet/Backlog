using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Backlog.Modules.Sync.Abstractions;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// The built-in minimal-API validation at the edge (org ADR 0004): a body that leaves out a
/// field its request record marks <c>[Required]</c> is refused before any handler runs, as a
/// 400 Problem Details with an <c>errors</c> extension naming the field. What the value means
/// — a device name the model accepts, a code that matches — stays with the handler, which is
/// why the names allow an empty string and only a missing one is refused here.
/// </summary>
public sealed class SyncEdgeValidationTests : IDisposable
{
    private readonly SyncServiceFactory _service = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string, string[]> RequiredFields => new()
    {
        { SyncRoutes.EnrolmentStart, ["RedirectUri", "CodeChallenge", "CodeChallengeMethod", "State"] },
        { SyncRoutes.RegisterDevice, ["DeviceName"] },
        { SyncRoutes.RedeemPairingCode, ["Code", "DeviceName"] },
    };

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [MemberData(nameof(RequiredFields))]
    public async Task A_body_missing_a_required_field_is_a_validation_problem(string route, string[] fields)
    {
        using var body = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await _service.CreateClient().PostAsync(SyncRoutes.Absolute(route), body, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        var root = problem.RootElement;

        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));

        var named = root.GetProperty("errors").EnumerateObject().Select(error => error.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(fields, field => Assert.Contains(field, named));
    }

    /// <summary>An empty device name is present, so the edge lets it through and the model's
    /// own rule refuses it with its own code — the line between transport validation and a
    /// domain invariant.</summary>
    [Fact]
    public async Task An_empty_device_name_reaches_the_model_rather_than_the_edge()
    {
        var response = await _service.CreateClient().PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
            new { code = "ABCD-EFGH", deviceName = "" },
            Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.Equal(SyncErrorCodes.DeviceNameRequired, problem?.Code);
    }
}
