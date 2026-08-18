using Backlog.UI.Components.Metrics;

namespace Backlog.UI.Storybook.Components.Shared;

/// <summary>
/// The figures the dashboard stories are drawn from: fourteen days of AI usage
/// for a four-person team.
/// </summary>
/// <remarks>
/// <para>
/// Written out by hand rather than taken from <c>Backlog.Infrastructure.Claude</c>,
/// because this project references the component library and the service defaults
/// and nothing else — that missing reference is what proves the library carries no
/// domain. So the shapes are copied, not the types: token counts split four ways
/// the way a messages usage report splits them, costs as an amount plus a currency
/// code the way a cost report reports them, and per-actor rows the way Claude Code
/// analytics returns them.
/// </para>
/// <para>
/// The numbers are internally consistent on purpose. Every breakdown adds up to
/// the same 412.4M tokens and the same 487.20 USD as the tiles, and the daily
/// series sum to those totals too, so a reviewer reading the page can check the
/// components against each other. A dashboard whose tiles and tables disagree is
/// the most common way this kind of view is wrong, and fixture data that does not
/// add up hides it.
/// </para>
/// </remarks>
internal static class UsageFixtures
{
    /// <summary>What the period is called wherever a delta or a footnote names it.</summary>
    public const string Period = "the last 14 days";

    public const string PreviousPeriod = "previous 14 days";

    /// <summary>The currency every cost here is reported in. Named once so no story
    /// can drift onto a second one — a total across two currencies is a wrong
    /// number, which <c>MetricBreakdown</c> refuses to print.</summary>
    public const string Currency = "USD";

    /// <summary>Weekday dates, weekend dips. Fourteen labels is more than the
    /// column chart prints, which is the point: it thins them to seven and always
    /// keeps the last.</summary>
    private static readonly string[] Days =
    [
        "05 Aug", "06 Aug", "07 Aug", "08 Aug", "09 Aug", "10 Aug", "11 Aug",
        "12 Aug", "13 Aug", "14 Aug", "15 Aug", "16 Aug", "17 Aug", "18 Aug"
    ];

    /// <summary>Total tokens per day, in millions. The two pairs of small figures
    /// are weekends.</summary>
    private static readonly decimal[] DailyTokenMillions =
    [
        34.9m, 37.8m, 33.1m, 6.2m, 3.1m, 44.6m, 47.3m,
        42.8m, 36.2m, 39.7m, 4.7m, 2.9m, 48.9m, 30.2m
    ];

    /// <summary>Spend per day, in <see cref="Currency"/>. Tracks the token series
    /// but not proportionally — the expensive model is not used evenly.</summary>
    private static readonly decimal[] DailyCost =
    [
        41.28m, 44.71m, 39.15m, 7.34m, 3.67m, 52.76m, 55.94m,
        50.62m, 42.83m, 46.95m, 5.56m, 3.43m, 57.84m, 35.12m
    ];

    /// <summary>Tokens per day. Real counts, not millions: the components format,
    /// so the fixture holds the figure as reported.</summary>
    public static IReadOnlyList<MetricPoint> TokensPerDay { get; } =
        [.. Days.Select((day, index) => new MetricPoint(day, DailyTokenMillions[index] * 1_000_000m))];

    public static IReadOnlyList<MetricPoint> CostPerDay { get; } =
        [.. Days.Select((day, index) => new MetricPoint(day, DailyCost[index]))];

    /// <summary>Sessions per day — the one series where a rising line is good news
    /// rather than a rising bill.</summary>
    public static IReadOnlyList<MetricPoint> SessionsPerDay { get; } =
    [
        new("05 Aug", 12m), new("06 Aug", 14m), new("07 Aug", 11m), new("08 Aug", 3m),
        new("09 Aug", 1m), new("10 Aug", 15m), new("11 Aug", 17m), new("12 Aug", 14m),
        new("13 Aug", 12m), new("14 Aug", 13m), new("15 Aug", 2m), new("16 Aug", 1m),
        new("17 Aug", 16m), new("18 Aug", 12m)
    ];

    public static long TotalTokens => (long)(DailyTokenMillions.Sum() * 1_000_000m);

    public static MoneyAmount TotalCost { get; } = new(DailyCost.Sum(), Currency);

    public static int TotalSessions => (int)SessionsPerDay.Sum(point => point.Value);

