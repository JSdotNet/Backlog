namespace Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;

/// <summary>
/// How old an answer a reader will take (local ADR 0042 §3): a kept answer younger
/// than <paramref name="MaxAge"/> is reused, and a read still out that began within it
/// is waited on rather than sent again. A merged pull request, and a number GitHub
/// answered without, are kept whatever the age.
/// </summary>
public sealed record PullRequestReadOptions(TimeSpan MaxAge)
{
    /// <summary>One minute: what every reader shared before there was one cache, and
    /// short enough that a list a person is looking at is not passed off as current
    /// after GitHub has moved on.</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(1);

    /// <summary>Any answer up to <see cref="FreshFor"/> old.</summary>
    public static PullRequestReadOptions Default { get; } = new(FreshFor);

    /// <summary>Asks GitHub again — what Refresh, and linking a pull request by its
    /// reference, mean. A merged pull request is still not asked again.</summary>
    public static PullRequestReadOptions Fresh { get; } = new(TimeSpan.Zero);
}
