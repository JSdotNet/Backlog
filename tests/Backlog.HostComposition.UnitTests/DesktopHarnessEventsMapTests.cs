extern alias DesktopHarness;

using Backlog.Modules.Events;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;
using Backlog.SharedKernel.Results;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using EventsSeed = DesktopHarness::Backlog.Desktop.WebHarness.EventsSeed;
using HarnessAddressGeocoder = DesktopHarness::Backlog.Desktop.WebHarness.HarnessAddressGeocoder;
using HarnessEventMapTiles = DesktopHarness::Backlog.Desktop.WebHarness.HarnessEventMapTiles;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// The desktop harness's Events map fixture (local ADR 0061): a seed of events across the
/// list's sections, states and lookups, a geocoder answering from a fixed table, and tiles from
/// the harness's own origin — so the map draws, and Look up now answers, with no Azure key and
/// no network. Over a workspace of the test's own (<see cref="IsolatedHarnessFactory{TEntryPoint}"/>).
/// </summary>
public sealed class DesktopHarnessEventsMapTests
{
    /// <summary>The harness with both switches set as given, so neither the Development settings
    /// nor a variable left in the shell decides them.</summary>
    private sealed class Harness(bool seed = false, bool fakeMap = true) : IsolatedHarnessFactory<DesktopHarness::Program>
    {
        protected override void ConfigureHarness(IWebHostBuilder builder)
        {
            builder.UseSetting(EventsSeed.ConfigurationKey, seed ? "true" : "false");
            builder.UseSetting(HarnessAddressGeocoder.ConfigurationKey, fakeMap ? "true" : "false");
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The test's workspace is isolated, so the guard is told it may write there; the
    /// guard itself is proved below, in either build.</summary>
    private static Task<bool> SeedIsolatedAsync(IServiceProvider services) =>
        EventsSeed.SeedAsync(services, TimeProvider.System, refuses: _ => false, Ct);

    [Fact]
    public async Task The_seed_puts_events_on_the_map_off_it_and_out_of_its_default_view()
    {
        using var harness = new Harness();
        Assert.True(await SeedIsolatedAsync(harness.Services));
        var events = await harness.Services.GetRequiredService<IKnowledgeEventRepository>().ListAsync(Ct);

        Assert.Equal(EventsSeed.EventIds.Count, events.Count);

        var towns = events
            .Where(item => item.Place?.Lookup == AddressLookup.Placed)
            .Select(item => item.Place!.Address!.Town)
            .ToList();
        Assert.Contains("Nieuwegein", towns);
        Assert.Contains("Utrecht", towns);
        Assert.Contains("Hilversum", towns);
        Assert.Contains("Rotterdam", towns);
        Assert.Contains("Prague", towns);

        Assert.Contains(events, item => item.Place?.IsOnline == true);
        Assert.Contains(events, item => item.Place is null);
        Assert.Contains(events, item => item.Place?.Lookup == AddressLookup.NotFound);
        Assert.Contains(events, item => item.Place?.Lookup == AddressLookup.NotLookedUpYet);
        Assert.All(
            events.Where(item => item.Place?.Lookup == AddressLookup.Placed),
            item => Assert.NotNull(item.Place!.Point));

        var states = events.Select(item => item.State).ToHashSet();
        Assert.Superset(
            new HashSet<KnowledgeEventState>
            {
                KnowledgeEventState.Going,
                KnowledgeEventState.Spotted,
                KnowledgeEventState.Considering,
                KnowledgeEventState.Attended,
                KnowledgeEventState.Skipped
            },
            states);

        // Upcoming and Past: the list's two sections, from the harness's own clock.
        var today = DateOnly.FromDateTime(DateTime.Now);
        Assert.Contains(events, item => item.Start?.Date > today);
        Assert.Contains(events, item => (item.End ?? item.Start)?.Date < today);

        // Prague is placed and past, so the default view — which fits the numbered, upcoming
        // pins — leaves it out, and only a pan or a zoom out finds it.
        var prague = Assert.Single(events, item => item.Place?.Address?.Town == "Prague");
        Assert.True((prague.End ?? prague.Start)!.Date < today);
    }

    [Fact]
    public async Task Seeding_again_adds_nothing_and_keeps_what_was_changed_and_what_was_there()
    {
        using var harness = new Harness();
        var repository = harness.Services.GetRequiredService<IKnowledgeEventRepository>();
        await SeedIsolatedAsync(harness.Services);

        var own =Assert.IsType<Ok<KnowledgeEvent>>(KnowledgeEvent.Create(Guid.CreateVersion7(), "A person's own event").Value).Value;
        await repository.SaveAsync(own, Ct);

        var renamed = (await repository.GetAsync(EventsSeed.EventIds[0], Ct))!;
        Assert.IsType<Ok>(renamed.Rename("Renamed in a QA session").Value);
        await repository.SaveAsync(renamed, Ct);

        await SeedIsolatedAsync(harness.Services);

        var events = await repository.ListAsync(Ct);
        Assert.Equal(EventsSeed.EventIds.Count + 1, events.Count);
        Assert.Equal("Renamed in a QA session", (await repository.GetAsync(EventsSeed.EventIds[0], Ct))!.Title);
        Assert.Contains(events, item => item.Id == own.Id);
    }

    [Fact]
    public async Task A_seeded_event_removed_stays_removed()
    {
        using var harness = new Harness();
        var repository = harness.Services.GetRequiredService<IKnowledgeEventRepository>();
        await SeedIsolatedAsync(harness.Services);

        var removed = (await repository.GetAsync(EventsSeed.EventIds[1], Ct))!;
        Assert.IsType<Ok>(removed.Remove().Value);
        await repository.SaveAsync(removed, Ct);

        await SeedIsolatedAsync(harness.Services);

        Assert.Null(await repository.GetAsync(EventsSeed.EventIds[1], Ct));
    }

    [Fact]
    public async Task With_the_seed_off_the_harness_writes_no_event()
    {
        using var harness = new Harness(seed: false);

        Assert.Empty(await harness.Services.GetRequiredService<IKnowledgeEventRepository>().ListAsync(Ct));
    }

    /// <summary>The harness's own start, with the seed on: a Debug build seeds its (here isolated)
    /// workspace; a Release build composes the installed app's folder, so it refuses whatever
    /// the root is.</summary>
    [Fact]
    public async Task With_the_seed_on_a_debug_build_seeds_at_start_and_a_release_build_refuses()
    {
        using var harness = new Harness(seed: true);

        var events = await harness.Services.GetRequiredService<IKnowledgeEventRepository>().ListAsync(Ct);

        Assert.Equal(EventsSeed.ReleaseBuild ? 0 : EventsSeed.EventIds.Count, events.Count);
    }

    /// <summary>The workspace is the one every worktree's harness and the person's Debug app
    /// share, so a run seeds only when it asks to (<c>Harness__SeedEvents=true</c>); the fake map,
    /// which writes nothing, is on. Read from the settings files themselves, so a variable left
    /// in the shell that runs the tests does not decide it.</summary>
    [Fact]
    public void By_its_development_settings_the_harness_seeds_no_event_and_fakes_the_map()
    {
        var folder = HarnessFolder();
        var settings = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(folder, "appsettings.json"), optional: false)
            .AddJsonFile(Path.Combine(folder, "appsettings.Development.json"), optional: false)
            .Build();

        Assert.False(settings.GetValue(EventsSeed.ConfigurationKey, false));
        Assert.True(settings.GetValue(HarnessAddressGeocoder.ConfigurationKey, false));
    }

