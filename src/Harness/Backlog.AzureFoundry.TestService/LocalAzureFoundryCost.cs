using System.Globalization;
using System.Text.Json;

namespace Backlog.AzureFoundry.TestService;

/// <summary>
/// The stand-in for Azure Cost Management's query on a Foundry resource: a
/// deterministic bill for whatever window the request names, in the shape the
/// real API answers with, so the dashboard's Cost section has figures to draw
/// without an Azure sign-in.
/// </summary>
/// <remarks>
/// The amounts are a function of the day alone, so the same window always draws
/// the same chart and a screenshot taken today matches one taken tomorrow. Three
/// meters, named the way Azure names a model deployment's — the model, then the
/// direction of the tokens — and in euros, because the point of a third provider
/// beside two that bill in dollars is that the dashboard says so rather than
/// summing them.
/// </remarks>
public static class LocalAzureFoundryCost
{
    public const string Currency = "EUR";

    private static readonly string[] Meters =
    [
        "gpt-5.4 Input Tokens",
        "gpt-5.4 Output Tokens",
        "text-embedding-3-large Tokens"
    ];

    /// <summary>The window the request asked for, or null when the body is not a
    /// Cost Management query. Only the time period is read; the stand-in answers
    /// every scope and every grouping with the same daily-by-meter bill.</summary>
    public static (DateOnly From, DateOnly To)? ReadWindow(JsonElement body)
    {
        if (!body.TryGetProperty("timePeriod", out var period)) return null;
        if (!TryReadDay(period, "from", out var from) || !TryReadDay(period, "to", out var to)) return null;

        return from <= to ? (from, to) : null;
    }

    /// <summary>The response body: the four columns the client reads, and one row
    /// per day per meter with a charge on it.</summary>
    public static object CreateResponse((DateOnly From, DateOnly To) window)
    {
        var rows = new List<object[]>();

        for (var day = window.From; day <= window.To; day = day.AddDays(1))
        {
            for (var meter = 0; meter < Meters.Length; meter++)
            {
                var amount = Amount(day, meter);
                if (amount == 0m) continue;

                rows.Add([amount, int.Parse(day.ToString("yyyyMMdd", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture), Meters[meter], Currency]);
            }
        }

        return new
        {
            id = "local-cost-query",
            name = "local-cost-query",
            type = "Microsoft.CostManagement/query",
            properties = new
            {
                nextLink = (string?)null,
                columns = new[]
                {
                    new { name = "Cost", type = "Number" },
                    new { name = "UsageDate", type = "Number" },
                    new { name = "Meter", type = "String" },
                    new { name = "Currency", type = "String" }
                },
                rows
            }
        };
    }

    /// <summary>A working week's worth of charge, nothing at the weekend, with
    /// output tokens costing more than input and embeddings costing little — so
    /// the by-model table has an order worth reading.</summary>
    private static decimal Amount(DateOnly day, int meter)
    {
        if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return 0m;

        var weight = meter switch { 0 => 0.6m, 1 => 1.8m, _ => 0.05m };
        var wobble = 1m + (day.Day % 5) * 0.1m;

        return decimal.Round(weight * wobble, 4);
    }

    private static bool TryReadDay(JsonElement period, string name, out DateOnly day)
    {
        day = default;

        return period.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var moment)
            && (day = DateOnly.FromDateTime(moment.UtcDateTime)) != default;
    }
}
