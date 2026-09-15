using System.Text.Json;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// Reads a repository one commit at a time: the list of everything a commit
/// contains, and then any one file out of it.
/// <para>
/// This is the network half of lazy branch loading. A snapshot used to arrive
/// as a whole archive; now it arrives as one listing and then file by file, so
/// that a repository of two thousand files costs one call to list and a handful
/// to open the chapters somebody actually reads. Both calls go through
/// <see cref="IGitHubTransport"/>, which parses JSON — and both endpoints answer
/// in JSON, a blob's bytes arriving base64-encoded — so unlike the archive
/// download this needs no second HTTP path and no second credential decision.
/// </para>
/// </summary>
public interface IGitHubTreeClient
{
    /// <summary>
    /// Every path a commit contains, recursively, with the blob id of each file.
    /// </summary>
    /// <exception cref="GitHubException">The commit could not be listed — no such
    /// commit, no access, or the network refused.</exception>
    Task<GitHubTreeListing> ListTreeAsync(
        GitHubRepositoryRef repository,
        string commitSha,
        CancellationToken cancellationToken = default);

    /// <summary>The bytes of one file, by the blob id the listing gave it.</summary>
    /// <exception cref="GitHubException">The blob could not be read.</exception>
    Task<byte[]> ReadBlobAsync(
        GitHubRepositoryRef repository,
        string blobSha,
        CancellationToken cancellationToken = default);
}

/// <summary>One path in a commit: a file with the id of its content, or a
/// directory.</summary>
/// <param name="Path">Repository-relative, <c>/</c>-separated.</param>
/// <param name="Sha">The blob id for a file, the tree id for a directory.</param>
/// <param name="Size">Bytes, for a file; null for a directory.</param>
public sealed record GitHubTreeEntry(string Path, string Sha, bool IsDirectory, long? Size);

/// <summary>
/// A commit's paths.
/// <para>
/// <paramref name="Truncated"/> is GitHub saying the listing was cut short —
/// the recursive tree endpoint stops at a hundred thousand entries — and it is
/// carried rather than thrown because a partial listing still opens every
/// chapter it names. The caller decides whether to say so.
/// </para>
/// </summary>
public sealed record GitHubTreeListing(IReadOnlyList<GitHubTreeEntry> Entries, bool Truncated);

/// <inheritdoc />
public sealed class GitHubTreeClient(IGitHubTransport transport) : IGitHubTreeClient
{
    private readonly IGitHubTransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public async Task<GitHubTreeListing> ListTreeAsync(
        GitHubRepositoryRef repository,
        string commitSha,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(commitSha);

        var payload = await _transport.SendAsync(
            HttpMethod.Get,
            $"repos/{repository.Owner}/{repository.Name}/git/trees/{Uri.EscapeDataString(commitSha.Trim())}?recursive=1",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!payload.TryGetProperty("tree", out var tree) || tree.ValueKind is not JsonValueKind.Array)
        {
            throw new GitHubException($"GitHub did not return a tree for {repository.FullName} at {commitSha}.");
        }

        var entries = new List<GitHubTreeEntry>();

        foreach (var element in tree.EnumerateArray())
        {
            var path = String(element, "path");
            var sha = String(element, "sha");
            var type = String(element, "type");

            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(sha)) continue;

            // A submodule ("commit") is a pointer to another repository, not a
            // file this one can serve; it is neither a folder to walk nor a blob
            // to read, so it is simply not listed.
            switch (type)
            {
                case "blob":
                    entries.Add(new GitHubTreeEntry(
                        path,
                        sha,
                        IsDirectory: false,
                        element.TryGetProperty("size", out var size) && size.ValueKind is JsonValueKind.Number
                            ? size.GetInt64()
                            : null));
                    break;
                case "tree":
                    entries.Add(new GitHubTreeEntry(path, sha, IsDirectory: true, null));
                    break;
            }
        }

        var truncated = payload.TryGetProperty("truncated", out var flag) && flag.ValueKind is JsonValueKind.True;

        return new GitHubTreeListing(entries, truncated);
    }

    public async Task<byte[]> ReadBlobAsync(
        GitHubRepositoryRef repository,
        string blobSha,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(blobSha);

        var payload = await _transport.SendAsync(
            HttpMethod.Get,
            $"repos/{repository.Owner}/{repository.Name}/git/blobs/{Uri.EscapeDataString(blobSha.Trim())}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var content = String(payload, "content");
        var encoding = String(payload, "encoding");

        if (content is null)
        {
            throw new GitHubException($"GitHub did not return content for blob {blobSha} in {repository.FullName}.");
        }

        // GitHub base64-encodes with line breaks every sixty characters, which
        // the decoder skips as whitespace. An empty file is an empty content
        // string with the same encoding, and decodes to nothing, correctly.
        return encoding switch
        {
            "base64" => Convert.FromBase64String(content),
            "utf-8" or null or "" => System.Text.Encoding.UTF8.GetBytes(content),
            _ => throw new GitHubException($"GitHub returned blob {blobSha} in {repository.FullName} with an encoding this app cannot read: {encoding}.")
        };
    }

    private static string? String(JsonElement element, string property) =>
        element.ValueKind is JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;
}
