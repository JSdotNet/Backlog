using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;

namespace Backlog.Modules.RepositoryManagement.UnitTests.Fakes;

/// <summary>Pull request statuses as the cache and command tests state them.</summary>
internal static class Statuses
{
    public const string BacklogRepository = "JSdotNet/Backlog";

    public const string ArchifyRepository = "JSdotNet/Archify";

    public static readonly DateTimeOffset Noon = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    public static PullRequestStatus Pull(
        int number,
        PullRequestState state = PullRequestState.Open,
        string repository = BacklogRepository,
        string? head = null,
        string baseRef = "main") =>
        new(
            repository,
            number,
            $"https://github.com/{repository}/pull/{number}",
            $"Pull request {number}",
            state,
            head ?? $"feature-{number}",
            baseRef,
            "octocat",
            ViewerDidAuthor: true,
            Noon.AddMinutes(-number))
        {
            MergedAt = state == PullRequestState.Merged ? Noon.AddHours(-1) : null,
            DefaultBranch = state == PullRequestState.Merged ? "main" : null,
            Checks = PullRequestCheckState.Passing,
            Handle = new PullRequestHandle($"PR_{number}", $"sha-{number}", PullRequestMergeMethod.Squash)
        };

    public static PullRequestReference Ref(int number, string repository = BacklogRepository) => new(repository, number);
}
