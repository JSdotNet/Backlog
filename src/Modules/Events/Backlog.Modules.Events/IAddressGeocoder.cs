using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;

namespace Backlog.Modules.Events;

/// <summary>
/// The geocoder an in-person event's address is sent to, once, when the person saves it new or
/// changed or presses Look up now (local ADR 0061 §3, §5). Only the address's four parts go out:
/// never the venue name, the title or anything else about the event.
/// <para>
/// A port: <c>AddEventsModule()</c> registers one that sends nothing and gets no answer back, as
/// a desktop with no map key does; the host replaces it with the Azure Maps adapter, which lives
/// in a project of its own (ADR 0061 §4, guideline 0005).
/// </para>
/// <para>
/// Being offline, having no key, a timeout, a <c>429</c> or a <c>5xx</c> is
/// <see cref="GeocodeAnswer.Failed"/>, never <see cref="GeocodeAnswer.NotFound"/>, and is answered
/// rather than thrown. An adapter logs the outcome, the status code and the duration, never the
/// address or the point (guideline 0010).
/// </para>
/// </summary>
public interface IAddressGeocoder
{
    Task<GeocodeAnswer> GeocodeAsync(Address address, CancellationToken cancellationToken = default);
}

/// <summary>
/// What a geocoder answered for one address, as the <see cref="AddressLookup"/> it leaves the
/// event in: Placed with its point, Address not found, or Not looked up yet when no answer came
/// back. A Placed answer always carries its point, and no other answer carries one.
/// </summary>
public sealed record GeocodeAnswer
{
    private GeocodeAnswer(AddressLookup lookup, MapPoint? point)
    {
        Lookup = lookup;
        Point = point;
    }

    public AddressLookup Lookup { get; }

    /// <summary>The point found; set exactly when <see cref="Lookup"/> is Placed.</summary>
    public MapPoint? Point { get; }

    /// <summary>A usable match: the first answer of High or Medium confidence finer than a
    /// province (ADR 0061 §5), at <paramref name="point"/>.</summary>
    public static GeocodeAnswer Placed(MapPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return new GeocodeAnswer(AddressLookup.Placed, point);
    }

    /// <summary>The geocoder answered, with no usable match.</summary>
    public static GeocodeAnswer NotFound { get; } = new(AddressLookup.NotFound, point: null);

    /// <summary>No answer came back: offline, no key, or the call failed.</summary>
    public static GeocodeAnswer Failed { get; } = new(AddressLookup.NotLookedUpYet, point: null);
}
