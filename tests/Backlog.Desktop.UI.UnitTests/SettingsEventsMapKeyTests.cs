using System.Reflection;

using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.AzureMaps;
using Backlog.Modules.Events.Abstractions;
using Backlog.SharedKernel;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Events page of the Settings screen: the Azure Maps key the Events map draws its tiles
/// and looks up addresses with (local ADR 0061 §4), kept the way the Azure Foundry key is -
/// sealed at rest, never shown again, set and forgotten - and the pass its first save starts
/// over the addresses saved before it (<c>.devbook/domain/events/domain.md#address-lookup</c>).
/// </summary>
public sealed class SettingsEventsMapKeyTests
{
    private const string Key = "maps-key-0123456789";

    [Fact]
    public void The_page_is_a_product_area_offered_while_events_is_on_and_draws_the_card_it_declares()
    {
        using var page = Open();

        Assert.Equal(["Azure Maps"], page.Settings.Component.FindAll("[data-testid='events-settings'] .settings-card__title").Select(title => title.TextContent.Trim()));
        Assert.Equal(
            SettingsPages.BuiltIn.Single(section => section.Id == SettingsPages.Events).CardTitles!,
            page.Settings.Component.FindAll("[data-testid='events-settings'] .settings-card__title").Select(title => title.TextContent.Trim()));
        Assert.Equal("The Events map needs an Azure Maps key.", Headline(page));
        Assert.NotNull(page.Settings.Component.Find("[data-testid='settings-menu-productareas'] a[data-page='events']"));
    }

    [Fact]
    public void With_events_off_or_no_key_store_there_is_no_events_page()
    {
        using var eventsOff = Render(eventsOn: false);
        Assert.DoesNotContain("Events", SettingsMenu.Pages(eventsOff.Settings.Component));

        using var noStore = Render(eventsOn: true, withStore: false);
        Assert.DoesNotContain("Events", SettingsMenu.Pages(noStore.Settings.Component));
    }

    /// <summary>A stored secret that renders back into an input is one screenshot away from
    /// being shared, so the placeholder says a key is there and nothing else does.</summary>
    [Fact]
    public void The_key_is_stored_sealed_and_never_rendered_back()
    {
        using var page = Open();

        var input = KeyInput(page);
        Assert.Equal("password", input.GetAttribute("type"));
        Assert.Equal("key", input.GetAttribute("placeholder"));

        Enter(page, $"  {Key}  ");

        Assert.Equal(Key, page.Store.Current.ApiKey);
        Assert.DoesNotContain(Key, File.ReadAllText(page.Store.SettingsPath), StringComparison.Ordinal);
        Assert.DoesNotContain(Key, page.Settings.Component.Markup, StringComparison.Ordinal);
        Assert.Equal(string.Empty, KeyInput(page).GetAttribute("value") ?? string.Empty);
        Assert.Equal(EventsSettingsPage.StoredHidden, KeyInput(page).GetAttribute("placeholder"));
        Assert.Equal("Azure Maps key stored.", Status(page));
        Assert.Equal("The Events map draws from Azure Maps.", Headline(page));
    }

    [Fact]
    public void Forgetting_the_key_clears_it_and_says_so()
    {
        using var page = Open();
        Assert.True(ForgetButton(page).HasAttribute("disabled"));

        Enter(page, Key);
        ForgetButton(page).Click();

        Assert.Null(page.Store.Current.ApiKey);
        Assert.Equal("Azure Maps key forgotten.", Status(page));
        Assert.Equal("key", KeyInput(page).GetAttribute("placeholder"));
        Assert.True(ForgetButton(page).HasAttribute("disabled"));
        Assert.Equal("The Events map needs an Azure Maps key.", Headline(page));
    }

    /// <summary>The store keeps the key for this session even when its file cannot be
    /// written, and says so; the page shows that sentence rather than "stored".</summary>
    [Fact]
    public void The_stores_own_message_is_shown_when_the_key_cannot_be_kept()
    {
        using var page = Open(unwritable: true);

        Enter(page, Key);

        Assert.Equal("Changed, but the Azure Maps settings couldn't be saved for next time.", Status(page));
    }

    [Fact]
    public void A_blank_entry_stores_nothing()
    {
        using var page = Open();

        Enter(page, "   ");

        Assert.Null(page.Store.Current.ApiKey);
        Assert.Equal(0, page.Pane.Passes);
        Assert.DoesNotContain("stored", Status(page), StringComparison.Ordinal);
    }