    [Fact]
    public void A_release_build_refuses_every_workspace()
    {
        using var temp = new TemporaryDirectory("events-seed-guard");
        var installed = temp.Combine("Backlog");

        Assert.True(EventsSeed.RefusesWorkspace(temp.Combine("anywhere"), releaseBuild: true, installed, Path.Combine(installed, "settings.json")));
    }

    [Fact]
    public void A_debug_build_refuses_the_installed_apps_folder_and_what_is_under_it()
    {
        using var temp = new TemporaryDirectory("events-seed-guard");
        var installed = temp.Combine("Backlog");
        var settings = Path.Combine(installed, "settings.json");

        Assert.True(EventsSeed.RefusesWorkspace(installed, releaseBuild: false, installed, settings));
        Assert.True(EventsSeed.RefusesWorkspace(installed + Path.DirectorySeparatorChar, releaseBuild: false, installed, settings));
        Assert.True(EventsSeed.RefusesWorkspace(Path.Combine(installed, "nested"), releaseBuild: false, installed, settings));
        Assert.False(EventsSeed.RefusesWorkspace(installed + ".Debug", releaseBuild: false, installed, settings));
        Assert.False(EventsSeed.RefusesWorkspace(HarnessIsolation.Root, releaseBuild: false, installed, settings));
    }

    /// <summary>The installed app's workspace is wherever its own settings file points, and that
    /// file is read as it is: nothing is created on the way.</summary>
    [Fact]
    public void A_debug_build_refuses_the_root_the_installed_apps_settings_name_and_creates_nothing()
    {
        using var temp = new TemporaryDirectory("events-seed-guard");
        var installed = temp.Combine("Backlog");
        var settings = Path.Combine(installed, "settings.json");
        var moved = temp.Combine("OneDrive", "Backlog");
        Directory.CreateDirectory(installed);
        File.WriteAllText(settings, $$"""{ "rootDirectory": {{System.Text.Json.JsonSerializer.Serialize(moved)}} }""");

        Assert.True(EventsSeed.RefusesWorkspace(moved, releaseBuild: false, installed, settings));
        Assert.True(EventsSeed.RefusesWorkspace(Path.Combine(moved, "nested"), releaseBuild: false, installed, settings));
        Assert.False(EventsSeed.RefusesWorkspace(temp.Combine("OneDrive", "Elsewhere"), releaseBuild: false, installed, settings));
        Assert.False(Directory.Exists(moved));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("""{ "rootDirectory": "  " }""")]
    [InlineData("""{ "rootDirectory": 42 }""")]
    public void Installed_settings_that_name_no_root_leave_only_the_installed_folder_refused(string? content)
    {
        using var temp = new TemporaryDirectory("events-seed-guard");
        var installed = temp.Combine("Backlog");
        var settings = Path.Combine(installed, "settings.json");
        if (content is not null)
        {
            Directory.CreateDirectory(installed);
            File.WriteAllText(settings, content);
        }

        Assert.True(EventsSeed.RefusesWorkspace(installed, releaseBuild: false, installed, settings));
        Assert.False(EventsSeed.RefusesWorkspace(temp.Combine("elsewhere"), releaseBuild: false, installed, settings));
    }

