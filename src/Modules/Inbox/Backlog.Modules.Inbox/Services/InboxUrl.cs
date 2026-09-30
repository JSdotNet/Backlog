using System.Text.RegularExpressions;

namespace Backlog.Modules.Inbox.Services;

/// <summary>A link written in some text, as written and as compared.</summary>
internal readonly record struct InboxLink(string Written, string Key);

/// <summary>
/// How the Inbox reads and compares links: the rules <see cref="DependencyProposal"/>
/// and <see cref="RelationFinder"/> share, in one place so "the same link" means
/// the same thing to both of them.
/// </summary>
internal static partial class InboxUrl
{
    /// <summary>The forges where a host alone says nothing: every repository on
    /// them shares it, so their site is the host and the repository.</summary>
    private static readonly string[] Forges = ["github.com", "gitlab.com"];

    /// <summary>The query parameters that say where a link was shared from
    /// rather than what it points at, by exact name; every <c>utm_</c> one is
    /// dropped too.</summary>
    private static readonly string[] TrackingParameters = ["fbclid", "gclid", "ref_src"];

    /// <summary>
    /// How two links are compared: without case, without a fragment, without a
    /// trailing slash, without a leading <c>www.</c>, with <c>http</c> read as
    /// <c>https</c>, and without the tracking parameters a share adds
    /// (<c>utm_*</c>, <c>fbclid</c>, <c>gclid</c>, <c>ref_src</c>) — so
    /// <c>…/issues/12#issuecomment-3</c> and <c>…/issues/12/</c> both name issue
    /// 12, and <c>…/issues/123</c> does not. Every other query parameter is kept,
    /// in its order: <c>?v=abc</c> and <c>?v=xyz</c> are two videos.
    /// <para>
    /// The whole link is lower-cased, not only the scheme and host. A path can be
    /// case-sensitive on some servers, but the links a person captures twice are
    /// the same link retyped or re-shared, and GitHub — where most of them
    /// point — ignores the case of an owner and a repository.
    /// </para>
    /// </summary>
    public static string Key(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        var key = url.Trim();
        var fragment = key.IndexOf('#', StringComparison.Ordinal);
        if (fragment >= 0) key = key[..fragment];

        key = key.ToLowerInvariant();

        if (key.StartsWith("http://", StringComparison.Ordinal)) key = "https://" + key["http://".Length..];

        var scheme = key.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0 && key.AsSpan(scheme + 3).StartsWith("www.", StringComparison.Ordinal))
        {
            key = key[..(scheme + 3)] + key[(scheme + 7)..];
        }

        var question = key.IndexOf('?', StringComparison.Ordinal);
        var path = (question >= 0 ? key[..question] : key).TrimEnd('/');
        if (question < 0) return path;

        var kept = key[(question + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(parameter => !IsTracking(parameter))
            .ToList();

        return kept.Count == 0 ? path : path + "?" + string.Join('&', kept);
    }

    /// <summary>Whether a <c>name=value</c> pair, already lower case, is one
    /// of the tracking parameters.</summary>
    private static bool IsTracking(string parameter)
    {
        var equals = parameter.IndexOf('=', StringComparison.Ordinal);
        var name = equals >= 0 ? parameter[..equals] : parameter;

        return name.StartsWith("utm_", StringComparison.Ordinal) || TrackingParameters.Contains(name, StringComparer.Ordinal);
    }

    /// <summary>
    /// The site a link is on: its host without <c>www.</c>, or on GitHub and
    /// GitLab the host and the repository (<c>github.com/owner/name</c>), so two
    /// issues in one repository share a site and two unrelated GitHub links do
    /// not. Null for something that is not an <c>http(s)</c> link, and for a
    /// forge link that names no repository — the forge's front page is not a
    /// site anything else can share.
    /// </summary>
    public static string? Site(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("http" or "https")) return null;

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        if (host.Length == 0) return null;

        if (!Forges.Contains(host, StringComparer.Ordinal)) return host;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length < 2 ? null : $"{host}/{segments[0].ToLowerInvariant()}/{segments[1].ToLowerInvariant()}";
    }

    /// <summary>Every <c>http(s)</c> link in the text, trailing sentence
    /// punctuation left off — "see https://example.com/a." links to <c>/a</c>.
    /// A markdown link's brackets and a quoted link's quotes end it too.</summary>
    public static List<InboxLink> Links(string text) =>
        [.. LinkPattern().Matches(text)
            .Select(match => match.Value.TrimEnd('.', ',', ';', ':', '!', '?'))
            .Where(written => written.Length > 0)
            .Select(written => new InboxLink(written, Key(written)))];

    /// <summary>The text's own spelling of <paramref name="url"/>, or null when
    /// the text does not link to it.</summary>
    public static string? Written(List<InboxLink> links, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var key = Key(url);
        return links.FirstOrDefault(link => string.Equals(link.Key, key, StringComparison.Ordinal)).Written;
    }

    /// <summary>Whether two links are the same link, compared as <see cref="Key"/>
    /// compares them. Two missing links are not the same link.</summary>
    public static bool Same(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && string.Equals(Key(a), Key(b), StringComparison.Ordinal);

    [GeneratedRegex(@"https?://[^\s<>()\[\]{}""'`]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkPattern();
}
