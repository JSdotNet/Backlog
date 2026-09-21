using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// What the service says about itself to anyone who asks. The root is the one
/// anonymous route, and the build it reports is what a machine compares against
/// the repository to decide whether a deploy is due — so the shape of that answer
/// is a contract, and an empty commit has to be an honest "nothing stamped"
/// rather than a value that happens to compare equal to something.
/// </summary>
public class ServiceIdentityTests : IDisposable
{
    private readonly SyncServiceFactory _service = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task The_root_names_the_service_and_the_build_it_is_running()
    {
        var response = await _service.CreateClient().GetAsync("/", Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("Backlog Sync", body.GetProperty("service").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("version").GetString()));
        Assert.Equal(JsonValueKind.String, body.GetProperty("commit").ValueKind);
        Assert.Equal(BuildInformation.Commit, body.GetProperty("commit").GetString());
    }

    /// <summary>The SDK stamps <c>&lt;version&gt;+&lt;sha&gt;</c> when it built
    /// from a git checkout, and a bare version when it did not; both are real
    /// contents of the attribute, and only the second half is the commit.</summary>
    [Theory]
    [InlineData("1.0.0+8315afc00cefc1663e7ecb2f1ab7653eb6096e46", "1.0.0", "8315afc00cefc1663e7ecb2f1ab7653eb6096e46")]
    [InlineData("1.0.0", "1.0.0", "")]
    [InlineData("1.0.0+", "1.0.0", "")]
    [InlineData("", "", "")]
    public void The_commit_is_the_part_after_the_plus(string informational, string version, string commit)
    {
        Assert.Equal(version, BuildInformation.VersionOf(informational));
        Assert.Equal(commit, BuildInformation.CommitOf(informational));
    }
}
