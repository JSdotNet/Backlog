using Backlog.Desktop.UI.Shell;
using Backlog.SharedKernel;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The one <c>backlog://window/…</c> format of local ADR 0040 §7, written by
/// <see cref="SurfaceLink"/> (what <c>open_window</c> answers) and read by
/// <see cref="BacklogWindowLink"/> (what the desktop head activates): a link the
/// builder writes parses back to the same surface and query.
/// </summary>
public sealed class BacklogWindowLinkRoundTripTests
{
    [Fact]
    public void A_link_SurfaceLink_builds_parses_back_to_the_same_surface_and_query()
    {
        KeyValuePair<string, string>[] query =
        [
            new("plan", "shell-pop-out-windows"),
            new("entry", "8cb393df-7bf7-4573-b1c1-ba09882ca70d"),
            new("chapter", "arc42/adr/0040 a surface & its window.md"),
            new("scope", "JSdotNet/Backlog"),
        ];

        var link = SurfaceLink.Protocol("devbook", query);
        var parsed = BacklogWindowLink.Parse(link);

        Assert.NotNull(parsed);
        Assert.Equal("devbook", parsed.SurfaceId);
        Assert.Equal(query.ToDictionary(pair => pair.Key, pair => pair.Value), parsed.Query);
    }
}
