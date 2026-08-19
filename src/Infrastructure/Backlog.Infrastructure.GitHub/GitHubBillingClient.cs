using System.Globalization;
using System.Text.Json;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// One line of an AI-credit usage report: a model, how many credits it consumed,
/// and what GitHub charged for them.
/// </summary>
/// <remarks>
/// <para>
/// GitHub does the credit-to-money arithmetic. Copilot moved to usage-based billing
/// on 1 June 2026 — premium request units became AI credits, metered on token
/// consumption at each model's published rates — and the report returns both the
/// quantity and the amount. Backlog reads them; it does not hold a price table,
/// because a price table in an app is a price table that goes stale.
/// </para>
/// <para>
/// Net rather than gross is what a reader means by "spend": the gross figure is
/// before the plan's included allowance is applied, so it charges you for credits
/// the subscription already paid for.
/// </para>
/// </remarks>
public sealed record GitHubAiCreditUsageItem(
    string? Product,
    string? Sku,
    string? Model,
    string? UnitType,
    decimal PricePerUnit,
    decimal GrossQuantity,
    decimal GrossAmount,
    decimal DiscountQuantity,
    decimal DiscountAmount,
    decimal NetQuantity,
    decimal NetAmount);

/// <summary>Whose billing the report was read from.</summary>
public enum GitHubBillingScope
{
    /// <summary>Not worked out yet, or nothing could be read.</summary>
    Unknown,

    /// <summary>The signed-in account's own plan, from
    /// <c>/users/{username}/settings/billing</c>.</summary>
    PersonalAccount,

    /// <summary>A seat an organization pays for, from
    /// <c>/organizations/{org}/settings/billing</c> filtered to one user.</summary>
    Organization
}

/// <summary>One month, or one day of it, of AI-credit usage.</summary>
public sealed record GitHubAiCreditUsage(
    IReadOnlyList<GitHubAiCreditUsageItem> Items,
    GitHubBillingScope Scope)
{
    public static GitHubAiCreditUsage Empty { get; } = new([], GitHubBillingScope.Unknown);

    /// <summary>What was actually charged, across every line.</summary>
    public decimal NetAmount => Items.Sum(item => item.NetAmount);

    /// <summary>Credits consumed after the included allowance, across every line.</summary>
    public decimal NetQuantity => Items.Sum(item => item.NetQuantity);
}

/// <summary>Why AI-credit reporting is or is not usable, in words fit for a screen.</summary>
public sealed record GitHubBillingAvailability(bool IsAvailable, string Reason, GitHubBillingScope Scope);

/// <summary>
/// The billing questions Backlog asks GitHub: what did this person's assistant
/// usage cost.
/// </summary>
public interface IGitHubBillingClient
{
    Task<GitHubBillingAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// AI-credit usage for a calendar month, or for one day of it when
    /// <paramref name="day"/> is given.
    /// </summary>
    Task<GitHubAiCreditUsage> GetAiCreditUsageAsync(
        int year,
        int month,
        int? day = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IGitHubBillingClient"/> over the enhanced billing platform's
/// AI-credit usage report.
/// </summary>
/// <remarks>
/// <para>
/// These endpoints only exist from API version <c>2026-03-10</c>, which is why the
/// transport takes a per-request version at all. Calling them on the version the
/// rest of the app uses answers 404, which reads like a missing resource rather
/// than a missing version.
/// </para>
/// <para>
/// Two endpoints, tried in order, because which one holds the figures depends on
/// who pays for the seat. The user endpoint covers a Copilot plan bought on a
/// personal account. A seat an organization pays for does not appear there at all —
/// its usage bills to the organization — so the organization endpoint is tried next
/// with the login as a filter. For an organization owned by an enterprise, GitHub
/// refuses that filter to organization admins and only answers it at enterprise
/// level; that refusal is reported as a reason rather than as a zero, because a
/// dashboard that shows nothing spent is making a claim.
/// </para>
/// </remarks>
public sealed class GitHubBillingClient(
    IGitHubTransport transport,
    IGitHubIdentityClient identity,
    GitHubSettingsStore settings) : IGitHubBillingClient
{
    /// <summary>The version the billing usage reports live on.</summary>
    internal const string BillingApiVersion = "2026-03-10";

    /// <summary>
    /// GitHub's billing reports amounts without naming a currency; the enhanced
    /// billing platform reports United States dollars.
    /// <para>
    /// Public because an adapter has to attach it — the figure must never reach a
    /// screen without a currency beside it, and the alternative is every caller
    /// writing the string itself and one of them eventually writing a different one.
    /// </para>
    /// </summary>
    public const string Currency = "USD";

    public async Task<GitHubBillingAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        if (!await transport.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            return new GitHubBillingAvailability(
                false,
                "Backlog cannot reach GitHub. Sign in with `gh auth login`, or add a personal access token in "
                + "repository settings.",
                GitHubBillingScope.Unknown);
        }

        var login = await identity.GetLoginAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(login))
        {
            return new GitHubBillingAvailability(
                false,
                "GitHub did not say who you are signed in as, so there is no account to read billing for.",
                GitHubBillingScope.Unknown);
        }

        return new GitHubBillingAvailability(
            true,
            $"Reading your Copilot AI-credit usage as {login} with the {transport.Description}.",
            GitHubBillingScope.Unknown);
    }

