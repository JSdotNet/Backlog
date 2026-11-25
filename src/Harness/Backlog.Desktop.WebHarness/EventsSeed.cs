using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Events;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;
using Backlog.SharedKernel.Results;
using Backlog.SharedKernel.Storage;

namespace Backlog.Desktop.WebHarness;

/// <summary>
/// Knowledge events for the Events pane and its map to show in this harness (local ADR 0061):
/// Upcoming and Past, every state but Harvested, placed in Nieuwegein, Utrecht, Hilversum and
/// Rotterdam, one placed in Prague (past, so outside the map's default view, which fits the
/// numbered pins), one online, one with no venue or address, one whose address was not found,
/// and one not looked up yet, which <see cref="HarnessAddressGeocoder"/> places on Look up now.
/// Dated from today on the first start.
/// <para>
/// Each event has a fixed id and is added only while the workspace holds no event, live or
/// removed, under that id. So a second start adds nothing, an edit made in a session stays, a
/// removed one stays removed, and nothing else in the workspace is touched. The workspace is the
/// one every worktree's harness shares (<c>Backlog.Debug</c>), so other sessions see these
/// events too. It refuses the installed app's own workspace: in any Release build, which composes
/// it, and in a Debug one whatever root the installed app's settings name. Harness only, and only while <see cref="ConfigurationKey"/> is on;
/// it is off by default, in Development too, because that workspace is shared: a run that wants
/// the events sets <c>Harness__SeedEvents=true</c>.
/// </para>
/// </summary>
internal static class EventsSeed
{
    /// <summary>The configuration value that turns the seed on.</summary>
    public const string ConfigurationKey = "Harness:SeedEvents";

    /// <summary>The seeded events' ids, in the order they are listed below.</summary>
    public static IReadOnlyList<Guid> EventIds { get; } =
    [
        .. Enumerable.Range(1, 11).Select(n => Guid.Parse($"0195e7e0-5eed-7000-8000-{n:D12}"))
    ];

    /// <summary>Whether this is a Release build, whose head folder is the installed app's own
    /// (<see cref="AppDataPaths"/>): every workspace it composes is the person's.</summary>
    public static bool ReleaseBuild { get; } =
        string.Equals(AppDataPaths.HeadFolderName, AppDataPaths.SharedFolderName, StringComparison.Ordinal);

    /// <summary>Whether the seed must not write into <paramref name="rootDirectory"/>, on this
    /// machine and in this build.</summary>
    public static bool RefusesWorkspace(string rootDirectory) =>
        RefusesWorkspace(rootDirectory, ReleaseBuild, AppDataPaths.Shared(), AppDataPaths.Shared("settings.json"));

    /// <summary>
    /// Whether the seed must not write into <paramref name="rootDirectory"/>: always in a Release
    /// build, whose workspace is the installed app's wherever it was moved; otherwise when the root
    /// is <paramref name="installedFolder"/>, the root <paramref name="installedSettingsPath"/>
    /// names, or under either. The settings file is read as it is, never through
    /// <see cref="WorkspaceSettingsStore"/>, which creates folders; one that cannot be read names
    /// no root, as the installed app then falls back to its own folder.
    /// </summary>
    public static bool RefusesWorkspace(string rootDirectory, bool releaseBuild, string installedFolder, string installedSettingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        if (releaseBuild) return true;

        return IsAtOrUnder(rootDirectory, installedFolder)
            || (InstalledRoot(installedSettingsPath) is { } moved && IsAtOrUnder(rootDirectory, moved));
    }

    private static bool IsAtOrUnder(string path, string folder)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var limit = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

        return string.Equals(root, limit, StringComparison.OrdinalIgnoreCase)
            || root.StartsWith(limit + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The <c>rootDirectory</c> the installed app's settings file names, or null.</summary>
    private static string? InstalledRoot(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath)) return null;

