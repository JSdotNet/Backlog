using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The <c>gh</c> CLI used as a source of credentials rather than as a way of
/// sending requests.
/// <para>
/// This is what makes the whole change possible. <c>gh api</c> has no per-call
/// account selector, so the CLI cannot be asked to be somebody else for one call —
/// but <c>gh auth token --user</c> hands over the token for a named login,
/// including an inactive one, and the app can then send the request itself.
/// </para>
/// <para>
/// Driven through the stand-in executable rather than a fake, for the reason
/// <see cref="GhCliTransportTests"/> is: what is being tested is the conversation
/// with a command line, and a fake would only pin our own idea of it.
/// </para>
/// </summary>
public sealed class GhCliAccountSourceTests
{
    // --- Listing the accounts -------------------------------------------------

    /// <summary><c>gh auth status --json hosts</c> answers
    /// <c>{"hosts":{"github.com":[…]}}</c>, so Settings can offer a picker instead of
    /// asking anybody to spell a login — a typo in a login surfaces as a 404, which
    /// is the class of failure being removed.</summary>
    [Fact]
    public async Task The_accounts_are_read_from_gh_auth_status()
    {
        using var gh = new GhStub().Answers(
            """
            {
              "hosts": {
                "github.com": [
                  { "login": "JSdotNet", "state": "active", "scopes": "repo, read:org" },
                  { "login": "j-schepers_innobv", "state": "inactive", "scopes": "repo" }
                ]
              }
            }
            """);

        var accounts = await gh.Source().ListAsync();

        Assert.Equal(new[] { "api", "auth", "status", "--json", "hosts" }[1..], gh.OnlyCall);

        Assert.Collection(
            accounts,
            first =>
            {
                Assert.Equal("JSdotNet", first.Login);
                Assert.Equal("github.com", first.Host);
                Assert.True(first.Active);
                Assert.Equal("repo, read:org", first.Scopes);
            },
            second =>
            {
                Assert.Equal("j-schepers_innobv", second.Login);
                Assert.False(second.Active);
            });
    }

    /// <summary>The host key names the host when the entry does not, which is how a
    /// GitHub Enterprise Server account arrives.</summary>
    [Fact]
    public async Task An_entry_takes_its_host_from_the_key_it_sits_under()
    {
        using var gh = new GhStub().Answers(
            """{ "hosts": { "ghe.example.internal": [ { "login": "enterprise", "state": "active" } ] } }""");

        var account = Assert.Single(await gh.Source().ListAsync());

        Assert.Equal("ghe.example.internal", account.Host);
        Assert.Null(account.Scopes);
    }

    [Fact]
    public async Task The_account_list_is_worked_out_once_and_then_remembered()
    {
        using var gh = new GhStub().Answers("""{ "hosts": { "github.com": [ { "login": "octocat" } ] } }""");
        var source = gh.Source();

        Assert.Single(await source.ListAsync());
        Assert.Single(await source.ListAsync());

        Assert.Single(gh.Calls);
    }

