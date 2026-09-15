using Backlog.Modules.Capture.Abstractions.DataTransferObjects;
using Backlog.Modules.Capture.Ports;

namespace Backlog.Infrastructure.Capture.Feeds;

/// <summary>
/// The loop both adapters share: every target of a source in turn, what each
/// one found gathered up, and what went wrong with one written down beside the
/// target rather than thrown.
/// <para>
/// One target failing must not stop the next — a person watching three
/// channels and one dead page is owed the three channels — so the only thing
/// that leaves this loop as an exception is the reader's own cancellation.
/// Everything else, a 500, a timeout, a page that is not a feed, becomes a
/// note on the source's line.
/// </para>
/// </summary>
internal static class SourceTargets
{
    public static async Task<CaptureSourceFindings> ReadAllAsync(
        MonitoredSource source,
        Func<string, CancellationToken, Task<TargetReading>> readTarget,
        CancellationToken cancellationToken)
    {
        var entries = new List<CapturedEntry>();
        var notes = new List<string>();

        foreach (var target in source.Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(target)) continue;

            var trimmed = target.Trim();

            try
            {
                var reading = await readTarget(trimmed, cancellationToken).ConfigureAwait(false);

                entries.AddRange(reading.Entries);
                if (reading.Note is { } note) notes.Add($"{trimmed}: {note}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The same rule the run applies one level up, per target
                // instead of per source: written down, not thrown.
                notes.Add($"{trimmed}: {ex.Message.TrimEnd('.')}");
            }
        }

        return new CaptureSourceFindings(entries, notes);
    }

    /// <summary>The fetcher's URL for a target a person typed: a bare host or
    /// path gets <c>https://</c> in front, because that is what they meant.</summary>
    public static Uri ParseUrl(string target)
    {
        var text = target.Contains("://", StringComparison.Ordinal) ? target : $"https://{target}";

        if (!Uri.TryCreate(text, UriKind.Absolute, out var url) || !IsFetchable(url))
        {
            throw new FormatException("not a web address");
        }

        return url;
    }

    /// <summary>Whether a URL is one the fetcher may be sent to: http or
    /// https, to a host with a dot in it. The one rule for an address a
    /// person typed and an address a page advertised, because both end up
    /// fetched on the reader's machine — a <c>file:</c> link in a page's
    /// head must not read a file, and <c>localhost</c> is not a source.</summary>
    public static bool IsFetchable(Uri url) =>
        url.IsAbsoluteUri
        && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrEmpty(url.Host)
        && url.Host.Contains('.', StringComparison.Ordinal);
}

/// <summary>What one target gave: its entries, and a remark when there is
/// one — a page with no feed, a channel with no id. A remark and entries do
/// not go together; a target that produced entries has nothing to add.</summary>
internal sealed record TargetReading(IReadOnlyList<CapturedEntry> Entries, string? Note = null)
{
    public static TargetReading Remark(string note) => new([], note);
}