    /// <summary>
    /// The four token kinds, cheapest first, so the bar grows more expensive to the
    /// right. This is the breakdown worth putting on a dashboard: cache reads are a
    /// fraction of the price of uncached input, so a big pale band on the left is
    /// the difference between a bill and a much larger one, and it is invisible in
    /// any view that reports one "tokens" number.
    /// </summary>
    public static IReadOnlyList<MetricPart> TokenKinds { get; } =
    [
        new("Cache read", 318_400_000m),
        new("Input (uncached)", 41_200_000m),
        new("Cache write", 39_100_000m),
        new("Output", 13_700_000m)
    ];

    /// <summary>What share of the <em>input</em> came out of the cache — the number a
    /// team watching its bill actually steers on. Over input rather than over every
    /// token: output is not something the cache could have served, so counting it in
    /// the denominator would understate the rate by a few points for no reason.</summary>
    public static decimal CacheHitRate
    {
        get
        {
            var input = TokenKinds.Where(part => part.Label != "Output").Sum(part => part.Value);

            return input <= 0m ? 0m : 318_400_000m / input;
        }
    }

    /// <summary>Per model, biggest bill first. Opus burns fewer tokens than Sonnet
    /// and costs more than twice as much, which is the whole argument for showing
    /// tokens and cost in the same table instead of picking one.</summary>
    public static IReadOnlyList<MetricRow> ByModel { get; } =
    [
        new("claude-opus-5", 168_400_000L, new MoneyAmount(331.85m, Currency)),
        new("claude-sonnet-5", 201_700_000L, new MoneyAmount(138.42m, Currency)),
        new("claude-haiku-4-5", 42_300_000L, new MoneyAmount(16.93m, Currency))
    ];

    /// <summary>Per person, with what they shipped as the detail line. Sessions,
    /// commits and pull requests are what Claude Code analytics reports beside the
    /// tokens, and they are the only thing on the page that says whether the spend
    /// bought anything.</summary>
    public static IReadOnlyList<MetricRow> ByActor { get; } =
    [
        new("j.schepers", 214_800_000L, new MoneyAmount(268.44m, Currency), "78 sessions · 142 commits · 31 PRs"),
        new("a.dekker", 109_200_000L, new MoneyAmount(121.06m, Currency), "37 sessions · 68 commits · 14 PRs"),
        new("m.visser", 61_700_000L, new MoneyAmount(74.19m, Currency), "20 sessions · 39 commits · 9 PRs"),
        new("s.bakker", 26_700_000L, new MoneyAmount(23.51m, Currency), "8 sessions · 11 commits · 2 PRs")
    ];

    /// <summary>Copilot seats: a login, an editor, a last-activity date and no
    /// figures at all, because GitHub publishes none per seat. The table drops the
    /// token and cost columns rather than printing four em dashes down each of
    /// them, which is the case <c>MetricRow</c>'s nullable measures exist for.</summary>
    public static IReadOnlyList<MetricRow> CopilotSeats { get; } =
    [
        new("j.schepers", Detail: "Business · VS Code · active 2 hours ago"),
        new("a.dekker", Detail: "Business · Visual Studio · active yesterday"),
        new("m.visser", Detail: "Business · VS Code · active 3 days ago"),
        new("s.bakker", Detail: "Business · editor not reported · never active")
    ];

    /// <summary>Month to date against the cap the team set itself.</summary>
    public static decimal BudgetSpent => 612.40m;

    public static decimal BudgetCap => 1_000.00m;

    /// <summary>Where the meter starts warning: eight tenths of the cap, passed as
    /// a value on the same scale rather than as a fraction.</summary>
    public static decimal BudgetThreshold => BudgetCap * 0.8m;

    /// <summary>Spend divided by sessions. Derived rather than written down, because
    /// the two figures it comes from are on the same page and a hand-typed third one
    /// drifts the moment either changes.</summary>
    public static MoneyAmount CostPerSession { get; } =
        new(Math.Round(DailyCost.Sum() / SessionsPerDay.Sum(point => point.Value), 2), Currency);

    /// <summary>Money, with the currency kept and never turned into a symbol.</summary>
    public static string Money(decimal amount) => MetricFormat.Money(new MoneyAmount(amount, Currency));

    /// <summary>The verbatim reason a usage client gives when it has no credential —
    /// the state this UI is in until someone configures one, and the most common
    /// thing these components will be asked to render.</summary>
    public const string NoCredential =
        "No GitHub credential is available. Copilot usage needs an organization and a token "
        + "with owner-level access to it.";
}
