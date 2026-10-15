using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.Services;

/// <summary>
/// The size buckets merged pull requests are cut into by changed lines, and the cut.
/// </summary>
/// <remarks>
/// <para>
/// The edges are the ceilings, exclusive: under 10 lines is XS, under 50 S, under 200 M,
/// under 400 L, and anything from 400 up XL. They are kept beside their labels, as
/// <see cref="ProductivityScoring"/> keeps its thresholds beside the label that names them,
/// so a bucket cannot drift from what it says it holds.
/// </para>
/// <para>
/// The Quality score's "Merged under 400 changed lines" counts 400 itself as small, where
/// the XL bucket here starts at 400. The two are separate readings and are left as they are.
/// </para>
/// </remarks>
internal static class PullRequestSizes
{
    private const int ExtraSmallBelow = 10;

    private const int SmallBelow = 50;

    private const int MediumBelow = 200;

    private const int LargeBelow = 400;

    private static readonly (string Label, int MinLines, int? MaxLinesExclusive)[] Buckets =
    [
        ("XS", 0, ExtraSmallBelow),
        ("S", ExtraSmallBelow, SmallBelow),
        ("M", SmallBelow, MediumBelow),
        ("L", MediumBelow, LargeBelow),
        ("XL", LargeBelow, null)
    ];

    /// <summary>
    /// Every bucket, in order and empty ones included, over the pull requests whose size
    /// was read. A pull request whose size was not read is in none of them.
    /// </summary>
    /// <remarks>
    /// The share's predicate and denominator are the rework rate's: the bucket's pull
    /// requests that came back after review, out of those that were reviewed at all.
    /// </remarks>
    public static IReadOnlyList<ReworkSize> Cut(IEnumerable<ActivityPullRequest> pullRequests)
    {
        ArgumentNullException.ThrowIfNull(pullRequests);

        var sized = pullRequests.Where(pr => pr.SizeKnown).ToList();

        return
        [
            .. Buckets.Select(bucket =>
            {
                var inBucket = sized
                    .Where(pr => pr.ChangedLines >= bucket.MinLines
                        && (bucket.MaxLinesExclusive is not { } ceiling || pr.ChangedLines < ceiling))
                    .ToList();
                var reviewed = inBucket.Where(pr => pr.FirstReviewedAt is not null).ToList();

                return new ReworkSize(
                    bucket.Label,
                    bucket.MinLines,
                    bucket.MaxLinesExclusive,
                    inBucket.Count,
                    reviewed.Count,
                    reviewed.Count(pr => pr.HasChurn));
            })
        ];
    }
}
