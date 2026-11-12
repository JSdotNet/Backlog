using System.Text;
using System.Text.Json.Nodes;

using Backlog.Desktop.UI.Mcp;
using Backlog.SharedKernel.Mcp;

using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The grant tokens the broker mints and the listener validates (local ADR 0052 §3): a
/// 15-minute HS256 JWT for one grant, signed with a key the process holds in memory, and
/// refused once the grant is revoked or its version moves past the token's.
/// </summary>
public sealed class McpGrantTokensTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 9, 30, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcp-grant-tokens-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new(Now);
    /// <summary>64 bytes rather than the 32 the app draws, so the same key can sign HS512 too:
    /// what refuses that token is then its algorithm alone, not its signature.</summary>
    private readonly byte[] _key = Enumerable.Range(1, 64).Select(value => (byte)value).ToArray();
    private readonly FileMcpGrantStore _grants;
    private readonly McpGrantTokens _tokens;

    public McpGrantTokensTests()
    {
        _grants = new FileMcpGrantStore(Path.Combine(_root, FileMcpGrantStore.FileName), _clock);
        _tokens = new McpGrantTokens(_grants, _clock, _key);
    }

    private CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public void A_minted_token_names_the_grant_its_scopes_and_version_for_15_minutes()
    {
        var grant = _grants.Create("Claude Code", ["tasks:read", "tasks:write"], "jobsc");

        var minted = _tokens.Mint(grant.Id);

        Assert.Null(minted.Refusal);
        Assert.NotNull(minted.Token);

        var jwt = new JsonWebToken(minted.Token.Value);
        Assert.Equal("HS256", jwt.Alg);
        Assert.Equal("backlog-desktop", jwt.Issuer);
        Assert.Equal(["backlog-mcp-local"], jwt.Audiences);
        Assert.Equal(grant.Id, jwt.Subject);
        Assert.Equal("tasks:read tasks:write", jwt.GetClaim("scope").Value);
        Assert.True(jwt.TryGetPayloadValue<int>("ver", out var version));
        Assert.Equal(1, version);
        Assert.Equal(Now.UtcDateTime, jwt.IssuedAt);
        Assert.Equal(Now.UtcDateTime, jwt.ValidFrom);
        Assert.Equal(Now.AddMinutes(15).UtcDateTime, jwt.ValidTo);
        Assert.Equal(Now.AddMinutes(15), minted.Token.ExpiresAt);
    }

    [Fact]
    public void An_unknown_grant_mints_nothing()
    {
        var minted = _tokens.Mint("g_aaaaaaaaaaaaaaaa");

        Assert.Null(minted.Token);
        Assert.Equal(McpGrantRefusal.UnknownGrant, minted.Refusal);
    }

    [Fact]
    public void A_revoked_grant_mints_nothing()
    {
        var grant = _grants.Create("Copilot", [], "jobsc");
        _grants.Revoke(grant.Id);

        var minted = _tokens.Mint(grant.Id);

        Assert.Null(minted.Token);
        Assert.Equal(McpGrantRefusal.Revoked, minted.Refusal);
    }

    [Fact]
    public async Task A_minted_token_validates_as_its_grant_with_its_claims()
    {
        var grant = _grants.Create("Claude Code", ["tasks:read"], "jobsc");
        var token = _tokens.Mint(grant.Id).Token!.Value;

        var validation = await _tokens.ValidateAsync(token, Cancel);

        Assert.Null(validation.Refusal);
        Assert.Equal(grant.Id, validation.Grant?.Id);
        Assert.NotNull(validation.Principal);
        Assert.True(validation.Principal.Identity?.IsAuthenticated);
        Assert.Equal(grant.Id, validation.Principal.FindFirst("sub")?.Value);
        Assert.Equal("tasks:read", validation.Principal.FindFirst("scope")?.Value);
    }

    [Fact]
    public async Task A_token_is_good_until_its_last_second_and_refused_at_expiry_with_no_skew()
    {
        var grant = _grants.Create("Claude Code", [], "jobsc");
        var token = _tokens.Mint(grant.Id).Token!.Value;

        _clock.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(1));
        Assert.Null((await _tokens.ValidateAsync(token, Cancel)).Refusal);

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(McpGrantRefusal.Expired, (await _tokens.ValidateAsync(token, Cancel)).Refusal);
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_refused()
    {
        var grant = _grants.Create("Claude Code", [], "jobsc");
        var other = new McpGrantTokens(_grants, _clock);

        var validation = await _tokens.ValidateAsync(other.Mint(grant.Id).Token!.Value, Cancel);

        Assert.Equal(McpGrantRefusal.BadToken, validation.Refusal);
        Assert.Null(validation.Principal);
    }

    [Fact]
    public async Task A_token_whose_signature_was_altered_is_refused()
    {
        var grant = _grants.Create("Claude Code", [], "jobsc");
        var token = _tokens.Mint(grant.Id).Token!.Value;
        var tampered = token[..^2] + (token[^2] == 'A' ? "B" : "A") + token[^1];

        Assert.Equal(McpGrantRefusal.BadToken, (await _tokens.ValidateAsync(tampered, Cancel)).Refusal);
    }

    [Theory]
    [InlineData("backlog-sync", "backlog-desktop")]
    [InlineData("backlog-mcp-local", "someone-else")]
    public async Task A_token_for_another_audience_or_from_another_issuer_is_refused(string audience, string issuer)
    {
        var grant = _grants.Create("Claude Code", [], "jobsc");

        var token = Forge(grant.Id, audience: audience, issuer: issuer);

        Assert.Equal(McpGrantRefusal.BadToken, (await _tokens.ValidateAsync(token, Cancel)).Refusal);
    }

    [Fact]
    public async Task A_token_signed_with_another_algorithm_is_refused()
    {
        var grant = _grants.Create("Claude Code", [], "jobsc");

        var token = Forge(grant.Id, algorithm: SecurityAlgorithms.HmacSha512);

        Assert.Equal(McpGrantRefusal.BadToken, (await _tokens.ValidateAsync(token, Cancel)).Refusal);
    }

    [Fact]
    public async Task An_unsigned_token_is_refused()
    {
        var grant = _grants.Create("Claude Code", [], "jobsc");
        var payload = new JsonObject
        {
            ["iss"] = "backlog-desktop",
            ["aud"] = "backlog-mcp-local",
            ["sub"] = grant.Id,
            ["ver"] = 1,
            ["iat"] = Now.ToUnixTimeSeconds(),
            ["nbf"] = Now.ToUnixTimeSeconds(),
            ["exp"] = Now.AddMinutes(15).ToUnixTimeSeconds()
        };
        var token = Base64Url("{\"alg\":\"none\",\"typ\":\"JWT\"}") + "." + Base64Url(payload.ToJsonString()) + ".";

        Assert.Equal(McpGrantRefusal.BadToken, (await _tokens.ValidateAsync(token, Cancel)).Refusal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    public async Task Something_that_is_not_a_token_is_refused(string token) =>
        Assert.Equal(McpGrantRefusal.BadToken, (await _tokens.ValidateAsync(token, Cancel)).Refusal);

    [Fact]
    public async Task A_token_for_a_grant_this_app_does_not_keep_is_refused()
    {
        var token = Forge("g_aaaaaaaaaaaaaaaa");

        Assert.Equal(McpGrantRefusal.UnknownGrant, (await _tokens.ValidateAsync(token, Cancel)).Refusal);
    }

    [Fact]
    public async Task A_token_minted_before_its_grant_was_revoked_is_refused_on_the_next_request()
    {
        var grant = _grants.Create("Claude Code", [], "jobsc");
        var token = _tokens.Mint(grant.Id).Token!.Value;
        Assert.Null((await _tokens.ValidateAsync(token, Cancel)).Refusal);

        _grants.Revoke(grant.Id);

        Assert.Equal(McpGrantRefusal.Revoked, (await _tokens.ValidateAsync(token, Cancel)).Refusal);
    }

    [Fact]
    public async Task A_token_older_than_the_stored_grant_version_is_refused()
    {
        var grant = _grants.Create("Claude Code", [], "jobsc");
        var token = _tokens.Mint(grant.Id).Token!.Value;

        // What a person does by hand until the Agents card exists, and what a rotation will do.
        var path = Path.Combine(_root, FileMcpGrantStore.FileName);
        var file = JsonNode.Parse(File.ReadAllText(path))!;
        file["grants"]![0]!["version"] = 2;
        File.WriteAllText(path, file.ToJsonString());

        Assert.Equal(McpGrantRefusal.StaleVersion, (await _tokens.ValidateAsync(token, Cancel)).Refusal);
    }

    [Fact]
    public async Task A_token_with_no_version_is_refused()
    {
        var grant = _grants.Create("Claude Code", [], "jobsc");

        var token = Forge(grant.Id, version: null);

        Assert.Equal(McpGrantRefusal.StaleVersion, (await _tokens.ValidateAsync(token, Cancel)).Refusal);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task A_store_that_cannot_be_read_refuses_the_token_rather_than_throwing(Type failure)
    {
        var store = new Backlog.Tests.FailableMcpGrantStore(_grants);
        var tokens = new McpGrantTokens(store, _clock, _key);
        var token = tokens.Mint(_grants.Create("Claude Code", [], "jobsc").Id).Token!.Value;

        store.Failure = (Exception)Activator.CreateInstance(failure)!;

        var validation = await tokens.ValidateAsync(token, Cancel);

        Assert.Equal(McpGrantRefusal.StoreUnavailable, validation.Refusal);
        Assert.Null(validation.Principal);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>A token signed with this process's key but otherwise as the caller says.</summary>
    private string Forge(
        string subject,
        string audience = "backlog-mcp-local",
        string issuer = "backlog-desktop",
        string algorithm = SecurityAlgorithms.HmacSha256,
        int? version = 1)
    {
        var claims = new Dictionary<string, object>(StringComparer.Ordinal) { ["sub"] = subject };
        if (version is { } ver) claims["ver"] = ver;

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = Now.UtcDateTime,
            NotBefore = Now.UtcDateTime,
            Expires = Now.AddMinutes(15).UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(_key), algorithm)
        });
    }

    private static string Base64Url(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