    public async Task<GitHubAiCreditUsage> GetAiCreditUsageAsync(
        int year,
        int month,
        int? day = null,
        CancellationToken cancellationToken = default)
    {
        var login = await identity.GetLoginAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new GitHubNotConfiguredException(
                "GitHub did not say who you are signed in as, so there is no account to read billing for.");

        var query = $"?year={year.ToString(CultureInfo.InvariantCulture)}"
            + $"&month={month.ToString(CultureInfo.InvariantCulture)}"
            + (day is { } chosen ? $"&day={chosen.ToString(CultureInfo.InvariantCulture)}" : string.Empty);

        var personal = await TryReadAsync(
            $"users/{Uri.EscapeDataString(login)}/settings/billing/ai_credit/usage{query}",
            GitHubBillingScope.PersonalAccount,
            cancellationToken).ConfigureAwait(false);

        // An empty personal report is not the same as no personal report: somebody
        // on a personal plan who used nothing this month should see zero, not an
        // organization's figures. Only a refusal falls through.
        if (personal.Read) return personal.Usage;

        var organizations = Organizations();

        foreach (var organization in organizations)
        {
            var owned = await TryReadAsync(
                $"organizations/{Uri.EscapeDataString(organization)}/settings/billing/ai_credit/usage"
                    + $"{query}&user={Uri.EscapeDataString(login)}",
                GitHubBillingScope.Organization,
                cancellationToken).ConfigureAwait(false);

            if (owned.Read) return owned.Usage;
        }

        throw new GitHubException(
            organizations.Count == 0
                ? "GitHub would not report AI-credit usage for your account, and no organization is configured to "
                    + "ask instead. A Copilot seat paid for by an organization is billed to that organization, so "
                    + "the usage has to be read there."
                : "GitHub would not report your AI-credit usage, for your account or for "
                    + string.Join(", ", organizations)
                    + ". Reading one person's usage in an organization needs organization admin rights, and for an "
                    + "organization owned by an enterprise GitHub only answers it at enterprise level.");
    }

    /// <summary>
    /// Reads one endpoint, distinguishing "GitHub answered" from "GitHub refused".
    /// A refusal is what makes the next endpoint worth trying; an answer, even an
    /// empty one, ends the search.
    /// </summary>
    private async Task<(bool Read, GitHubAiCreditUsage Usage)> TryReadAsync(
        string path,
        GitHubBillingScope scope,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await transport.SendAsync(
                HttpMethod.Get,
                path,
                body: null,
                apiVersion: BillingApiVersion,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return (true, Read(response, scope));
        }
        catch (GitHubException)
        {
            return (false, GitHubAiCreditUsage.Empty);
        }
        catch (GitHubNotConfiguredException)
        {
            return (false, GitHubAiCreditUsage.Empty);
        }
    }

    /// <summary>Which organizations to try, in the order they are configured.</summary>
    private List<string> Organizations() =>
        [.. settings.Current.Repositories
            .Select(repository => repository.Owner)
            .Where(owner => !string.IsNullOrWhiteSpace(owner))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    internal static GitHubAiCreditUsage Read(JsonElement response, GitHubBillingScope scope)
    {
        // Either shape means "answered, with nothing in it", so the scope is kept:
        // which endpoint answered is worth knowing even when the month was quiet.
        if (response.ValueKind != JsonValueKind.Object
            || !response.TryGetProperty("usageItems", out var rows)
            || rows.ValueKind != JsonValueKind.Array)
        {
            return new GitHubAiCreditUsage([], scope);
        }

        var items = new List<GitHubAiCreditUsageItem>();

        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) continue;

            items.Add(new GitHubAiCreditUsageItem(
                String(row, "product"),
                String(row, "sku"),
                String(row, "model"),
                String(row, "unitType"),
                Decimal(row, "pricePerUnit"),
                Decimal(row, "grossQuantity"),
                Decimal(row, "grossAmount"),
                Decimal(row, "discountQuantity"),
                Decimal(row, "discountAmount"),
                Decimal(row, "netQuantity"),
                Decimal(row, "netAmount")));
        }

        return new GitHubAiCreditUsage(items, scope);
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Reads a money or quantity figure. Decimal rather than double: an amount has
    /// to reach a currency label without a round trip through binary floating point.
    /// A string is accepted as well as a number because billing payloads have been
    /// seen both ways.
    /// </summary>
    private static decimal Decimal(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return 0m;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(
                value.GetString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => 0m
        };
    }
}
