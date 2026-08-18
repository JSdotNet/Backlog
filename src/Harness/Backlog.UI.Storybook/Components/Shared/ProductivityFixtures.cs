using Backlog.UI.Components.Metrics;

namespace Backlog.UI.Storybook.Components.Shared;

/// <summary>
/// Twelve weeks of a productivity score across five repositories.
/// </summary>
/// <remarks>
/// <para>
/// Generated from a shape rather than typed out. Sixty readings written by hand
/// would be sixty chances to fat-finger a digit, and the shape is what the stories
/// are about: each repository needs a recognisable trajectory — one climbing, one
/// falling, one flat, one erratic, one that started late — so a reviewer can check
/// that the chart is showing the trajectory the data has.
/// </para>
/// <para>
/// Deterministic on purpose. No <c>Random</c> without a seed and no clock: the same
/// figures render on every machine and every run, so a screenshot taken today can
/// be compared with one taken next month.
/// </para>
/// <para>
/// Self-consistent on purpose too. The score card, the last trellis panel value,
/// the last heatmap cell and the spotlight legend are all one number, because the
/// series takes its final reading from the score its own inputs produce rather than
/// from the shape. Figures that nearly agree are how a dashboard quietly loses its
/// reader — and a fixture that does not add up hides exactly that bug.
/// </para>
/// </remarks>
internal static class ProductivityFixtures
{
    /// <summary>Twelve weekly buckets. Weeks rather than days: a productivity score
    /// over a single day is mostly noise about which day of the week it was.</summary>
    private static readonly string[] Weeks =
    [
        "W23", "W24", "W25", "W26", "W27", "W28",
        "W29", "W30", "W31", "W32", "W33", "W34"
    ];

    public static IReadOnlyList<string> Buckets { get; } = Weeks;

    /// <summary>The bands the score is read against. Floors only — <c>MetricScoring</c>
    /// matches the highest floor at or below the score, so there are no ceilings to
    /// keep in step with the next band up.</summary>
    public static IReadOnlyList<MetricBand> Bands { get; } =
    [
        new("Struggling", 0m),
        new("Finding its feet", 40m),
        new("Steady", 60m),
        new("Strong", 78m)
    ];

    /// <summary>
    /// What the score is made of, and how much of it each input is worth. Each input
    /// carries what counts as full marks for a fortnight, which is the only way a
    /// score over mixed units means anything: without a max, lines changed would bury
    /// pull requests every time.
    /// </summary>
    /// <remarks>
    /// These weights are a starting position and nothing more. The point of putting
    /// them on screen is that they are arguable, and the first useful conversation a
    /// score like this starts is someone saying the throughput weight is too high.
    /// </remarks>
    private static readonly (string Label, decimal Max, decimal Weight)[] Inputs =
    [
        ("Pull requests merged", 18m, 3m),
        ("Issues closed", 30m, 2m),
        ("Review turnaround under a day", 22m, 2m),
        ("Builds green on first run", 52m, 1m)
    ];

    /// <summary>One repository, its shape, and what it is like — the description is
    /// what a reviewer checks the chart against.</summary>
    private sealed record Shape(string Name, string Detail, decimal Start, decimal Drift, decimal Wobble, int StartsAt = 0);

    private static readonly Shape[] Shapes =
    [
        new("backlog", "climbing steadily", 54m, 2.1m, 3m),
        new("backlog-cloud", "flat, and fine", 71m, 0m, 2m),
        new("backlog-ide", "slipping since W27", 76m, -2.4m, 3m),
        new("design-system", "erratic week to week", 58m, 0.4m, 13m),
        new("spike-agents", "only started at W29", 44m, 4.2m, 4m, StartsAt: 6)
    ];

    /// <summary>
    /// The score per repository per week. The wobble is a fixed sine rather than a
    /// random walk so the series is reproducible, and it is deliberately large for
    /// one repository — an erratic series is what catches a chart that draws every
    /// panel to its own scale.
    /// </summary>
    /// <remarks>
    /// The final reading is the score the repository's inputs actually produce rather
    /// than the shape's own value, so nothing on the page contradicts the score card
    /// by a rounding step.
    /// </remarks>
    public static IReadOnlyList<MetricSeries> ByRepository { get; } =
    [
        .. Shapes.Select(shape =>
        {
            var weeks = Weeks.Skip(shape.StartsAt).ToArray();
            var last = weeks.Length - 1;

            return new MetricSeries(
                shape.Name,
                [
                    .. weeks.Select((week, offset) => new MetricPoint(
                        week,
                        offset == last
                            ? MetricScoring.Score(InputsFor(Raw(shape, offset)))
                            : Math.Round(Raw(shape, offset), 1)))
                ],
                shape.Detail);
        })
    ];

    /// <summary>The repository the page opens on — the one with the trajectory worth
    /// looking at rather than the first alphabetically.</summary>
    public const string Subject = "backlog-ide";

    /// <summary>Every repository name, for a selector.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Shapes.Select(shape => shape.Name)];

    /// <summary>The inputs behind one repository's current score.</summary>
    public static IReadOnlyList<MetricScoreComponent> ScoreInputsFor(string repository) =>
        InputsFor(RawFor(repository, weeksBack: 0));

    /// <summary>
    /// One repository's score movement against the previous fortnight, worked out
    /// rather than asserted. Null when it has not been running long enough to have a
    /// previous fortnight — which the newest repository has not, and a delta against
    /// a period that does not exist is the sort of figure a dashboard should decline
    /// to invent.
    /// </summary>
    public static MetricDelta? ScoreDeltaFor(string repository)
    {
        var shape = Shapes.FirstOrDefault(one => one.Name == repository);

        if (shape is null || Weeks.Length - shape.StartsAt < 3) return null;

        var now = MetricScoring.Score(ScoreInputsFor(repository));
        var before = MetricScoring.Score(InputsFor(RawFor(repository, weeksBack: 2)));

        return before == 0m
            ? null
            : new MetricDelta((now - before) / before, MetricDeltaUnit.Percent, "the previous fortnight");
    }

