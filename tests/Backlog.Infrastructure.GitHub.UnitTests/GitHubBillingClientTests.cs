using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The AI-credit usage report, and the two-endpoint fallback that decides where a
/// person's Copilot spend is actually recorded.
/// </summary>
public class GitHubBillingClientTests
{
    private const string OneModel = """
        {
          "usageItems": [
            {
              "product": "copilot",
              "sku": "copilot_ai_credit",
              "model": "gpt-5",
              "unitType": "credit",
              "pricePerUnit": 0.04,
              "grossQuantity": 300,
              "grossAmount": 12.0,
              "discountQuantity": 200,
              "discountAmount": 8.0,
              "netQuantity": 100,
              "netAmount": 4.0
            }
          ]
        }
        """;

    /// <summary>
    /// These endpoints do not exist on the version the rest of the app calls, and a
    /// wrong version answers 404 — which reads like a missing resource rather than a
    /// missing version, and is the failure this pins down.
    /// </summary>
    [Fact]
    public async Task Billing_is_read_on_the_version_the_billing_endpoints_live_on()
    {
        var transport = new RoutingTransport().Returns("/ai_credit/usage", OneModel);

        _ = await Client(transport).GetAiCreditUsageAsync(2026, 8);

        Assert.Contains("2026-03-10", transport.ApiVersions);
    }

    [Fact]
    public async Task Net_figures_are_read_rather_than_gross_ones()
    {
        var transport = new RoutingTransport().Returns("/ai_credit/usage", OneModel);

        var usage = await Client(transport).GetAiCreditUsageAsync(2026, 8);

        var item = Assert.Single(usage.Items);

        // Net is what was charged; gross is before the plan's included credits are
        // applied, so reporting gross would bill you for what the subscription paid.
        Assert.Equal(4.0m, usage.NetAmount);
        Assert.Equal(100m, usage.NetQuantity);
        Assert.Equal(12.0m, item.GrossAmount);
        Assert.Equal(8.0m, item.DiscountAmount);
        Assert.Equal("gpt-5", item.Model);
    }

    /// <summary>Billing payloads have been seen with amounts as strings as well as
    /// numbers, and a silent zero would be indistinguishable from a quiet month.</summary>
    [Fact]
    public async Task An_amount_reported_as_a_string_is_still_read_as_money()
    {
        var transport = new RoutingTransport().Returns("/ai_credit/usage", """
            { "usageItems": [ { "model": "gpt-5", "netAmount": "4.25", "netQuantity": "106" } ] }
            """);

        var usage = await Client(transport).GetAiCreditUsageAsync(2026, 8);

        Assert.Equal(4.25m, usage.NetAmount);
        Assert.Equal(106m, usage.NetQuantity);
    }

    [Fact]
    public async Task A_personal_plan_is_read_from_the_user_endpoint_and_the_organization_is_never_asked()
    {
        var transport = new RoutingTransport().Returns("users/jsdotnet/settings/billing/ai_credit/usage", OneModel);

        var usage = await Client(transport).GetAiCreditUsageAsync(2026, 8);

        Assert.Equal(GitHubBillingScope.PersonalAccount, usage.Scope);
        Assert.Equal(0, transport.CallsTo("organizations/"));
    }

    /// <summary>
    /// A month with no usage on a personal plan is a real answer — you used nothing —
    /// and must not fall through to an organization's figures, which would report
    /// somebody else's spend as yours.
    /// </summary>
    [Fact]
    public async Task An_empty_personal_report_ends_the_search_rather_than_falling_through()
    {
        var transport = new RoutingTransport()
            .Returns("users/jsdotnet/settings/billing/ai_credit/usage", """{ "usageItems": [] }""")
            .Returns("organizations/", OneModel);

        var usage = await Client(transport).GetAiCreditUsageAsync(2026, 8);

        Assert.Empty(usage.Items);
        Assert.Equal(GitHubBillingScope.PersonalAccount, usage.Scope);
        Assert.Equal(0, transport.CallsTo("organizations/"));
    }

