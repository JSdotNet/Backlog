using System.Globalization;
using System.Text;
using Backlog.Modules.Events;
using Backlog.Modules.Events.DomainModels;

namespace Backlog.Desktop.WebHarness;

/// <summary>
/// A geocoder for this harness that answers from a fixed table of towns, so an address saved, or
/// looked up with Look up now, gets an answer with no Azure Maps key and no network (local ADR
/// 0061). The harness registers it in place of the Azure Maps adapter, with
/// <see cref="HarnessEventMapTiles"/> for the tiles, when <see cref="ConfigurationKey"/> is true.
/// Its Development settings turn that on.
/// <para>
/// Only the town is read, compared without case or accents. A town in <see cref="Towns"/> is
/// Placed at that town's centre. <see cref="OfflineTown"/> gets no answer back, as an offline or
/// failed call would, so the "nothing was looked up" path can be driven. Anything else, an address
/// with no town included, is Address not found.
/// </para>
/// </summary>
public sealed class HarnessAddressGeocoder : IAddressGeocoder
{
    /// <summary>The configuration value that swaps this geocoder and the local tiles in.</summary>
    public const string ConfigurationKey = "Events:FakeMap";

    /// <summary>The town that gets no answer back, as a call that failed would.</summary>
    public const string OfflineTown = "Offline";

    /// <summary>The towns this geocoder places, by name, at their centres.</summary>
    public static IReadOnlyDictionary<string, (double Latitude, double Longitude)> Towns { get; } =
        new Dictionary<string, (double, double)>(StringComparer.Ordinal)
        {
            ["utrecht"] = (52.0907, 5.1214),
            ["nieuwegein"] = (52.0292, 5.0807),
            ["hilversum"] = (52.2292, 5.1669),
            ["rotterdam"] = (51.9225, 4.4792),
            ["amsterdam"] = (52.3676, 4.9041),
            ["amersfoort"] = (52.1561, 5.3878),
            ["'s-hertogenbosch"] = (51.6978, 5.3037),
            ["den bosch"] = (51.6978, 5.3037),
            ["antwerp"] = (51.2194, 4.4025),
            ["antwerpen"] = (51.2194, 4.4025),
            ["prague"] = (50.0755, 14.4378),
            ["praha"] = (50.0755, 14.4378),
        };

    public Task<GeocodeAnswer> GeocodeAsync(Address address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        var town = Normalise(address.Town);
        if (town == Normalise(OfflineTown)) return Task.FromResult(GeocodeAnswer.Failed);
        if (town is null || !Towns.TryGetValue(town, out var centre)) return Task.FromResult(GeocodeAnswer.NotFound);

        return MapPoint.Create(centre.Latitude, centre.Longitude).Value switch
        {
            Backlog.SharedKernel.Results.Ok<MapPoint> placed => Task.FromResult(GeocodeAnswer.Placed(placed.Value)),
            _ => Task.FromResult(GeocodeAnswer.NotFound),
        };
    }

    /// <summary>Lower case, accents dropped, a typographic apostrophe read as a straight one.</summary>
    private static string? Normalise(string? town)
    {
        if (string.IsNullOrWhiteSpace(town)) return null;

        var decomposed = town.Trim().Replace('’', '\'').ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var kept = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) kept.Append(c);
        }

        return kept.ToString().Normalize(NormalizationForm.FormC);
    }
}
