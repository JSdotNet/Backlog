namespace Backlog.Modules.Events.UI;

/// <summary>
/// The mark at the start of a list row (<c>.devbook/design/color-scheme.md#events-map-marks</c>):
/// its tone and, for an upcoming event on the map, its map number — the same as its pin's.
/// </summary>
/// <param name="Tone">How the mark is drawn.</param>
/// <param name="Number">The map number, or null when the event carries none.</param>
public sealed record EventMark(EventMarkTone Tone, string? Number);

/// <summary>How a row's mark is drawn.</summary>
public enum EventMarkTone
{
    /// <summary>Going: a <c>color-primary</c> fill.</summary>
    Filled,

    /// <summary>Spotted or Considering: a dashed <c>color-primary</c> edge, no fill.</summary>
    Dashed,

    /// <summary>On the map but past, Skipped, or Attended or Harvested while upcoming: a grey ring.</summary>
    Grey,

    /// <summary>Online: <c>◎</c>, never on the map.</summary>
    Online,

    /// <summary>Off the map for any other reason: <c>–</c>.</summary>
    Off
}