    /// <summary>
    /// A seat an organization pays for is billed to that organization and does not
    /// appear on the user endpoint at all, so a refusal there is what makes the
    /// organization endpoint worth asking.
    /// </summary>
    [Fact]
    public async Task An_organization_billed_seat_is_read_from_the_organization_endpoint_filtered_to_the_login()
    {
        var transport = new RoutingTransport()
            .Refuses("users/jsdotnet/settings/billing")
            .Returns("organizations/JSdotNet/settings/billing/ai_credit/usage", OneModel);

        var usage = await Client(transport).GetAiCreditUsageAsync(2026, 8);

        Assert.Equal(GitHubBillingScope.Organization, usage.Scope);
        Assert.Equal(4.0m, usage.NetAmount);
        Assert.Contains(transport.Paths, path => path.Contains("user=jsdotnet", StringComparison.Ordinal));
    }

    /// <summary>
    /// For an organization owned by an enterprise, GitHub refuses the per-user filter
    /// to organization admins and only answers it at enterprise level. A dashboard
    /// showing nothing spent would be making a claim, so the client says what
    /// happened instead.
    /// </summary>
    [Fact]
    public async Task Both_endpoints_refusing_says_so_rather_than_reporting_nothing_spent()
    {
        var transport = new RoutingTransport()
            .Refuses("users/")
            .Refuses("organizations/");

        var failure = await Assert.ThrowsAsync<GitHubException>(
            () => Client(transport).GetAiCreditUsageAsync(2026, 8));

        Assert.Contains("enterprise", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("admin", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task With_no_organization_configured_the_message_says_that_instead()
    {
        var transport = new RoutingTransport().Refuses("users/");

        var failure = await Assert.ThrowsAsync<GitHubException>(
            () => Client(transport, repositories: null).GetAiCreditUsageAsync(2026, 8));

        Assert.Contains("no organization is configured", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_day_is_asked_for_only_when_one_is_wanted()
    {
        var transport = new RoutingTransport().Returns("/ai_credit/usage", OneModel);

        _ = await Client(transport).GetAiCreditUsageAsync(2026, 8, day: 12);

        Assert.Contains(transport.Paths, path => path.Contains("day=12", StringComparison.Ordinal));

        var monthOnly = new RoutingTransport().Returns("/ai_credit/usage", OneModel);
        _ = await Client(monthOnly).GetAiCreditUsageAsync(2026, 8);

        Assert.DoesNotContain(monthOnly.Paths, path => path.Contains("day=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_an_identity_there_is_no_account_to_read_billing_for()
    {
        var transport = new RoutingTransport().Returns("/ai_credit/usage", OneModel);
        var client = new GitHubBillingClient(transport, new StubIdentity(null), Settings("JSdotNet/Backlog"));

        var availability = await client.GetAvailabilityAsync();

        Assert.False(availability.IsAvailable);
        Assert.Contains("who you are signed in as", availability.Reason, StringComparison.Ordinal);

        _ = await Assert.ThrowsAsync<GitHubNotConfiguredException>(() => client.GetAiCreditUsageAsync(2026, 8));
    }

    [Fact]
    public async Task An_unreachable_transport_explains_itself_rather_than_throwing()
    {
        var availability = await Client(new RoutingTransport { Available = false }).GetAvailabilityAsync();

        Assert.False(availability.IsAvailable);
        Assert.Contains("gh auth login", availability.Reason, StringComparison.Ordinal);
    }

    private static GitHubBillingClient Client(RoutingTransport transport, string? repositories = "JSdotNet/Backlog") =>
        new(transport, new StubIdentity("jsdotnet"), Settings(repositories));

    private static GitHubSettingsStore Settings(string? repositories)
    {
        var path = Path.Combine(Path.GetTempPath(), "backlog-billing-tests", Guid.NewGuid().ToString("N"), "github.json");
        var store = new GitHubSettingsStore(path);

        if (repositories is null) return store;

        var (parsed, errors) = GitHubSettings.ParseText(repositories);
        Assert.Empty(errors);
        Assert.Null(store.SetRepositories(parsed));

        return store;
    }

    private sealed class StubIdentity(string? login) : IGitHubIdentityClient
    {
        public Task<string?> GetLoginAsync(CancellationToken cancellationToken = default) => Task.FromResult(login);
    }
}
