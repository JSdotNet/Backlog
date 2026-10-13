using System.Text;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A tree client that reaches no network: a commit's files are
/// <see cref="Trees"/>[sha] (path to blob id), a blob's text is
/// <see cref="Blobs"/>[id], and every read is counted.
/// <para>
/// A blob with an entry in <see cref="Gates"/> answers only once its gate is
/// set, and gives up when the token it was handed is cancelled — the way a real
/// read does, which is what lets a test see whose token a shared read runs on.
/// </para>
/// </summary>
internal sealed class StubTreeClient : IGitHubTreeClient
{
    public Dictionary<string, Dictionary<string, string>> Trees { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Blobs { get; } = new(StringComparer.Ordinal);

    /// <summary>Trees GitHub would report as cut short.</summary>
    public HashSet<string> TruncatedTrees { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, TaskCompletionSource> Gates { get; } = new(StringComparer.Ordinal);

    public List<string> TreesListed { get; } = [];

    public List<string> BlobsRead { get; } = [];

    public Task<GitHubTreeListing> ListTreeAsync(
        GitHubRepositoryRef repository,
        string commitSha,
        CancellationToken cancellationToken = default)
    {
        lock (TreesListed) TreesListed.Add(commitSha);

        if (!Trees.TryGetValue(commitSha, out var files)) throw new GitHubException($"No tree for {commitSha}.");

        return Task.FromResult(new GitHubTreeListing(
            [.. files.Select(file => new GitHubTreeEntry(file.Key, file.Value, IsDirectory: false, Size: null))],
            Truncated: TruncatedTrees.Contains(commitSha)));
    }

    public async Task<byte[]> ReadBlobAsync(
        GitHubRepositoryRef repository,
        string blobSha,
        CancellationToken cancellationToken = default)
    {
        lock (BlobsRead) BlobsRead.Add(blobSha);

        if (Gates.TryGetValue(blobSha, out var gate)) await gate.Task.WaitAsync(cancellationToken);

        return Blobs.TryGetValue(blobSha, out var text)
            ? Encoding.UTF8.GetBytes(text)
            : throw new GitHubException($"No blob {blobSha}.");
    }
}
