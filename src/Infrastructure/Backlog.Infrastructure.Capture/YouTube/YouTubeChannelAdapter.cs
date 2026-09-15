using System.Text.RegularExpressions;

using Backlog.Infrastructure.Capture.Feeds;
using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.DataTransferObjects;
using Backlog.Modules.Capture.Ports;

namespace Backlog.Infrastructure.Capture.YouTube;

/// <summary>
/// Answers <see cref="ICaptureSourceAdapter"/> for a YouTube channel through
/// the channel's own Atom feed — no API key, no quota, and the feed carries
/// the fifteen most recent uploads, which is what "new on the channel" means
/// on a button press.
/// <para>
/// The work is turning what a person pasted into that feed's URL. A feed URL
/// is used as it is; a <c>/channel/UC…</c> URL or a bare id names the channel
/// directly; anything else — a handle, a <c>/user/</c> or <c>/c/</c> page — is
/// fetched once and its channel id read out of the page, because those
/// spellings are names YouTube resolves and the feed endpoint does not.
/// </para>
/// <para>
/// That page fetch carries a consent cookie, and only that fetch does. Asked
/// without one, YouTube answers a handle page with a redirect to its consent
/// wall and then a landing page that names no channel — which reads here as
/// "could not find a channel id" for every handle in the EU. <c>SOCS=CAI</c>
/// is the cookie that gets past the wall; <c>CONSENT=YES+cb</c> is the older
/// one, kept beside it the way feed readers keep both. The feed does not want
/// either, and no other host should be shown them.
/// </para>
/// </summary>
internal sealed partial class YouTubeChannelAdapter(FeedFetcher fetcher) : ICaptureSourceAdapter
{
    /// <summary>What the page fetch carries past the consent wall; see the
    /// class remarks for why.</summary>
    internal static readonly IReadOnlyDictionary<string, string> ConsentHeaders =
        new Dictionary<string, string> { ["Cookie"] = "SOCS=CAI; CONSENT=YES+cb" };

    public CaptureSourceKind Kind => CaptureSourceKind.YouTube;

    public Task<CaptureSourceFindings> RunAsync(MonitoredSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        return SourceTargets.ReadAllAsync(source, ReadTargetAsync, cancellationToken);
    }

    private async Task<TargetReading> ReadTargetAsync(string target, CancellationToken cancellationToken)
    {
        var feed = FeedUrlFor(target);

        if (feed is null)
        {
            var page = await fetcher.GetAsync(PageUrlFor(target), ConsentHeaders, cancellationToken).ConfigureAwait(false);
            var channelId = ChannelIdIn(page.Text);

            if (channelId is null) return TargetReading.Remark("could not find a channel id");

            feed = FeedUrlFor(channelId)!;
        }

        var document = await fetcher.GetAsync(feed, cancellationToken).ConfigureAwait(false);

        await using var xml = document.OpenRead();
        return new TargetReading(FeedReader.Read(xml, document.Url));
    }

    /// <summary>The feed URL a target names directly, or null when the target
    /// is a name the page has to be asked about.</summary>
    internal static Uri? FeedUrlFor(string target)
    {
        if (target.Contains("/feeds/videos.xml", StringComparison.OrdinalIgnoreCase))
        {
            return SourceTargets.ParseUrl(target);
        }

        var channel = ChannelUrl().Match(target);
        if (channel.Success) return Feed(channel.Groups["id"].Value);

        return BareChannelId().IsMatch(target) ? Feed(target) : null;
    }

    /// <summary>The page to ask for a channel id: a URL as given, a handle as
    /// <c>youtube.com/@handle</c>, and a bare name as a handle too, since that
    /// is what a person who typed one meant.</summary>
    internal static Uri PageUrlFor(string target)
    {
        if (target.Contains("youtube.com/", StringComparison.OrdinalIgnoreCase)
            || target.Contains("youtu.be/", StringComparison.OrdinalIgnoreCase))
        {
            return SourceTargets.ParseUrl(target);
        }

        var handle = target.TrimStart('@');
        return new Uri($"https://www.youtube.com/@{Uri.EscapeDataString(handle)}");
    }

    /// <summary>The channel id in a channel page, wherever the page puts it:
    /// the canonical link in the head, the feed link beside it, the
    /// <c>channelId</c> or <c>externalId</c> field of the page's data, or the
    /// <c>identifier</c> / <c>channelId</c> microdata in the head. First match
    /// wins; they agree when more than one is present, and a page served
    /// around a redirect may carry only one of them.</summary>
    internal static string? ChannelIdIn(string html)
    {
        foreach (var pattern in new[] { CanonicalChannelLink(), FeedChannelId(), ChannelIdField(), ExternalIdField(), ChannelIdMeta() })
        {
            var match = pattern.Match(html);
            if (match.Success) return match.Groups["id"].Value;
        }

        return null;
    }

    private static Uri Feed(string channelId) =>
        new($"https://www.youtube.com/feeds/videos.xml?channel_id={channelId}");

    [GeneratedRegex(@"/channel/(?<id>UC[\w-]{20,})", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelUrl();

    [GeneratedRegex(@"^UC[\w-]{20,}$")]
    private static partial Regex BareChannelId();

    [GeneratedRegex(@"<link\b[^>]*rel=[""']canonical[""'][^>]*href=[""']https?://(?:www\.)?youtube\.com/channel/(?<id>UC[\w-]{20,})", RegexOptions.IgnoreCase)]
    private static partial Regex CanonicalChannelLink();

    [GeneratedRegex(@"channel_id=(?<id>UC[\w-]{20,})", RegexOptions.IgnoreCase)]
    private static partial Regex FeedChannelId();

    [GeneratedRegex(@"""channelId""\s*:\s*""(?<id>UC[\w-]{20,})""")]
    private static partial Regex ChannelIdField();

    [GeneratedRegex(@"""externalId""\s*:\s*""(?<id>UC[\w-]{20,})""")]
    private static partial Regex ExternalIdField();

    [GeneratedRegex(@"<meta\b[^>]*itemprop=[""'](?:identifier|channelId)[""'][^>]*content=[""'](?<id>UC[\w-]{20,})[""']", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelIdMeta();
}
