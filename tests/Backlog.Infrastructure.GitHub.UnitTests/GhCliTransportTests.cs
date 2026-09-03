using System.Text.Json;

using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// What the app actually says to the <c>gh</c> CLI, and what it makes of what comes
/// back.
/// <para>
/// Characterization: every assertion here states what this transport does today, not
/// what it ought to do. It had no test at all, and the multi-account work narrows it to
/// the default credential path — so the command line it writes, the messages it raises
/// and the caching it does are written down first, and a later stage that changes one
/// of them has to change a test and say why.
/// </para>
/// <para>
/// Driven through a stand-in executable rather than a fake, because the executable is
/// the only seam the type has. See <see cref="GhStub"/>.
/// </para>
/// </summary>
public sealed class GhCliTransportTests
{
    private const string DefaultVersionHeader = "X-GitHub-Api-Version: 2022-11-28";

    // --- Availability, and who gh is signed in as -----------------------------

    /// <summary><c>gh api user</c> proves both halves at once: the CLI is there, and
    /// its stored credential still works.</summary>
    [Fact]
    public async Task Availability_is_proved_by_asking_gh_who_it_is_signed_in_as()
    {
        using var gh = new GhStub().Answers("""{"login":"octocat"}""");
        var transport = gh.Transport();

        Assert.True(await transport.IsAvailableAsync());

        Assert.Equal(new[] { "api", "user" }, gh.OnlyCall);
        Assert.Equal("octocat", transport.Account);
    }

    /// <summary>An answer with no <c>login</c> in it still counts as signed in — the
    /// account is a label for Settings, not the proof.</summary>
    [Fact]
    public async Task An_answer_without_a_login_is_still_available_but_has_no_account()
    {
        using var gh = new GhStub().Answers("{}");
        var transport = gh.Transport();

        Assert.True(await transport.IsAvailableAsync());
        Assert.Null(transport.Account);
    }

    [Fact]
    public async Task Availability_is_worked_out_once_and_then_remembered()
    {
        using var gh = new GhStub().Answers("""{"login":"octocat"}""");
        var transport = gh.Transport();

        Assert.True(await transport.IsAvailableAsync());
        Assert.True(await transport.IsAvailableAsync());

        Assert.Single(gh.Calls);
    }

    /// <summary>No <c>gh</c> on this machine is an ordinary state, not a failure: the
    /// resolving transport is expected to move on to a token.</summary>
    [Fact]
    public async Task A_cli_that_cannot_even_be_started_is_simply_unavailable()
    {
        var transport = new GhCliTransport(
            Path.Combine(Path.GetTempPath(), $"gh-not-installed-{Guid.NewGuid():N}.cmd"));

        Assert.False(await transport.IsAvailableAsync());
        Assert.Null(transport.Account);
    }

    [Fact]
    public async Task A_cli_that_cannot_authenticate_is_unavailable()
    {
        using var gh = new GhStub().Fails(1, "gh: To get started with GitHub CLI, please run: gh auth login");

        Assert.False(await gh.Transport().IsAvailableAsync());
    }

    [Fact]
    public async Task An_answer_that_is_not_json_leaves_the_cli_unavailable()
    {
        using var gh = new GhStub().Answers("this is not json");

        Assert.False(await gh.Transport().IsAvailableAsync());
    }

    /// <summary>Both fields, because Settings shows the account beside the connection
    /// line and a stale login there would read as the app having switched
    /// accounts.</summary>
    [Fact]
    public async Task Invalidate_forgets_the_availability_and_the_account_together()
    {
        using var gh = new GhStub().Answers("""{"login":"octocat"}""");
        var transport = gh.Transport();

        Assert.True(await transport.IsAvailableAsync());
        Assert.Equal("octocat", transport.Account);

        transport.Invalidate();
        Assert.Null(transport.Account);

        // Asking again reaches the CLI rather than the remembered answer, which is
        // the whole point of invalidating: a `gh auth login` in another window is
        // noticed without restarting the app.
        gh.Fails();
        Assert.False(await transport.IsAvailableAsync());
        Assert.Equal(2, gh.Calls.Count);
    }

    // --- The command line a call writes ---------------------------------------

    [Fact]
    public async Task A_call_names_the_verb_the_path_and_the_api_version()
    {
        using var gh = new GhStub().Answers("[]");

        await gh.Transport().SendAsync(HttpMethod.Get, "repos/octo/demo/issues");

        Assert.Equal(
            new[] { "api", "--method", "GET", "repos/octo/demo/issues", "--header", DefaultVersionHeader },
            gh.OnlyCall);
    }

