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
/// <para>
/// One login per identity this machine holds: whoever <c>gh</c> is signed in as,
/// and every account configured on the Accounts tab. A person with two GitHub
/// accounts has two Copilot bills, and the figure the dashboard shows is their
/// sum. Each login's search is the two-endpoint one above, and a
/// <c>users/{login}</c> path binds to that login's own credential and endpoint
/// through the transport, so a second account on another host is read there.
/// </para>
/// <para>
/// <paramref name="months"/> is why a seven-month trend costs one call rather
/// than seven. A month that ended more than <see cref="SettlementDays"/> ago is
/// read once per login and remembered; only the running month, and the one just
/// ended while GitHub is still metering it, are asked for again. Optional
/// because it is an optimization, and <paramref name="time"/> exists only to say
/// which months those are — a test pins it, a host leaves it on the wall clock.
/// </para>
/// </remarks>
public sealed class GitHubBillingClient(
    IGitHubTransport transport,
    IGitHubIdentityClient identity,
    GitHubSettingsStore settings,
    IAiCreditUsageCache? months = null,
    TimeProvider? time = null) : IGitHubBillingClient
{
    /// <summary>The version the billing usage reports live on.</summary>
    internal const string BillingApiVersion = "2026-03-10";

    /// <summary>
    /// How many days past its end a month has to be before its report is treated as
    /// final. GitHub's usage metering lags the usage by hours, not days, and the
    /// first of a month is when the previous one is most likely still being closed
    /// out; three days is comfortably past that and costs one extra month of calls
    /// for three days in thirty.
    /// </summary>
    internal const int SettlementDays = 3;

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

        var logins = await LoginsAsync(cancellationToken).ConfigureAwait(false);

        if (logins.Count == 0)
        {
            return new GitHubBillingAvailability(
                false,
                "GitHub did not say who you are signed in as, so there is no account to read billing for.",
                GitHubBillingScope.Unknown);
        }

        return new GitHubBillingAvailability(
            true,
            $"Reading your Copilot AI-credit usage as {string.Join(", ", logins)} with the {transport.Description}.",
            GitHubBillingScope.Unknown);
    }

    public async Task<GitHubAiCreditUsage> GetAiCreditUsageAsync(
        int year,
        int month,
        int? day = null,
        CancellationToken cancellationToken = default)
    {
        var logins = await LoginsAsync(cancellationToken).ConfigureAwait(false);

        if (logins.Count == 0)
        {
            throw new GitHubNotConfiguredException(
                "GitHub did not say who you are signed in as, so there is no account to read billing for.");
        }

        var query = $"?year={year.ToString(CultureInfo.InvariantCulture)}"
            + $"&month={month.ToString(CultureInfo.InvariantCulture)}"
            + (day is { } chosen ? $"&day={chosen.ToString(CultureInfo.InvariantCulture)}" : string.Empty);

        var read = new List<GitHubAiCreditUsage>();
        GitHubException? refused = null;

        foreach (var login in logins)
        {
            try
            {
                read.Add(await ReadOrRecallAsync(login, year, month, day, query, cancellationToken).ConfigureAwait(false));
            }
            catch (GitHubException ex)
            {
                // One account GitHub will not report is a gap, not a reason to
                // blank the others - unless it was the only one, in which case
                // its reason is the answer.
                refused = ex;
            }
        }

        if (read.Count == 0) throw refused!;

        if (read.Count == 1) return read[0];

        // Two bills, one figure. The scope is kept only when every account was
        // read the same way; a personal plan beside an organization seat is not one
        // scope, and Unknown says so rather than picking one.
        var scopes = read.Select(usage => usage.Scope).Distinct().ToList();

        return new GitHubAiCreditUsage(
            [.. read.SelectMany(usage => usage.Items)],
            scopes.Count == 1 ? scopes[0] : GitHubBillingScope.Unknown);
    }

    /// <summary>
    /// The logins to read billing for: the signed-in identity first, then each
    /// configured account, without repeats. Any of them may be absent - a machine
    /// with no <c>gh</c> still has its pasted-token accounts, and one with no
    /// accounts configured is the single-login behaviour this had before, exactly.
    /// </summary>
    private async Task<IReadOnlyList<string>> LoginsAsync(CancellationToken cancellationToken)
    {
        var logins = new List<string>();

        var signedIn = await identity.GetLoginAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(signedIn)) logins.Add(signedIn.Trim());

        foreach (var account in settings.Current.Accounts)
        {
            if (!logins.Any(known => GitHubAccount.IsSameLogin(known, account.Login))) logins.Add(account.Login);
        }

        return logins;
    }

    /// <summary>
    /// Whether a month's report can no longer change: it ended at least
    /// <see cref="SettlementDays"/> days ago, by the clock this client was given.
    /// </summary>
    private bool IsSettled(int year, int month)
    {
        var today = DateOnly.FromDateTime((time ?? TimeProvider.System).GetUtcNow().UtcDateTime);
        var settledOn = new DateOnly(year, month, 1).AddMonths(1).AddDays(SettlementDays);

        return today >= settledOn;
    }

    /// <summary>
    /// One login's month: from the cache when the month is settled and it is
    /// there, and from GitHub when it is not — written back only when settled, so
    /// a running month's figure is never frozen mid-way.
    /// </summary>
    private async Task<GitHubAiCreditUsage> ReadOrRecallAsync(
        string login,
        int year,
        int month,
        int? day,
        string query,
        CancellationToken cancellationToken)
    {
        // A single day of a month is a different report from the month, and not
        // one the dashboard asks for; it goes straight to GitHub rather than
        // teaching the cache a second shape.
        var settled = day is null && months is not null && IsSettled(year, month);

        if (settled && months!.TryRead(login, year, month) is { } remembered) return remembered;

        var read = await ReadLoginAsync(login, query, cancellationToken).ConfigureAwait(false);

        if (settled) months!.Write(login, year, month, read);

        return read;
    }

    /// <summary>One login's search: its own plan, then each configured organization
    /// filtered to it.</summary>
    private async Task<GitHubAiCreditUsage> ReadLoginAsync(string login, string query, CancellationToken cancellationToken)
    {
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
                ? $"GitHub would not report AI-credit usage for {login}, and no organization is configured to "
                    + "ask instead. A Copilot seat paid for by an organization is billed to that organization, so "
                    + "the usage has to be read there."
                : $"GitHub would not report AI-credit usage for {login}, for the account or for "
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