    /// <summary>
    /// The key's first save looks up each address saved before it, once. Enter raises keydown
    /// and then change, and the pass starts once for both. The field is not held while the
    /// geocoder answers: the page says the pass is running while the pane still holds it.
    /// </summary>
    [Fact]
    public void The_first_saved_key_starts_one_pass_off_the_save_and_reports_what_it_got_back()
    {
        using var page = Open();
        page.Pane.Hold();

        var input = KeyInput(page);
        input.Input(Key);
        input.KeyDown("Enter");
        KeyInput(page).Change(string.Empty);

        Assert.Equal(Key, page.Store.Current.ApiKey);
        page.Settings.Component.WaitForAssertion(() =>
        {
            Assert.Equal(1, page.Pane.Passes);
            Assert.Equal(EventsSettingsPage.LookingUp, PassStatus(page));
        });

        page.Pane.Answer(new AddressLookupPassDto(Placed: 2, NotFound: 1, NotLookedUpYet: 0));

        page.Settings.Component.WaitForAssertion(() => Assert.Equal(
            "Looked up 3 addresses saved before the key: 2 on the map, 1 not found, 0 not looked up yet.",
            PassStatus(page)));
        Assert.Equal(1, page.Pane.Passes);
    }

    [Fact]
    public void A_first_key_with_nothing_waiting_says_so()
    {
        using var page = Open();

        Enter(page, Key);

        page.Settings.Component.WaitForAssertion(() =>
            Assert.Equal("No address was waiting to be looked up.", PassStatus(page)));
    }

    [Fact]
    public void A_pass_that_fails_says_the_next_save_looks_the_address_up()
    {
        using var page = Open();
        page.Pane.Fail();

        Enter(page, Key);

        page.Settings.Component.WaitForAssertion(() => Assert.Equal(
            "Couldn't look up the addresses saved before the key. Saving an event looks its address up.",
            PassStatus(page)));
    }

    /// <summary>Only the key's first save is the act that looks the waiting addresses up; a
    /// key replaced by another starts nothing.</summary>
    [Fact]
    public void A_replaced_key_starts_no_pass()
    {
        using var page = Open(storedKey: "old-key");
        Assert.Equal(EventsSettingsPage.StoredHidden, KeyInput(page).GetAttribute("placeholder"));

        Enter(page, Key);

        Assert.Equal(Key, page.Store.Current.ApiKey);
        Assert.Equal(0, page.Pane.Passes);
        Assert.Empty(page.Settings.Component.FindAll("[data-testid='azure-maps-pass-status']"));
    }

    /// <summary>
    /// One pass at a time per app: a key forgotten and saved again while the first save's pass
    /// still runs joins that pass, so no waiting address goes to Azure Maps twice at once
    /// (local ADR 0061 §5).
    /// </summary>
    [Fact]
    public void A_second_first_save_while_the_pass_runs_joins_it()
    {
        using var page = Open();
        page.Pane.Hold();

        Enter(page, Key);
        ForgetButton(page).Click();
        Enter(page, "another-key");

        page.Settings.Component.WaitForAssertion(() => Assert.Equal(EventsSettingsPage.LookingUp, PassStatus(page)));
        Assert.Equal(1, page.Pane.Passes);

        page.Pane.Answer(new AddressLookupPassDto(Placed: 1, NotFound: 0, NotLookedUpYet: 0));

        page.Settings.Component.WaitForAssertion(() => Assert.Equal(
            "Looked up 1 address saved before the key: 1 on the map, 0 not found, 0 not looked up yet.",
            PassStatus(page)));
        Assert.Equal(1, page.Pane.Passes);
    }

    /// <summary>
    /// The pass belongs to the app, not to the page that started it: Settings opened in a
    /// second window while it runs says so, starts no second one on its own first save, and
    /// shows the same answer when it ends.
    /// </summary>
    [Fact]
    public void Another_window_sees_the_running_pass_and_joins_it()
    {
        using var first = Open();
        first.Pane.Hold();
        Enter(first, Key);

        using var second = OpenBeside(first);
        second.Settings.Component.WaitForAssertion(() => Assert.Equal(EventsSettingsPage.LookingUp, PassStatus(second)));

        ForgetButton(second).Click();
        Enter(second, "another-key");

        Assert.Equal(1, first.Pane.Passes);
        Assert.Equal(0, second.Pane.Passes);

        first.Pane.Answer(new AddressLookupPassDto(Placed: 0, NotFound: 2, NotLookedUpYet: 0));

        const string Answer = "Looked up 2 addresses saved before the key: 0 on the map, 2 not found, 0 not looked up yet.";
        first.Settings.Component.WaitForAssertion(() => Assert.Equal(Answer, PassStatus(first)));
        second.Settings.Component.WaitForAssertion(() => Assert.Equal(Answer, PassStatus(second)));
    }

    /// <summary>A pass that has ended frees the way: the key's next first save starts a new one.</summary>
    [Fact]
    public void A_first_save_after_the_pass_ended_starts_a_new_one()
    {
        using var page = Open();
        Enter(page, Key);
        page.Settings.Component.WaitForAssertion(() =>
            Assert.Equal("No address was waiting to be looked up.", PassStatus(page)));

        ForgetButton(page).Click();
        Enter(page, "another-key");

        page.Settings.Component.WaitForAssertion(() => Assert.Equal(2, page.Pane.Passes));
    }