    /// <summary>The version is overridden per call rather than left to the CLI's own
    /// default, because the billing endpoints answer 404 through <c>gh</c>
    /// otherwise.</summary>
    [Fact]
    public async Task A_caller_that_asks_for_an_api_version_gets_it_trimmed()
    {
        using var gh = new GhStub().Answers("{}");

        await gh.Transport().SendAsync(
            HttpMethod.Get,
            "users/jsdotnet/settings/billing/ai_credit/usage",
            apiVersion: "  2026-03-10  ");

        Assert.Equal("X-GitHub-Api-Version: 2026-03-10", gh.OnlyCall[5]);
    }

    [Fact]
    public async Task A_caller_that_asks_for_blank_gets_the_version_the_rest_of_the_app_uses()
    {
        using var gh = new GhStub().Answers("{}");

        await gh.Transport().SendAsync(HttpMethod.Get, "repos/octo/demo", apiVersion: "   ");

        Assert.Equal(DefaultVersionHeader, gh.OnlyCall[5]);
    }

    /// <summary>The clients build paths both ways, and <c>gh api</c> wants the
    /// relative form.</summary>
    [Fact]
    public async Task A_leading_slash_is_trimmed_off_the_path()
    {
        using var gh = new GhStub().Answers("{}");

        await gh.Transport().SendAsync(HttpMethod.Get, "/repos/octo/demo");

        Assert.Equal("repos/octo/demo", gh.OnlyCall[3]);
    }

    [Fact]
    public async Task A_body_is_piped_in_rather_than_put_on_the_command_line()
    {
        using var gh = new GhStub().Answers("{}");

        await gh.Transport().SendAsync(
            HttpMethod.Post,
            "repos/octo/demo/issues",
            new { IssueTitle = "Ship it" });

        Assert.Equal(
            new[]
            {
                "api", "--method", "POST", "repos/octo/demo/issues",
                "--header", DefaultVersionHeader,
                "--input", "-"
            },
            gh.OnlyCall);

        // Serialized with the shared options, so the CLI path and the token path send
        // GitHub the same field names.
        Assert.Equal("""{"issue_title":"Ship it"}""", gh.StandardInput);
    }

    // --- What comes back ------------------------------------------------------

    [Fact]
    public async Task A_failed_call_carries_what_gh_wrote_on_standard_error()
    {
        using var gh = new GhStub().Fails(1, "gh: Not Found (HTTP 404)");

        var exception = await Assert.ThrowsAsync<GitHubException>(() =>
            gh.Transport().SendAsync(HttpMethod.Get, "repos/octo/demo"));

        Assert.Equal("gh: Not Found (HTTP 404)", exception.Message);
    }

    /// <summary>A CLI that fails and says nothing still has to produce a sentence
    /// somebody can act on, so the verb and the path — the path as the caller wrote
    /// it, leading slash and all — stand in for one.</summary>
    [Fact]
    public async Task A_failed_call_that_said_nothing_names_the_verb_and_the_path()
    {
        using var gh = new GhStub().Fails();

        var exception = await Assert.ThrowsAsync<GitHubException>(() =>
            gh.Transport().SendAsync(HttpMethod.Delete, "/repos/octo/demo/issues/7"));

        Assert.Equal("The GitHub CLI failed on DELETE /repos/octo/demo/issues/7.", exception.Message);
    }

    [Fact]
    public async Task An_answer_that_is_not_json_is_reported_as_exactly_that()
    {
        using var gh = new GhStub().Answers("Welcome to GitHub CLI!");

        var exception = await Assert.ThrowsAsync<GitHubException>(() =>
            gh.Transport().SendAsync(HttpMethod.Get, "repos/octo/demo"));

        Assert.Equal("The GitHub CLI returned something that wasn't JSON.", exception.Message);
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
    }

    /// <summary>A 204 through the CLI writes nothing at all, and the callers expect an
    /// element rather than an exception.</summary>
    [Fact]
    public async Task An_empty_answer_reads_as_a_null_element()
    {
        using var gh = new GhStub();

        var result = await gh.Transport().SendAsync(HttpMethod.Delete, "repos/octo/demo/issues/7");

        Assert.Equal(JsonValueKind.Null, result.ValueKind);
    }

    [Fact]
    public async Task An_answer_is_handed_back_parsed()
    {
        using var gh = new GhStub().Answers("""{"number":7,"title":"Ship it"}""");

        var result = await gh.Transport().SendAsync(HttpMethod.Get, "repos/octo/demo/issues/7");

        Assert.Equal(7, result.GetProperty("number").GetInt32());
        Assert.Equal("Ship it", result.GetProperty("title").GetString());
    }

    [Fact]
    public void The_transport_describes_itself_as_the_github_cli()
    {
        using var gh = new GhStub();

        Assert.Equal("GitHub CLI", gh.Transport().Description);
    }
}