    /// <summary>The estate's score per week: the mean across whichever repositories
    /// reported that week. Not a sum — a score is already normalised, and adding five
    /// scores together produces a number on no scale at all.</summary>
    public static IReadOnlyList<MetricPoint> EstateScore { get; } =
    [
        .. Weeks.Select(week =>
        {
            var readings = ByRepository
                .SelectMany(series => series.Points.Where(point => point.Label == week))
                .Select(point => point.Value)
                .ToList();

            return new MetricPoint(week, readings.Count == 0 ? 0m : Math.Round(readings.Average(), 1));
        })
    ];


    /// <summary>
    /// Hours logged per repository per week — what a timesheet would hold, not
    /// percentages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Raw hours on purpose: <c>MetricStackedArea</c> normalises, so the fixture holds
    /// what a source would actually report and the chart does the dividing. A fixture
    /// of pre-computed percentages would also hide the bug worth catching here, which
    /// is a week that does not add to 100.
    /// </para>
    /// <para>
    /// W32 is deliberately empty — a week nobody logged anything, which is not the
    /// same as a week split evenly. The chart has to show a gap there rather than
    /// bridging across it, and the newest repository has no hours before W29 at all.
    /// </para>
    /// </remarks>
    private static readonly (string Name, decimal[] Hours)[] Timesheet =
    [
        ("backlog",       [14m, 16m, 18m, 21m, 22m, 19m, 17m, 15m, 14m, 0m, 12m, 11m]),
        ("backlog-cloud", [11m,  9m,  8m,  6m,  5m,  4m,  4m,  3m,  3m, 0m,  2m,  2m]),
        ("backlog-ide",   [ 8m,  7m,  6m,  5m,  4m,  4m,  3m,  3m,  2m, 0m,  2m,  1m]),
        ("design-system", [ 5m,  6m,  5m,  6m,  7m,  6m,  5m,  4m,  6m, 0m,  5m,  4m]),
        ("spike-agents",  [ 0m,  0m,  0m,  0m,  0m,  0m,  9m, 13m, 14m, 0m, 17m, 20m])
    ];

    /// <summary>
    /// The timesheet as series. A repository with no hours in a week contributes
    /// nothing to it, which is a share of zero; a week where nobody logged anything is
    /// a bucket with no total, which is a gap.
    /// </summary>
    public static IReadOnlyList<MetricSeries> TimeByRepository { get; } =
    [
        .. Timesheet.Select(entry => new MetricSeries(
            entry.Name,
            [.. Weeks.Select((week, index) => new MetricPoint(week, entry.Hours[index]))],
            $"{entry.Hours.Sum():0.#}h over the period"))
    ];

    /// <summary>Hours, the way a timesheet reads them.</summary>
    public static string Hours(decimal value) =>
        value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "h";

    /// <summary>Windows a reader might pick. The label is what a control shows and the
    /// count is how many of the twelve weeks it keeps.</summary>
    public static IReadOnlyList<(string Label, int Weeks)> Windows { get; } =
    [
        ("6 weeks", 6),
        ("12 weeks", 12)
    ];

    /// <summary>The last <paramref name="weeks"/> buckets of every series. What a
    /// window control actually does — and note the score's scale does not change with
    /// it, because a score is out of 100 whatever window it is read over.</summary>
    public static IReadOnlyList<MetricSeries> Window(IReadOnlyList<MetricSeries> series, int weeks) =>
        [.. series.Select(one => one with
        {
            Points = [.. one.Points.Skip(Math.Max(0, one.Points.Count - weeks))]
        })];

    /// <summary>
    /// The inputs that produce roughly <paramref name="target"/> out of 100.
    /// </summary>
    /// <remarks>
    /// Readings are whole numbers because the things being counted are whole things:
    /// there is no such thing as 13.4 merged pull requests. Rounding lands the score
    /// a fraction either side of the target, which is why the series takes its final
    /// reading from this function rather than the other way round — the number on the
    /// score card and the number at the end of the chart have to be the same number,
    /// and the only way to guarantee that is to derive one from the other.
    /// </remarks>
    private static IReadOnlyList<MetricScoreComponent> InputsFor(decimal target)
    {
        var share = Math.Clamp(target, 0m, MetricScoring.MaxScore) / MetricScoring.MaxScore;

        return
        [
            .. Inputs.Select(input => new MetricScoreComponent(
                input.Label,
                Math.Round(share * input.Max, 0, MidpointRounding.AwayFromZero),
                input.Max,
                input.Weight))
        ];
    }

    /// <summary>The shape's value at one offset from its own start, held on the score
    /// scale.</summary>
    private static decimal Raw(Shape shape, int offset)
    {
        var raw = shape.Start + shape.Drift * offset + shape.Wobble * (decimal)Math.Sin(offset * 1.7);

        return Math.Clamp(raw, 0m, MetricScoring.MaxScore);
    }

    /// <summary>One repository's shape value, counted back from its latest week.</summary>
    private static decimal RawFor(string repository, int weeksBack)
    {
        var shape = Shapes.FirstOrDefault(one => one.Name == repository);

        if (shape is null) return 0m;

        var last = Weeks.Length - shape.StartsAt - 1;

        return Raw(shape, Math.Max(0, last - weeksBack));
    }
}