    /// <summary>No <c>gh</c>, a <c>gh</c> too old for <c>--json hosts</c>, or an
    /// answer that was not JSON. An empty list degrades the Settings picker to manual
    /// entry rather than blocking the panel.</summary>
    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("""{ "hosts": "unexpected" }""")]
    public async Task An_answer_this_build_cannot_read_is_no_accounts(string answer)
    {
        using var gh = new GhStub().Answers(answer);

        Assert.Empty(await gh.Source().ListAsync());
    }

    [Fact]
    public async Task A_cli_that_cannot_be_started_is_no_accounts_and_no_token()
    {
        var source = new GhCliAccountSource(
            Path.Combine(Path.GetTempPath(), $"gh-not-installed-{Guid.NewGuid():N}.cmd"));

        Assert.Empty(await source.ListAsync());
        Assert.Null(await source.GetTokenAsync("octocat"));
    }

    [Fact]
    public async Task A_failed_status_call_is_no_accounts()
    {
        using var gh = new GhStub().Fails(1, "gh: you are not logged in");

        Assert.Empty(await gh.Source().ListAsync());
    }

    // --- Getting one account's token ------------------------------------------

    [Fact]
    public async Task A_token_is_asked_for_by_login()
    {
        using var gh = new GhStub().Answers("gho_innobv\n");

        Assert.Equal("gho_innobv", await gh.Source().GetTokenAsync("j-schepers_innobv"));
        Assert.Equal(new[] { "auth", "token", "--user", "j-schepers_innobv" }, gh.OnlyCall);
    }

    [Fact]
    public async Task A_token_for_another_host_names_the_host()
    {
        using var gh = new GhStub().Answers("gho_enterprise");

        Assert.Equal("gho_enterprise", await gh.Source().GetTokenAsync("enterprise", "ghe.example.internal"));

        Assert.Equal(
            new[] { "auth", "token", "--user", "enterprise", "--hostname", "ghe.example.internal" },
            gh.OnlyCall);
    }

    [Fact]
    public async Task A_login_gh_is_not_signed_in_to_has_no_token()
    {
        using var gh = new GhStub().Fails(1, "no oauth token found for j-schepers_innobv");

        Assert.Null(await gh.Source().GetTokenAsync("j-schepers_innobv"));
    }

    [Fact]
    public async Task A_blank_login_is_not_worth_a_subprocess()
    {
        using var gh = new GhStub().Answers("gho_something");

        Assert.Null(await gh.Source().GetTokenAsync("   "));
        Assert.Empty(gh.Calls);
    }

    /// <summary>A screen full of calls costs one subprocess rather than thirty. The
    /// window is short enough that a rotation is picked up on its own.</summary>
    [Fact]
    public async Task A_token_is_reused_until_it_goes_stale()
    {
        var time = new FakeTimeProvider();
        using var gh = new GhStub().Answers("gho_first");
        var source = gh.Source(time);

        Assert.Equal("gho_first", await source.GetTokenAsync("octocat"));
        Assert.Equal("gho_first", await source.GetTokenAsync("octocat"));
        Assert.Single(gh.Calls);

        gh.Answers("gho_second");

        time.Advance(GhCliAccountSource.TokenLifetime - TimeSpan.FromSeconds(1));
        Assert.Equal("gho_first", await source.GetTokenAsync("octocat"));
        Assert.Single(gh.Calls);

        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal("gho_second", await source.GetTokenAsync("octocat"));
        Assert.Equal(2, gh.Calls.Count);
    }

    /// <summary>A negative answer is remembered too, so a bound account the CLI
    /// cannot satisfy does not launch a subprocess for every call in the
    /// session.</summary>
    [Fact]
    public async Task A_login_with_no_token_is_not_asked_about_again_straight_away()
    {
        using var gh = new GhStub().Fails();
        var source = gh.Source(new FakeTimeProvider());

        Assert.Null(await source.GetTokenAsync("octocat"));
        Assert.Null(await source.GetTokenAsync("octocat"));

        Assert.Single(gh.Calls);
    }

    [Fact]
    public async Task Two_logins_are_two_answers()
    {
        using var gh = new GhStub().Answers("gho_one");
        var source = gh.Source(new FakeTimeProvider());

        Assert.Equal("gho_one", await source.GetTokenAsync("one"));

        gh.Answers("gho_two");
        Assert.Equal("gho_two", await source.GetTokenAsync("two"));

        // And the first is still the first, rather than the second's answer.
        Assert.Equal("gho_one", await source.GetTokenAsync("one"));
    }

    /// <summary>Settings' "Check the connection" button is the only way a
    /// <c>gh auth login</c> or a rotated credential in another window is ever
    /// noticed.</summary>
    [Fact]
    public async Task Invalidate_forgets_the_accounts_and_the_tokens_together()
    {
        using var gh = new GhStub().Answers("gho_first");
        var source = gh.Source(new FakeTimeProvider());

        Assert.Equal("gho_first", await source.GetTokenAsync("octocat"));

        source.Invalidate();

        gh.Answers("gho_second");
        Assert.Equal("gho_second", await source.GetTokenAsync("octocat"));
        Assert.Equal(2, gh.Calls.Count);
    }

    [Fact]
    public async Task Invalidate_forgets_the_account_list_too()
    {
        using var gh = new GhStub().Answers("""{ "hosts": { "github.com": [ { "login": "octocat" } ] } }""");
        var source = gh.Source();

        Assert.Single(await source.ListAsync());

        source.Invalidate();

        gh.Answers("""{ "hosts": { "github.com": [] } }""");
        Assert.Empty(await source.ListAsync());
    }

    /// <summary>A clock the test moves by hand, so the token lifetime is asserted
    /// rather than waited out.</summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
