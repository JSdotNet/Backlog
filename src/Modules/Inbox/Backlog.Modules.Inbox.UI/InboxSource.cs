namespace Backlog.Desktop.UI.Inbox;

/// <summary>
/// Where an inbox item came from: the channel it arrived through and, when
/// somebody handed it over, who.
/// <para>
/// Two facts and not one, because they answer different questions. The channel
/// is the Capture Source of <c>.domain/capture/domain.md</c> — <c>mobile</c>,
/// <c>youtube</c>, <c>website</c>, <c>email</c>, <c>web_clipper</c>, <c>ide</c>,
/// <c>manual</c> — plus <c>claude</c> for an artifact a session shared; it says
/// how the item got here. The person is a stored <c>@name</c> tag and says who
/// sent it, which is provenance the channel cannot carry: a link shared by a
/// colleague and the same link clipped alone arrive through different channels
/// and mean different things to the reader triaging them.
/// </para>
/// </summary>
/// <param name="Channel">The capture channel, in the domain's own lower-case
/// spelling.</param>
/// <param name="Person">The person who shared it as a stored tag — with its
/// <c>@</c>, the way the shared library's <c>TagText</c> stores one — or null
/// when nobody did.</param>
public sealed record InboxSource(string Channel, string? Person = null)
{
    /// <summary>The channel as a reader reads it beside its badge.</summary>
    public string ChannelLabel => Channel switch
    {
        "mobile" => "Mobile",
        "youtube" => "YouTube",
        "website" => "Website",
        "email" => "Email",
        "web_clipper" => "Web clipper",
        "ide" => "IDE",
        "manual" => "Manual",
        "claude" => "Claude",
        _ => Channel
    };
}