    /// <summary>A guard that cannot see the workspace does not let the write through.</summary>
    [Fact]
    public async Task With_no_workspace_settings_the_seed_writes_nothing()
    {
        var repository = new RecordingRepository();
        using var services = new ServiceCollection()
            .AddSingleton<IKnowledgeEventRepository>(repository)
            .BuildServiceProvider();

        Assert.False(await EventsSeed.SeedAsync(services, TimeProvider.System, refuses: _ => false, Ct));
        Assert.Equal(0, repository.Saves);
    }

    private sealed class RecordingRepository : IKnowledgeEventRepository
    {
        public int Saves { get; private set; }

        public Task<KnowledgeEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<KnowledgeEvent?>(null);

        public Task<IReadOnlyList<KnowledgeEvent>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KnowledgeEvent>>([]);

        public Task SaveAsync(KnowledgeEvent knowledgeEvent, CancellationToken cancellationToken = default)
        {
            Saves++;
            return Task.CompletedTask;
        }
    }

    /// <summary>The desktop harness's project folder, found from the test's output by the
    /// solution beside it.</summary>
    private static string HarnessFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var harness = Path.Combine(folder.FullName, "src", "Harness", "Backlog.Desktop.WebHarness");
            if (File.Exists(Path.Combine(folder.FullName, "Backlog.sln")) && Directory.Exists(harness)) return harness;
        }

        throw new InvalidOperationException("The desktop harness's folder was not found above the test output.");
    }

    [Fact]
    public async Task With_the_fake_map_the_harness_geocodes_from_its_table_and_draws_its_own_tiles()
    {
        using var harness = new Harness();
        using var scope = harness.Services.CreateScope();

        Assert.IsType<HarnessAddressGeocoder>(scope.ServiceProvider.GetRequiredService<IAddressGeocoder>());

        var tiles = await scope.ServiceProvider.GetRequiredService<IEventMapTiles>().GetAsync(Ct);
        Assert.NotNull(tiles);
        Assert.StartsWith("/", tiles.UrlTemplate, StringComparison.Ordinal);
        Assert.DoesNotContain("atlas.microsoft.com", tiles.UrlTemplate, StringComparison.OrdinalIgnoreCase);

        using var client = harness.CreateClient();
        var tileUrl = tiles.UrlTemplate.Replace("{z}", "7").Replace("{x}", "65").Replace("{y}", "42");
        using var response = await client.GetAsync(tileUrl, Ct);

        response.EnsureSuccessStatusCode();
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("7/65/42", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_the_fake_map_the_harness_keeps_the_composed_adapters()
    {
        using var harness = new Harness(fakeMap: false);
        using var scope = harness.Services.CreateScope();

        Assert.IsNotType<HarnessAddressGeocoder>(scope.ServiceProvider.GetRequiredService<IAddressGeocoder>());
        Assert.IsNotType<HarnessEventMapTiles>(scope.ServiceProvider.GetRequiredService<IEventMapTiles>());

        using var client = harness.CreateClient();
        using var response = await client.GetAsync("/harness/map-tiles/1/0/0.svg", Ct);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Utrecht", "NL")]
    [InlineData("nieuwegein", null)]
    [InlineData("Hilversum", "Nederland")]
    [InlineData("Rotterdam", "Netherlands")]
    [InlineData("Praha", "CZ")]
    [InlineData("'s-Hertogenbosch", "NL")]
    public async Task The_fake_geocoder_places_a_town_in_its_table(string town, string? country)
    {
        var address = Assert.IsType<Ok<Address>>(Address.Create(street: null, postcode: null, town, country).Value).Value;

        var answer = await new HarnessAddressGeocoder().GeocodeAsync(address, Ct);

        Assert.Equal(AddressLookup.Placed, answer.Lookup);
        Assert.NotNull(answer.Point);
    }

    [Fact]
    public async Task The_fake_geocoder_finds_no_town_outside_its_table()
    {
        var address = Assert.IsType<Ok<Address>>(Address.Create("Nowhere Lane 1", null, "Atlantis", null).Value).Value;

        Assert.Same(GeocodeAnswer.NotFound, await new HarnessAddressGeocoder().GeocodeAsync(address, Ct));
    }

    [Fact]
    public async Task The_fake_geocoder_answers_nothing_for_the_offline_town_as_a_failed_call_would()
    {
        var address = Assert.IsType<Ok<Address>>(Address.Create(null, null, HarnessAddressGeocoder.OfflineTown, null).Value).Value;

        Assert.Same(GeocodeAnswer.Failed, await new HarnessAddressGeocoder().GeocodeAsync(address, Ct));
    }
}