    [Theory]
    [InlineData(1, 0, 0, "Looked up 1 address saved before the key: 1 on the map, 0 not found, 0 not looked up yet.")]
    [InlineData(0, 0, 2, "Looked up 2 addresses saved before the key: 0 on the map, 0 not found, 2 not looked up yet.")]
    [InlineData(0, 0, 0, "No address was waiting to be looked up.")]
    public void The_pass_line_counts_each_answer(int placed, int notFound, int notLookedUpYet, string expected) =>
        Assert.Equal(expected, EventsSettingsPage.PassLine(new AddressLookupPassDto(placed, notFound, notLookedUpYet)));

    private static Page Open(string? storedKey = null, bool unwritable = false)
    {
        var page = Render(eventsOn: true, storedKey: storedKey, unwritable: unwritable);
        SettingsMenu.Open(page.Settings.Component, "Events");
        return page;
    }

    /// <summary>The Events page in a second window of the same app: the same key store, its own
    /// screen and its own Events pane.</summary>
    private static Page OpenBeside(Page first)
    {
        var page = Render(eventsOn: true, sharedStore: first.Store);
        SettingsMenu.Open(page.Settings.Component, "Events");
        return page;
    }

    private static Page Render(
        bool eventsOn,
        bool withStore = true,
        string? storedKey = null,
        bool unwritable = false,
        AzureMapsSettingsStore? sharedStore = null)
    {
        var temp = new TemporaryDirectory("backlog-settings-events-tests");
        var path = Path.Combine(temp.Path, "azure-maps.json");

        // A folder where the file should be: nothing reads from it, and every write is refused.
        if (unwritable) Directory.CreateDirectory(path);

        var store = sharedStore ?? new AzureMapsSettingsStore(path);
        if (storedKey is not null) _ = store.SetApiKey(storedKey);

        var pane = DispatchProxy.Create<IEventsPane, PendingPassPane>();
        var pass = (PendingPassPane)(object)pane;

        var settings = SettingsSectionRenderingTests.RenderSettings(services =>
        {
            var features = (IAppFeatureSettings)services
                .Single(descriptor => descriptor.ServiceType == typeof(IAppFeatureSettings))
                .ImplementationInstance!;
            _ = features.SetEnabled(EventsFeatures.Events, eventsOn);

            if (withStore) services.AddSingleton(store);
            services.AddScoped(_ => pane);
        });

        return new Page(temp, settings, store, pass);
    }

    private static void Enter(Page page, string text)
    {
        var input = KeyInput(page);
        input.Input(text);
        input.Change(text);
    }

    private static AngleSharp.Dom.IElement KeyInput(Page page) =>
        page.Settings.Component.Find("[data-testid='azure-maps-key-input']");

    private static AngleSharp.Dom.IElement ForgetButton(Page page) =>
        page.Settings.Component.Find("[data-testid='azure-maps-clear-key-button']");

    private static string Status(Page page) =>
        page.Settings.Component.Find("[data-testid='azure-maps-status']").TextContent.Trim();

    private static string PassStatus(Page page) =>
        page.Settings.Component.Find("[data-testid='azure-maps-pass-status']").TextContent.Trim();

    private static string Headline(Page page) =>
        page.Settings.Component.Find("[data-testid='settings-headline']").TextContent.Trim();

    private sealed record Page(
        TemporaryDirectory Temp,
        SettingsSectionRenderingTests.SettingsRenderContext Settings,
        AzureMapsSettingsStore Store,
        PendingPassPane Pane) : IDisposable
    {
        public void Dispose()
        {
            Settings.Dispose();
            Temp.Dispose();
        }
    }

    /// <summary>
    /// The Events pane as the page reaches it: only <see cref="IEventsPane.LookUpPendingAsync"/>
    /// answers, counting its calls; any other member throws. A proxy rather than a class, so a
    /// member the pane gains does not make every settings test change.
    /// </summary>
    public class PendingPassPane : DispatchProxy
    {
        private readonly TaskCompletionSource<AddressLookupPassDto> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _held;
        private int _passes;

        public int Passes => Volatile.Read(ref _passes);

        /// <summary>Holds every pass until <see cref="Answer"/>.</summary>
        public void Hold() => _held = true;

        public void Answer(AddressLookupPassDto pass) => _answer.TrySetResult(pass);

        /// <summary>Every pass throws, as a store that cannot be read would.</summary>
        public void Fail() => _answer.TrySetException(new IOException("The events store could not be read."));

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IEventsPane.LookUpPendingAsync))
            {
                throw new InvalidOperationException($"The Events settings page should not reach {targetMethod?.Name}.");
            }

            Interlocked.Increment(ref _passes);
            if (!_held && !_answer.Task.IsCompleted) _answer.TrySetResult(new AddressLookupPassDto(0, 0, 0));
            return _answer.Task;
        }
    }
}
