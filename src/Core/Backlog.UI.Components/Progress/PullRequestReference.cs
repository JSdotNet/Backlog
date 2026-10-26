using System.Globalization;
using System.Text.RegularExpressions;
using Backlog.UI.Components.Goals;

namespace Backlog.UI.Components.Progress;

/// <summary>
/// What a goal's attach search reads as a pull request
/// (<c>.devbook/domain/progress/features.md#attaching-work-to-a-goal</c>), so one can be
/// attached by its number although no candidate holds it — a merged one, or one the shell's
/// reader did not list. Pure.
/// </summary>
public static partial class PullRequestReference
{
    /// <summary>
    /// The pull requests <paramref name="query"/> names: <c>owner/name#N</c> and a GitHub
    /// <c>…/owner/name/pull/N</c> address name that one, whether or not
    /// <paramref name="repositories"/> lists it; <c>#N</c> and <c>N</c> name one in each of
    /// <paramref name="repositories"/>, in their order — the one repository in scope when the
    /// host hands in one. Anything else, and a number that is not a positive whole one, names
    /// none. A repository <paramref name="repositories"/> lists is written its way, so case
    /// typed differently still names the same pull request.
    /// </summary>
    /// <param name="repositories">The repositories a bare number is read in, <c>owner/name</c>
    /// each; blanks and repeats, case aside, are skipped.</param>
    public static IReadOnlyList<GoalSubject> Resolve(string? query, IReadOnlyList<string> repositories)
    {
        ArgumentNullException.ThrowIfNull(repositories);

        var text = query?.Trim() ?? string.Empty;
        if (text.Length == 0) return [];

        var listed = repositories
            .Select(repository => repository?.Trim() ?? string.Empty)
            .Where(repository => repository.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (Bare().Match(text) is { Success: true } bare)
        {
            return Number(bare) is { } number
                ? [.. listed.Select(repository => GoalSubject.ForPullRequest(repository, number))]
                : [];
        }

        var named = Named().Match(text) is { Success: true } written ? written : Address().Match(text);
        if (!named.Success || Number(named) is not { } n) return [];

        var typed = $"{named.Groups["owner"].Value}/{named.Groups["name"].Value}";
        var repositoryName = listed.FirstOrDefault(repository => string.Equals(repository, typed, StringComparison.OrdinalIgnoreCase)) ?? typed;
        return [GoalSubject.ForPullRequest(repositoryName, n)];
    }

    private static int? Number(Match match) =>
        int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : null;

    [GeneratedRegex(@"^#?(?<number>\d+)$")]
    private static partial Regex Bare();

    [GeneratedRegex(@"^(?<owner>[\w.-]+)/(?<name>[\w.-]+)#(?<number>\d+)$")]
    private static partial Regex Named();

    [GeneratedRegex(@"^(?:https?://)?(?:www\.)?github\.com/(?<owner>[\w.-]+)/(?<name>[\w.-]+)/pull/(?<number>\d+)(?:[/?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Address();
}
