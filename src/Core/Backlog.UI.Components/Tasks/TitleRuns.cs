using Backlog.SharedKernel.Markdown;
using Backlog.UI.Components.Badges;

namespace Backlog.UI.Components.Tasks;

/// <summary>
/// A task title read as the runs every task surface draws: prose, the tags typed
/// into it, and the bare web URLs pasted into it.
/// <para>
/// The contract is <see cref="TagText.Segments"/>'s, widened by one kind: the runs
/// concatenate back to the title character for character. Nothing is stripped and
/// nothing is reordered, so a surface that draws them still says the whole title a
/// person typed — the URL run is a reading of the text, never an edit of it.
/// </para>
/// <para>
/// What counts as a URL is <see cref="BareUrlScanner"/>'s, the reading the Markdown
/// bodies use, so a URL is a link in a title and in a body or in neither. A sigil
/// inside a URL — the <c>#frag</c> of an address — belongs to the address, and a
/// tag the URL cuts through is left as prose rather than drawn as a chip with half
/// its word missing.
/// </para>
/// </summary>
public static class TitleRuns
{
    /// <summary>The title as runs, in document order. Neighbouring prose is one run.</summary>
    public static IReadOnlyList<TitleRun> Split(string? title)
    {
        var text = title ?? string.Empty;

        if (text.Length == 0) return [];

        var urls = BareUrlScanner.FindAll(text);
        var runs = new List<TitleRun>();
        var at = 0;

        // Walked in the tag reading's own order, because its segments carry no
        // index of their own: the running offset is where each one starts.
        foreach (var segment in TagText.Segments(text))
        {
            var kind = segment.IsTag && !Overlaps(urls, at, segment.Text.Length)
                ? TitleRunKind.Tag
                : TitleRunKind.Text;

            Append(runs, segment.Text, kind);
            at += segment.Text.Length;
        }

        return urls.Count == 0 ? runs : CutUrls(runs, urls);
    }

    /// <summary>
    /// The distinct URLs in a title, in the order they first appear — one followable
    /// link each beside the title. The same URL twice is one link: two anchors with
    /// one destination are one choice offered twice.
    /// </summary>
    public static IReadOnlyList<string> Urls(string? title) =>
        BareUrlScanner.FindAll(title)
            .Select(url => url.Url)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Whether a title holds an address to follow, for a host deciding
    /// whether the slot its links sit in is drawn at all.</summary>
    public static bool HasUrl(string? title) => BareUrlScanner.FindAll(title).Count > 0;

    /// <summary>
    /// What a link beside the title reads as: the host, lower-cased the way a
    /// browser's address bar shows it. On a dense row the host is the shortest
    /// label that still tells two links apart. Falls back to the URL itself for
    /// anything <see cref="Uri"/> will not parse.
    /// </summary>
    public static string Host(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? uri.Host : url;

    private static bool Overlaps(IReadOnlyList<BareUrl> urls, int start, int length) =>
        urls.Any(url => start < url.Index + url.Length && url.Index < start + length);

    /// <summary>Cuts each URL out of the prose runs around it. Tags never overlap a
    /// URL by now — <see cref="Split"/> demoted those to prose — so a URL only ever
    /// lands inside text.</summary>
    private static List<TitleRun> CutUrls(List<TitleRun> runs, IReadOnlyList<BareUrl> urls)
    {
        var result = new List<TitleRun>();
        var start = 0;

        foreach (var run in runs)
        {
            var end = start + run.Text.Length;

            if (run.Kind != TitleRunKind.Text)
            {
                result.Add(run);
                start = end;
                continue;
            }

            var cursor = start;
            foreach (var url in urls.Where(url => url.Index >= start && url.Index < end))
            {
                Append(result, run.Text[(cursor - start)..(url.Index - start)], TitleRunKind.Text);
                result.Add(new TitleRun(url.Url, TitleRunKind.Url));
                cursor = url.Index + url.Length;
            }

            Append(result, run.Text[(cursor - start)..], TitleRunKind.Text);
            start = end;
        }

        return result;
    }

    private static void Append(List<TitleRun> runs, string text, TitleRunKind kind)
    {
        if (text.Length == 0) return;

        if (kind == TitleRunKind.Text && runs.Count > 0 && runs[^1].Kind == TitleRunKind.Text)
        {
            runs[^1] = runs[^1] with { Text = runs[^1].Text + text };
            return;
        }

        runs.Add(new TitleRun(text, kind));
    }
}

/// <summary>One run of a title, exactly as it appeared. A tag run carries its
/// sigil; a URL run is the address and nothing around it.</summary>
public sealed record TitleRun(string Text, TitleRunKind Kind);

/// <summary>What a run of a title is.</summary>
public enum TitleRunKind
{
    Text,
    Tag,
    Url
}
