using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Backlog.Modules.Events;
using Backlog.Modules.Events.DomainModels;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Backlog.Infrastructure.AzureMaps;

/// <summary>
/// The Events module's geocoder over Azure Maps Search's Get Geocoding (local ADR 0061 §1, §4,
/// §5). The address's four parts go out as the structured parameters — street to
/// <c>addressLine</c>, postcode to <c>postalCode</c>, town to <c>locality</c>, country to
/// <c>countryRegion</c> as the ISO alpha-2 code it names, or not at all when it names none
/// (<see cref="AzureMapsCountry"/>) — on the endpoint the settings name, the EU one by default, with the key
/// in the <c>subscription-key</c> header and never in the URL.
/// <para>
/// A usable match is the first feature of <c>High</c> or <c>Medium</c> confidence whose type is
/// finer than a province; its <c>Display</c> point is the place. A feature only at province or
/// country level counts as not found. No key, being offline, a timeout, an open circuit and any
/// refusal are <see cref="GeocodeAnswer.Failed"/>, answered rather than thrown, and with no key no
/// request is made at all.
/// </para>
/// <para>
/// It logs the outcome, the status code and the duration, and nothing else: no address, no point,
/// no key, and no exception, whose message may quote the request URI and so the address
/// (guideline 0010).
/// </para>
/// </summary>
public sealed class AzureMapsGeocoder(
    HttpClient http,
    AzureMapsSettingsStore settings,
    ILogger<AzureMapsGeocoder> logger,
    TimeProvider time) : IAddressGeocoder
{
    /// <summary>The feature types that place an address only at province or country level
    /// (ADR 0061 §5). Every other type Get Geocoding answers is finer.</summary>
    private static readonly HashSet<string> CoarseTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AdminDivision1",
        "CountryRegion"
    };

    public async Task<GeocodeAnswer> GeocodeAsync(Address address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        var current = settings.Current;
        if (!current.IsConfigured)
        {
            // No key is no consent: nothing leaves the machine (ADR 0061 §4).
            Log(Outcome.NoKey, status: null, elapsed: TimeSpan.Zero);
            return GeocodeAnswer.Failed;
        }

        var country = AzureMapsCountry.CodeOf(address.Country);
        if (address is { Street: null, Postcode: null, Town: null } && country is null)
        {
            // Only a country that names none: nothing finer than a province could be found, so
            // there is nothing to ask.
            Log(Outcome.NotFound, status: null, elapsed: TimeSpan.Zero);
            return GeocodeAnswer.NotFound;
        }

        var started = time.GetTimestamp();

        using var request = new HttpRequestMessage(HttpMethod.Get, RequestUri(current, address, country));
        request.Headers.TryAddWithoutValidation("subscription-key", current.ApiKey);

        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Log(Outcome.Refused, response.StatusCode, time.GetElapsedTime(started));
                return GeocodeAnswer.Failed;
            }

            var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var answer = await ReadAsync(body, cancellationToken).ConfigureAwait(false);

            Log(
                answer is null ? Outcome.Unreadable : answer.Point is null ? Outcome.NotFound : Outcome.Placed,
                response.StatusCode,
                time.GetElapsedTime(started));

            return answer ?? GeocodeAnswer.Failed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException or OperationCanceledException)
        {
            // The type alone: an HttpRequestException's message can quote the request URI, which
            // carries the address in its query.
            Log(Outcome.Unreachable, status: null, time.GetElapsedTime(started), ex.GetType().Name);
            return GeocodeAnswer.Failed;
        }
    }

    /// <summary><c>{endpoint}/geocode?api-version=…</c> and a parameter for each part the
    /// address has; a part it lacks is left out rather than sent empty. The country goes out as
    /// <paramref name="countryCode"/>, the alpha-2 code <see cref="AzureMapsCountry"/> read from
    /// it, and is left out when it named none.</summary>
    internal static Uri RequestUri(AzureMapsSettings settings, Address address, string? countryCode)
    {
        var uri = new StringBuilder(settings.Endpoint)
            .Append("/geocode?api-version=")
            .Append(Uri.EscapeDataString(settings.GeocodingApiVersion));

        Append(uri, "addressLine", address.Street);
        Append(uri, "postalCode", address.Postcode);
        Append(uri, "locality", address.Town);
        Append(uri, "countryRegion", countryCode);

        return new Uri(uri.ToString());

        static void Append(StringBuilder uri, string name, string? value)
        {
            if (value is null) return;
            uri.Append('&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
        }
    }

    /// <summary>The answer a Get Geocoding body gives, or null when it does not read as one.</summary>
    private static async Task<GeocodeAnswer?> ReadAsync(Stream body, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("features", out var features)
                || features.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var feature in features.EnumerateArray())
            {
                if (!IsUsable(feature)) continue;

                switch (PointOf(feature))
                {
                    case Ok<MapPoint>(var point):
                        return GeocodeAnswer.Placed(point);
                    case Error:
                        continue;
                }
            }

            return GeocodeAnswer.NotFound;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static bool IsUsable(JsonElement feature)
    {
        if (!feature.TryGetProperty("properties", out var properties)) return false;

        var confidence = properties.TryGetProperty("confidence", out var c) ? c.GetString() : null;
        var type = properties.TryGetProperty("type", out var t) ? t.GetString() : null;

        return confidence is not null
            && (confidence.Equals("High", StringComparison.OrdinalIgnoreCase) || confidence.Equals("Medium", StringComparison.OrdinalIgnoreCase))
            && type is not null
            && !CoarseTypes.Contains(type);
    }

    /// <summary>The feature's <c>Display</c> geocode point, else its own geometry; GeoJSON gives
    /// longitude first.</summary>
    private static Result<MapPoint> PointOf(JsonElement feature)
    {
        var coordinates = DisplayCoordinates(feature)
            ?? (feature.TryGetProperty("geometry", out var geometry) && geometry.TryGetProperty("coordinates", out var own) ? own : (JsonElement?)null);

        if (coordinates is not { ValueKind: JsonValueKind.Array } pair || pair.GetArrayLength() < 2)
        {
            throw new FormatException("A feature without coordinates.");
        }

        return MapPoint.Create(latitude: pair[1].GetDouble(), longitude: pair[0].GetDouble());
    }

    private static JsonElement? DisplayCoordinates(JsonElement feature)
    {
        if (!feature.GetProperty("properties").TryGetProperty("geocodePoints", out var points)
            || points.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var point in points.EnumerateArray())
        {
            if (point.TryGetProperty("usageTypes", out var usages)
                && usages.ValueKind == JsonValueKind.Array
                && usages.EnumerateArray().Any(usage => string.Equals(usage.GetString(), "Display", StringComparison.OrdinalIgnoreCase))
                && point.TryGetProperty("geometry", out var geometry)
                && geometry.TryGetProperty("coordinates", out var coordinates))
            {
                return coordinates;
            }
        }

        return null;
    }

    private void Log(Outcome outcome, HttpStatusCode? status, TimeSpan elapsed, string? failure = null)
    {
        var level = outcome is Outcome.Placed or Outcome.NotFound or Outcome.NoKey ? LogLevel.Information : LogLevel.Warning;

        logger.Log(
            level,
            "Azure Maps geocoding: {Outcome}, status {StatusCode}, {ElapsedMilliseconds} ms, failure {Failure}",
            outcome,
            status is null ? null : (int)status,
            (long)elapsed.TotalMilliseconds,
            failure);
    }

    private enum Outcome
    {
        Placed,
        NotFound,
        NoKey,
        Refused,
        Unreachable,
        Unreadable
    }
}
