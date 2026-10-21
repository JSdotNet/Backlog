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

    /// <summary>
    /// The version a client compares its own against: a client older than this
    /// stops syncing and asks to be updated. Set in the project rather than left
    /// to the SDK's <c>1.0.0</c>, which every released client is below.
    /// </summary>
    [Fact]
    public async Task The_root_reports_the_version_the_project_declares()
    {
        var response = await _service.CreateClient().GetAsync("/", Cancellation);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("0.1.0", body.GetProperty("version").GetString());
    }

    /// <summary>
    /// A build no release stamped — this one, a local run, a hand deploy — says so
    /// with a null <c>release</c>, beside a <c>version</c> that stays the project's
    /// compatibility number either way.
    /// </summary>
    [Fact]
    public async Task An_unreleased_build_reports_no_release()
    {
        var response = await _service.CreateClient().GetAsync("/", Cancellation);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.True(body.TryGetProperty("release", out var release), "GET / has no 'release' field.");
        Assert.Equal(JsonValueKind.Null, release.ValueKind);
        Assert.Equal("0.1.0", body.GetProperty("version").GetString());
    }

    /// <summary>
    /// Release All stamps the release number it deploys under as the
    /// <c>BacklogRelease</c> assembly metadata, apart from <c>Version</c>, which
    /// clients compare against and a release must never move. A blank or missing
    /// entry is no release.
    /// </summary>
    [Theory]
    [InlineData("0.1.123", "0.1.123")]
    [InlineData(" 0.1.123 ", "0.1.123")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void The_release_is_the_BacklogRelease_metadata(string stamped, string? release)
    {
        var metadata = new[]
        {
            new System.Reflection.AssemblyMetadataAttribute("RepositoryUrl", "https://github.com/JSdotNet/Backlog"),
            new System.Reflection.AssemblyMetadataAttribute("BacklogRelease", stamped),
        };

        Assert.Equal(release, BuildInformation.ReleaseOf(metadata));
    }

    [Fact]
    public void Without_BacklogRelease_metadata_there_is_no_release()
    {
        var metadata = new[] { new System.Reflection.AssemblyMetadataAttribute("RepositoryUrl", "https://github.com/JSdotNet/Backlog") };

        Assert.Null(BuildInformation.ReleaseOf(metadata));
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