            using var settings = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (settings.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;

            foreach (var property in settings.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "rootDirectory", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == System.Text.Json.JsonValueKind.String
                    && property.Value.GetString() is { } root
                    && !string.IsNullOrWhiteSpace(root))
                {
                    return root;
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Adds each seeded event the workspace does not hold yet. Answers false, and writes
    /// nothing, when the workspace is one <see cref="RefusesWorkspace(string)"/> refuses, or when
    /// no workspace is composed to ask about.</summary>
    public static Task<bool> SeedAsync(IServiceProvider services, TimeProvider clock, CancellationToken cancellationToken = default) =>
        SeedAsync(services, clock, RefusesWorkspace, cancellationToken);

    /// <summary>As above, with the guard given: a test passes one for its isolated workspace.</summary>
    public static async Task<bool> SeedAsync(IServiceProvider services, TimeProvider clock, Func<string, bool> refuses, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refuses);

        // Fails closed: with no workspace to ask about, the guard cannot say the write is safe.
        if (services.GetService<WorkspaceSettingsStore>() is not { } workspace || refuses(workspace.RootDirectory))
            return false;

        var repository = services.GetRequiredService<IKnowledgeEventRepository>();
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        foreach (var seeded in Events(today))
        {
            if (await repository.GetAsync(seeded.Id, cancellationToken) is not null) continue;
            if (await repository.IsRemovedAsync(seeded.Id, cancellationToken)) continue;

            await repository.SaveAsync(seeded, cancellationToken);
        }

        return true;
    }

    private static IEnumerable<KnowledgeEvent> Events(DateOnly today)
    {
        var id = EventIds.GetEnumerator();
        Guid Next() => id.MoveNext() ? id.Current : throw new InvalidOperationException("The seed lists more events than it has ids.");

        yield return Event(Next(), "Techorama 2026", KnowledgeEventState.Going,
            today.AddDays(5), today.AddDays(7),
            Placed("Kinepolis Jaarbeurs", "Jaarbeursboulevard 300", "3521 BX", "Utrecht", "NL", 52.0884, 5.1050),
            link: "https://techorama.nl", whatToLearn: "Aspire in production");

        yield return Event(Next(), "Dutch .NET Meetup", KnowledgeEventState.Considering,
            today.AddDays(12), null,
            Placed("Business Center Nieuwegein", "Newtonbaan 5", "3439 NK", "Nieuwegein", "NL", 52.0292, 5.0807));

        yield return Event(Next(), "Media Park Tech Day", KnowledgeEventState.Spotted,
            today.AddDays(19), null,
            Placed("Beeld en Geluid", "Media Parkboulevard 1", "1217 WE", "Hilversum", "NL", 52.2357, 5.1716));

        yield return Event(Next(), "Rotterdam Cloud Day", KnowledgeEventState.Going,
            today.AddDays(33), today.AddDays(34),
            Placed("Rotterdam Ahoy", "Ahoyweg 10", "3084 BA", "Rotterdam", "NL", 51.8838, 4.4882));

        // Upcoming, but Skipped: it keeps its date section, gets no number and no pin.
        yield return Event(Next(), "Cloud Summit", KnowledgeEventState.Skipped,
            today.AddDays(15), null,
            Placed("Jaarbeurs", "Jaarbeursplein 6", "3521 AL", "Utrecht", "NL", 52.0894, 5.1077),
            skipReason: "Clashes with the release");

        yield return Event(Next(), "Build 2027", KnowledgeEventState.Spotted,
            today.AddDays(45), today.AddDays(47), Place.Online, link: "https://build.microsoft.com");

        // No venue and no address yet: "No venue yet · not on the map".
        yield return Event(Next(), "Community Day", KnowledgeEventState.Considering,
            today.AddDays(9), null, place: null);

        // Looked up once, and not found: the fake geocoder knows no Atlantis either.
        yield return Event(Next(), "Lost City Meetup", KnowledgeEventState.Spotted,
            today.AddDays(26), null,
            WithAddress("De Kelder", "Nergensstraat 1", null, "Atlantis", null, AddressLookup.NotFound));

        // Not looked up yet, as an address saved with no key or offline is: Look up now places it.
        yield return Event(Next(), "Brabant DevDay", KnowledgeEventState.Considering,
            today.AddDays(40), null,
            WithAddress("Theater aan de Parade", "Parade 23", "5211 KL", "'s-Hertogenbosch", "NL", AddressLookup.NotLookedUpYet));

        // Past, placed in Prague: grey, unnumbered, and outside the view that fits the numbered pins.
        yield return Event(Next(), "Update Conference", KnowledgeEventState.Attended,
            today.AddDays(-20), today.AddDays(-18),
            Placed("Clarion Congress Hotel", "Freyova 33", "190 00", "Prague", "CZ", 50.1060, 14.4930));

        yield return Event(Next(), "Techorama 2025", KnowledgeEventState.Attended,
            today.AddDays(-60), today.AddDays(-58),
            Placed("Kinepolis Jaarbeurs", "Jaarbeursboulevard 300", "3521 BX", "Utrecht", "NL", 52.0884, 5.1050));
    }

    private static KnowledgeEvent Event(
        Guid id,
        string title,
        KnowledgeEventState state,
        DateOnly start,
        DateOnly? end,
        Place? place,
        string? link = null,
        string? whatToLearn = null,
        string? skipReason = null) =>
        KnowledgeEvent.Restore(
            id,
            title,
            series: null,
            start: Value(EventDate.Create(start, time: null, utcOffset: null)),
            end: end is { } last ? Value(EventDate.Create(last, time: null, utcOffset: null)) : null,
            dateSource: DateSource.Typed,
            place: place,
            link: link,
            state: state,
            skipReason: skipReason,
            whatToLearn: whatToLearn,
            keyDates: [],
            ticket: null,
            cost: null,
            notes: [],
            tasks: [],
            verdict: null);

    private static Place Placed(string venue, string street, string postcode, string town, string country, double latitude, double longitude) =>
        Value(Place.Create(
            venue,
            online: false,
            Value(Address.Create(street, postcode, town, country)),
            AddressLookup.Placed,
            Value(MapPoint.Create(latitude, longitude))));

    private static Place WithAddress(string venue, string street, string? postcode, string town, string? country, AddressLookup lookup) =>
        Value(Place.Create(venue, online: false, Value(Address.Create(street, postcode, town, country)), lookup, point: null));

    private static T Value<T>(Result<T> result) => result.Value switch
    {
        Ok<T> ok => ok.Value,
        Error error => throw new InvalidOperationException($"The events seed is not valid: {error}"),
        _ => throw new InvalidOperationException("The events seed is not valid."),
    };
}
