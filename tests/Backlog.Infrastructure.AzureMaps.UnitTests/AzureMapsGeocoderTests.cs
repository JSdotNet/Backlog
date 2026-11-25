using System.Globalization;
using System.Net;
using System.Text;
using Backlog.Modules.Events;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;
using Backlog.SharedKernel.Results;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Backlog.Infrastructure.AzureMaps.UnitTests;

/// <summary>
/// The Azure Maps adapter behind the Events module's geocoder port (local ADR 0061 §1, §4, §5):
/// the address's four parts go to Get Geocoding on the EU endpoint with the key in a header, the
/// first usable match becomes a point, and every failure is an answer rather than a throw. No
/// request leaves the process: a fake handler answers every one.
/// </summary>
public sealed class AzureMapsGeocoderTests : IDisposable
{
    private const string Key = "maps-key-secret";

    private readonly TemporaryDirectory _temp = new("backlog-azure-maps-geocoder");
    private readonly RecordingLogger<AzureMapsGeocoder> _logger = new();

    public void Dispose() => _temp.Dispose();

    // --- The request ----------------------------------------------------------------------

    [Fact]
    public async Task The_four_parts_go_to_get_geocoding_on_the_eu_endpoint_with_the_key_in_a_header()
    {
        var handler = Answering(Placed(52.0302, 5.0889));

        await Geocoder(handler).GeocodeAsync(AddressOf("Coltbaan 4", "3439 NG", "Nieuwegein", "NL"), Cancel);

        var request = Assert.IsType<HttpRequestMessage>(handler.Request);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://eu.atlas.microsoft.com/geocode", request.RequestUri!.GetLeftPart(UriPartial.Path));

        var query = QueryOf(request.RequestUri);
        Assert.Equal("2026-01-01", query["api-version"]);
        Assert.Equal("Coltbaan 4", query["addressLine"]);
        Assert.Equal("3439 NG", query["postalCode"]);
        Assert.Equal("Nieuwegein", query["locality"]);
        Assert.Equal("NL", query["countryRegion"]);

        Assert.Equal([Key], request.Headers.GetValues("subscription-key"));
        Assert.DoesNotContain(Key, request.RequestUri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_the_parts_present_are_sent()
    {
        var handler = Answering(Placed(52.0302, 5.0889));

        await Geocoder(handler).GeocodeAsync(AddressOf(null, null, "Nieuwegein", null), Cancel);

        var query = QueryOf(handler.Request!.RequestUri!);
        Assert.Equal("Nieuwegein", query["locality"]);
        Assert.False(query.ContainsKey("addressLine"));
        Assert.False(query.ContainsKey("postalCode"));
        Assert.False(query.ContainsKey("countryRegion"));
        Assert.False(query.ContainsKey("query"));
    }

    /// <summary>The domain's own example address: the country is typed as a name, and goes out
    /// as the code the parameter takes.</summary>
    [Theory]
    [InlineData("Belgium")]
    [InlineData("België")]
    [InlineData("be")]
    public async Task A_typed_country_goes_out_as_its_alpha_2_code(string country)
    {
        var handler = Answering(Placed(51.2194, 4.4025));

        await Geocoder(handler).GeocodeAsync(AddressOf("Groenendaallaan 394", "2030", "Antwerpen", country), Cancel);

        Assert.Equal("BE", QueryOf(handler.Request!.RequestUri!)["countryRegion"]);
    }

    [Fact]
    public async Task A_country_that_names_none_is_left_out_and_the_rest_still_goes()
    {
        var handler = Answering(Placed(51.2194, 4.4025));

        await Geocoder(handler).GeocodeAsync(AddressOf("Groenendaallaan 394", "2030", "Antwerpen", "Atlantis"), Cancel);

        var query = QueryOf(handler.Request!.RequestUri!);
        Assert.False(query.ContainsKey("countryRegion"));
        Assert.Equal("Antwerpen", query["locality"]);
        Assert.Equal("2030", query["postalCode"]);
        Assert.DoesNotContain("Atlantis", handler.Request.RequestUri!.ToString(), StringComparison.Ordinal);
    }

    /// <summary>An address that is only a country naming none leaves nothing to ask: no point
    /// can be found finer than a province, so it is not found, and nothing is sent.</summary>
    [Fact]
    public async Task An_address_that_is_only_a_country_naming_none_is_not_found_without_a_request()
    {
        var handler = Answering(Placed(51.2194, 4.4025));

        var answer = await Geocoder(handler).GeocodeAsync(AddressOf(null, null, null, "Atlantis"), Cancel);

        Assert.Same(GeocodeAnswer.NotFound, answer);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task The_endpoint_and_api_version_are_settings()
    {
        var store = Store();
        Assert.Null(store.SetApiKey(Key));
        WriteSettings(store.SettingsPath, endpoint: "https://atlas.example.test/", geocodingApiVersion: "2027-01-01");
        var handler = Answering(Placed(52.0302, 5.0889));

        await new AzureMapsGeocoder(new HttpClient(handler), new AzureMapsSettingsStore(store.SettingsPath), _logger, TimeProvider.System)
            .GeocodeAsync(AddressOf(null, null, "Nieuwegein", null), Cancel);

        Assert.Equal("https://atlas.example.test/geocode", handler.Request!.RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.Equal("2027-01-01", QueryOf(handler.Request.RequestUri)["api-version"]);
    }

    // --- The answer -----------------------------------------------------------------------

    [Fact]
    public async Task The_first_features_display_point_is_the_place()
    {
        var handler = Answering(Collection(
            Feature("Address", "High", display: (52.0302, 5.0889), geometry: (52.1, 5.1)),
            Feature("Address", "High", display: (51.0, 4.0))));

        var answer = await Geocoder(handler).GeocodeAsync(AddressOf("Coltbaan 4", null, "Nieuwegein", null), Cancel);

        Assert.Equal(AddressLookup.Placed, answer.Lookup);
        Assert.Equal(PointOf(52.0302, 5.0889), answer.Point);
    }

    [Fact]
    public async Task A_feature_without_a_display_point_is_placed_at_its_geometry()
    {
        var handler = Answering(Collection(Feature("PopulatedPlace", "Medium", display: null, geometry: (52.0302, 5.0889))));

        var answer = await Geocoder(handler).GeocodeAsync(AddressOf(null, null, "Nieuwegein", null), Cancel);

        Assert.Equal(PointOf(52.0302, 5.0889), answer.Point);
    }

    [Fact]
    public async Task No_feature_is_not_found()
    {
        var answer = await Geocoder(Answering(Collection())).GeocodeAsync(AddressOf(null, null, "Nowhere", null), Cancel);

        Assert.Same(GeocodeAnswer.NotFound, answer);
    }

    [Theory]
    [InlineData("AdminDivision1", "High")]
    [InlineData("CountryRegion", "High")]
    [InlineData("Address", "Low")]
    public async Task A_match_only_at_province_or_country_level_or_of_low_confidence_is_not_found(string type, string confidence)
    {
        var handler = Answering(Collection(Feature(type, confidence, display: (52.0, 5.0))));

        var answer = await Geocoder(handler).GeocodeAsync(AddressOf(null, null, "Utrecht", null), Cancel);

        Assert.Same(GeocodeAnswer.NotFound, answer);
    }

    [Fact]
    public async Task The_first_usable_match_is_taken_past_a_coarse_one()
    {
        var handler = Answering(Collection(
            Feature("CountryRegion", "High", display: (52.0, 5.0)),
            Feature("Postcode1", "Medium", display: (52.0302, 5.0889))));

        var answer = await Geocoder(handler).GeocodeAsync(AddressOf(null, "3439 NG", null, "NL"), Cancel);

        Assert.Equal(PointOf(52.0302, 5.0889), answer.Point);
    }

    [Fact]
    public async Task A_point_off_the_globe_is_not_a_match()
    {
        var handler = Answering(Collection(Feature("Address", "High", display: (95.0, 5.0))));

        var answer = await Geocoder(handler).GeocodeAsync(AddressOf(null, null, "Nieuwegein", null), Cancel);

        Assert.Same(GeocodeAnswer.NotFound, answer);
    }

    // --- Failures -------------------------------------------------------------------------

    [Fact]
    public async Task With_no_key_nothing_is_sent_and_no_answer_comes_back()
    {
        var handler = Answering(Placed(52.0302, 5.0889));

        var answer = await new AzureMapsGeocoder(new HttpClient(handler), Store(), _logger, TimeProvider.System)
            .GeocodeAsync(AddressOf(null, null, "Nieuwegein", null), Cancel);

        Assert.Same(GeocodeAnswer.Failed, answer);
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_refusal_is_no_answer_rather_than_not_found(HttpStatusCode status)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("""{"error":{"code":"x","message":"y"}}""", Encoding.UTF8, "application/json")
        });

        var answer = await Geocoder(handler).GeocodeAsync(AddressOf(null, null, "Nieuwegein", null), Cancel);

        Assert.Same(GeocodeAnswer.Failed, answer);
    }

    public static TheoryData<Exception> TransportFailures() =>
    [
        new HttpRequestException("No such host is known."),
        new TimeoutRejectedException("The operation didn't complete within the allowed timeout."),
        new BrokenCircuitException("The circuit is now open."),
        new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.")
    ];

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task Offline_a_timeout_or_an_open_circuit_is_no_answer(Exception failure)
    {
        var handler = new RecordingHandler(_ => throw failure);

        var answer = await Geocoder(handler).GeocodeAsync(AddressOf(null, null, "Nieuwegein", null), Cancel);

        Assert.Same(GeocodeAnswer.Failed, answer);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"features":"not an array"}""")]
    [InlineData("""{"features":[{"properties":{"type":"Address","confidence":"High"},"geometry":{"coordinates":["a","b"]}}]}""")]
    public async Task An_answer_that_does_not_read_is_no_answer(string body)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });

        var answer = await Geocoder(handler).GeocodeAsync(AddressOf(null, null, "Nieuwegein", null), Cancel);

        Assert.Same(GeocodeAnswer.Failed, answer);
    }

    [Fact]
    public async Task The_callers_own_cancellation_is_not_swallowed()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var handler = new RecordingHandler(_ => throw new TaskCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Geocoder(handler).GeocodeAsync(AddressOf(null, null, "Nieuwegein", null), cancelled.Token));
    }

    // --- What is logged (guideline 0010, ADR 0061 §5) --------------------------------------

    [Fact]
    public async Task The_outcome_status_and_duration_are_logged()
    {
        await Geocoder(Answering(Placed(52.0302, 5.0889))).GeocodeAsync(AddressOf(null, null, "Nieuwegein", null), Cancel);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal("Placed", entry.Fields["Outcome"]?.ToString());
        Assert.Equal(200, Convert.ToInt32(entry.Fields["StatusCode"], CultureInfo.InvariantCulture));
        Assert.True(entry.Fields.ContainsKey("ElapsedMilliseconds"));
    }

    public static TheoryData<string> Outcomes() => ["placed", "not-found", "refused", "throws"];

    [Theory]
    [MemberData(nameof(Outcomes))]
    public async Task Nothing_logged_carries_the_address_the_point_or_the_key(string outcome)
    {
        var handler = outcome switch
        {
            "placed" => Answering(Placed(52.0302, 5.0889)),
            "not-found" => Answering(Collection()),
            "refused" => new RecordingHandler { Status = HttpStatusCode.ServiceUnavailable },
            _ => new RecordingHandler(request => throw new HttpRequestException($"Failed for {request.RequestUri}"))
        };

        await Geocoder(handler).GeocodeAsync(AddressOf("Coltbaan 4", "3439 NG", "Nieuwegein", "NL"), Cancel);

        Assert.NotEmpty(_logger.Entries);
        foreach (var entry in _logger.Entries)
        {
            var logged = string.Join(
                " | ",
                [entry.Message, .. entry.Fields.Values.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture)), entry.Exception?.ToString()]);

            foreach (var secret in new[] { "Coltbaan", "3439", "Nieuwegein", "52.03", "5.08", Key })
            {
                Assert.DoesNotContain(secret, logged, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Null(entry.Exception);
        }
    }

    // --- Helpers --------------------------------------------------------------------------

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private AzureMapsGeocoder Geocoder(RecordingHandler handler)
    {
        var store = Store();
        Assert.Null(store.SetApiKey(Key));
        return new AzureMapsGeocoder(new HttpClient(handler), store, _logger, TimeProvider.System);
    }

    private AzureMapsSettingsStore Store() =>
        new(_temp.Combine(Guid.NewGuid().ToString("n"), "azure-maps.json"));

    /// <summary>Rewrites the non-secret settings in place, the way a person could edit the
    /// file: the endpoint and the api-version are settings, not code (ADR 0061 §1).</summary>
    private static void WriteSettings(string path, string endpoint, string geocodingApiVersion)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        json["endpoint"] = endpoint;
        json["geocodingApiVersion"] = geocodingApiVersion;
        File.WriteAllText(path, json.ToJsonString());
    }

    private static RecordingHandler Answering(string body) => new(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/geo+json")
    });

    private static string Placed(double latitude, double longitude) =>
        Collection(Feature("Address", "High", display: (latitude, longitude)));

    private static string Collection(params string[] features) =>
        $$"""{"type":"FeatureCollection","features":[{{string.Join(",", features)}}]}""";

    /// <summary>A feature as Get Geocoding 2026-01-01 answers one: coordinates longitude first.</summary>
    private static string Feature(string type, string confidence, (double Lat, double Lon)? display, (double Lat, double Lon)? geometry = null)
    {
        var point = geometry ?? display ?? (0, 0);
        var geocodePoints = display is { } shown
            ? $$"""[{"geometry":{"type":"Point","coordinates":[{{Number(shown.Lon)}},{{Number(shown.Lat)}}]},"calculationMethod":"Rooftop","usageTypes":["Display"]},{"geometry":{"type":"Point","coordinates":[1,1]},"calculationMethod":"Rooftop","usageTypes":["Route"]}]"""
            : "[]";

        return $$$"""
            {"type":"Feature","properties":{"type":"{{{type}}}","confidence":"{{{confidence}}}","matchCodes":["Good"],"geocodePoints":{{{geocodePoints}}}},
             "geometry":{"type":"Point","coordinates":[{{{Number(point.Lon)}}},{{{Number(point.Lat)}}}]}}
            """;
    }

    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static Address AddressOf(string? street, string? postcode, string? town, string? country) =>
        Address.Create(street, postcode, town, country) switch
        {
            Ok<Address>(var address) => address,
            Error error => throw new InvalidOperationException(error.Code)
        };

    private static MapPoint PointOf(double latitude, double longitude) =>
        MapPoint.Create(latitude, longitude) switch
        {
            Ok<MapPoint>(var point) => point,
            Error error => throw new InvalidOperationException(error.Code)
        };

    private static Dictionary<string, string> QueryOf(Uri uri) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => Uri.UnescapeDataString(pair.Length > 1 ? pair[1].Replace('+', ' ') : string.Empty),
                StringComparer.Ordinal);
}
