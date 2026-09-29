using System.Text.Json;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// Sends one GraphQL operation through an <see cref="IGitHubTransport"/> and reads
/// the answer the way GraphQL means it.
/// <para>
/// A helper over the transport rather than a third transport, because GraphQL is
/// one more POST to one more path and both transports already carry that: the token
/// transport posts the body, and <c>gh api graphql --input -</c> is the CLI's own
/// GraphQL route. What is different is the answer. GraphQL reports most refusals as
/// a 200 with an <c>errors</c> array beside a <c>data</c> that may be partly
/// filled, and a caller that only watched the status would read a refused merge as
/// one that worked. So the errors are checked here, once, and become the same
/// <see cref="GitHubException"/> every REST refusal already is.
/// </para>
/// <para>
/// <b>The path names the repository</b> — <c>graphql#owner/name</c> — for routing
/// only. <c>graphql</c> alone is one endpoint for every repository and so names
/// nobody, and a call that names nobody goes out as this machine's default identity:
/// in a workspace where the repository is bound to another account, that is the
/// wrong person merging. <see cref="GitHubSettings.AccountForPath"/> reads the hint;
/// both transports strip it through <see cref="ResourceOf"/>, so it is never sent.
/// A fragment, because a fragment is by definition the part of an address a server
/// never sees.
/// </para>
/// </summary>
internal static class GitHubGraphQl
{
    /// <summary>The GraphQL resource, relative to the API root on github.com.</summary>
    public const string Resource = "graphql";

    private const string HintedPrefix = Resource + "#";

    /// <summary>The schema preview <c>mergeStateStatus</c> was introduced behind. An
    /// Enterprise Server that still gates it answers the field as absent without
    /// this media type; where the field has graduated it costs nothing.</summary>
    public const string MergeInfoPreview = "application/vnd.github.merge-info-preview+json";

    /// <summary>The ordinary REST media type, named again beside the preview because
    /// an Accept set on a request replaces the default rather than adding to it.</summary>
    public const string GitHubJsonMediaType = "application/vnd.github+json";

    /// <summary>What every GraphQL call accepts, in the order both transports send it.</summary>
    public const string AcceptMediaTypes = MergeInfoPreview + ", " + GitHubJsonMediaType;

    /// <summary>The path a call about <paramref name="repository"/> is sent on: the
    /// GraphQL resource, with the repository after the <c>#</c> for the credential
    /// resolver.</summary>
    public static string PathFor(GitHubRepositoryRef repository) =>
        $"{HintedPrefix}{repository.Owner}/{repository.Name}";

    /// <summary>Whether <paramref name="path"/> is a GraphQL call, hinted or not.</summary>
    public static bool IsGraphQl(string? path) =>
        path is not null
        && (string.Equals(path.TrimStart('/'), Resource, StringComparison.OrdinalIgnoreCase)
            || path.TrimStart('/').StartsWith(HintedPrefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>What a transport actually requests: the path with any routing hint
    /// taken off. Every other path comes back unchanged — a REST path never carries
    /// a bare <c>#</c>, because the clients escape each segment they build.</summary>
    public static string ResourceOf(string path) =>
        IsGraphQl(path) ? Resource : path;

    /// <summary>The owner and name a hinted GraphQL path names, or null when it names
    /// none — plain <c>graphql</c>, or a hint that is not an owner/name pair.</summary>
    public static (string Owner, string Name)? RepositoryOf(string? path)
    {
        if (path is null) return null;

        var trimmed = path.Trim().TrimStart('/');
        if (!trimmed.StartsWith(HintedPrefix, StringComparison.OrdinalIgnoreCase)) return null;

        var parts = trimmed[HintedPrefix.Length..]
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length == 2 ? (parts[0], parts[1]) : null;
    }

    /// <summary>
    /// Posts <paramref name="query"/> with <paramref name="variables"/> and returns
    /// the <c>data</c> object.
    /// <para>
    /// Values always travel as variables. A repository name or a node id spliced
    /// into the query text is an injection waiting for an unusual name, and a query
    /// that is a constant is one GitHub can parse the same way every time.
    /// </para>
    /// </summary>
    /// <param name="tolerate">
    /// Given the <c>errors</c> entries and the <c>data</c> beside them, whether this
    /// answer is still one the caller can read. Null — every mutation — means any
    /// error is a failure. A query passes one when GraphQL's partial answers are
    /// part of its design: a field the credential may not see comes back null with
    /// an error pointing at it, and the rest of the answer is still true.
    /// </param>
    /// <exception cref="GitHubException">GitHub refused the call, over HTTP or in an
    /// <c>errors</c> entry — the first entry's message, with its <c>type</c>.</exception>
    public static async Task<JsonElement> SendAsync(
        IGitHubTransport transport,
        GitHubRepositoryRef repository,
        string query,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken cancellationToken,
        Func<IReadOnlyList<JsonElement>, JsonElement, bool>? tolerate = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(repository);

        // Dictionaries rather than an anonymous type: the shared serializer options
        // snake-case property names, and GraphQL's are camelCase. Dictionary keys
        // are left exactly as written.
        var body = new Dictionary<string, object?>
        {
            ["query"] = query,
            ["variables"] = variables
        };

        var response = await transport.SendAsync(
            HttpMethod.Post,
            PathFor(repository),
            body,
            cancellationToken: cancellationToken);

        return DataOf(response, tolerate);
    }

    /// <summary>Whether a GraphQL error's <c>path</c> passes through a field named
    /// <paramref name="field"/> — how a query recognises the one refusal it can
    /// read around.</summary>
    public static bool PathPassesThrough(JsonElement error, string field) =>
        error.ValueKind == JsonValueKind.Object
        && error.TryGetProperty("path", out var path)
        && path.ValueKind == JsonValueKind.Array
        && path.EnumerateArray().Any(segment =>
            segment.ValueKind == JsonValueKind.String
            && string.Equals(segment.GetString(), field, StringComparison.Ordinal));

    /// <summary>The <c>data</c> of a GraphQL answer, or the refusal it carries.</summary>
    internal static JsonElement DataOf(JsonElement response, Func<IReadOnlyList<JsonElement>, JsonElement, bool>? tolerate = null)
    {
        if (response.ValueKind != JsonValueKind.Object)
        {
            throw new GitHubException("GitHub's GraphQL API returned something that wasn't an answer.");
        }

        if (response.TryGetProperty("errors", out var errors)
            && errors.ValueKind == JsonValueKind.Array
            && errors.GetArrayLength() > 0)
        {
            if (tolerate is not null
                && response.TryGetProperty("data", out var partial)
                && partial.ValueKind == JsonValueKind.Object
                && tolerate([.. errors.EnumerateArray()], partial))
            {
                return partial;
            }

            var first = errors[0];
            var message = first.ValueKind == JsonValueKind.Object
                && first.TryGetProperty("message", out var text)
                && text.ValueKind == JsonValueKind.String
                    ? text.GetString()
                    : null;
            var type = first.ValueKind == JsonValueKind.Object
                && first.TryGetProperty("type", out var kind)
                && kind.ValueKind == JsonValueKind.String
                    ? kind.GetString()
                    : null;

            throw new GitHubException(string.IsNullOrWhiteSpace(message) ? "GitHub refused the request." : message)
            {
                ErrorType = type,
                Status = string.Equals(type, "NOT_FOUND", StringComparison.Ordinal)
                    ? System.Net.HttpStatusCode.NotFound
                    : null
            };
        }

        if (!response.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            throw new GitHubException("GitHub's GraphQL API answered without any data.");
        }

        return data;
    }
}
