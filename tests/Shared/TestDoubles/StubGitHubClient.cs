using Backlog.Infrastructure.GitHub;

namespace Backlog.Tests;

/// <summary>
/// A GitHub client for a render that has to be handed one but never reaches
/// GitHub: every call throws, so a test that does reach it fails loudly instead
/// of going to the network.
///
/// <para>Linked only into the test projects that use it, because it implements
/// a product interface a project without the GitHub reference cannot see.</para>
/// </summary>
internal sealed class StubGitHubClient : IGitHubClient
{
    public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<GitHubIssue> CreateIssueAsync(
        GitHubRepositoryRef repository,
        string title,
        string? body,
        IEnumerable<string>? labels = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<GitHubIssueSnapshot> GetIssueAsync(
        GitHubRepositoryRef repository,
        int number,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<GitHubUploadedFile> UploadFileAsync(
        GitHubRepositoryRef repository,
        string path,
        string branch,
        byte[] content,
        string commitMessage,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
