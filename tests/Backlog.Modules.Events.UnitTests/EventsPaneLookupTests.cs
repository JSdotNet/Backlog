using Backlog.Modules.Events.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// The pane geocodes an address once, when it is saved (local ADR 0061 §5,
/// <c>.devbook/domain/events/requirements.md#requirement-an-address-is-geocoded-once-when-it-is-saved</c>):
/// a new or changed address is sent and its answer kept on the event; an unchanged one is not;
/// no answer leaves it Not looked up yet, which the next save of its place or Look up now sends
/// again; and the key's first save sends each address not looked up yet, once. A save answers
/// once it is saved; the call runs after it, one at a time per event, and the pane says it
/// changed once the answer is kept.
/// </summary>
public sealed class EventsPaneLookupTests : EventsPaneFixture
{
    private static readonly AddressDto Antwerpen = new("Groenendaallaan 394", "2030", "Antwerpen", "Belgium");

    private static readonly AddressDto Brussel = new("Groenendaallaan 394", "2030", "Brussel", "Belgium");

    private static readonly MapPointDto AntwerpenPoint = new(51.2299, 4.4214);

    private static readonly MapPointDto BrusselPoint = new(50.8467, 4.3525);

    public EventsPaneLookupTests()
    {
        Geocoder.Places("Antwerpen", AntwerpenPoint.Latitude, AntwerpenPoint.Longitude);
        Geocoder.Places("Brussel", BrusselPoint.Latitude, BrusselPoint.Longitude);
        Geocoder.FindsNothingIn("Nergenshuizen");
    }

    // --- On save --------------------------------------------------------------------------

    [Fact]
    public async Task A_new_address_is_sent_once_and_its_point_kept()
    {
        var id = await Add("Techorama Belgium");

        var read = await PlacedAsync(id, AtAddress(Antwerpen));

        Assert.Equal("Antwerpen", Assert.Single(Geocoder.Sent).Town);
        Assert.Equal(new PlaceDto("Kinepolis", false, Antwerpen, AddressLookup.Placed, AntwerpenPoint), read.Place);
        Assert.Equal(read.Place, Read(await Pane.GetAsync(id, Token)).Place);
    }

    [Fact]
    public async Task A_changed_address_is_sent_and_moves_the_point()
    {
        var id = await Add("Techorama Belgium");
        await PlacedAsync(id, AtAddress(Antwerpen));

        var read = await PlacedAsync(id, AtAddress(Brussel));

        Assert.Equal(["Antwerpen", "Brussel"], Geocoder.Sent.Select(sent => sent.Town));
        Assert.Equal((AddressLookup.Placed, BrusselPoint), (read.Place!.Lookup, read.Place.Point));
    }

    [Fact]
    public async Task An_unchanged_address_is_not_sent_again_and_keeps_its_point()
    {
        var id = await Add("Techorama Belgium");
        await PlacedAsync(id, AtAddress(Antwerpen));

        await Pane.RenameAsync(id, "Techorama Belgium 2027", Token);
        var read = await PlacedAsync(id, AtAddress(Antwerpen, venue: "Kinepolis Antwerpen"));

        Assert.Single(Geocoder.Sent);
        Assert.Equal(("Kinepolis Antwerpen", AddressLookup.Placed, AntwerpenPoint), (read.Place!.Venue, read.Place.Lookup, read.Place.Point));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("Jaarbeurs", false)]
    public async Task A_place_with_no_address_sends_nothing(string? venue, bool online)
    {
        var id = await Add("dotNET Friday");

        var read = await PlacedAsync(id, new PlaceDto(venue, online));

        Assert.Empty(Geocoder.Sent);
        Assert.Equal(new PlaceDto(venue, online), read.Place);
    }

    [Fact]
    public async Task An_address_saved_with_no_answer_back_is_kept_not_looked_up_yet()
    {
        var id = await Add("Techorama Belgium");
        var offline = new AddressDto(null, null, "Gent", "Belgium");

        var read = await PlacedAsync(id, AtAddress(offline));

        Assert.Single(Geocoder.Sent);
        Assert.Equal(new PlaceDto("Kinepolis", false, offline, AddressLookup.NotLookedUpYet, null), read.Place);
    }

    [Fact]
    public async Task A_geocoder_that_throws_still_saves_the_address_not_looked_up_yet()
    {
        var id = await Add("Techorama Belgium");
        Geocoder.Throws = new HttpRequestException("The geocoder could not be reached.");

        var read = await PlacedAsync(id, AtAddress(Antwerpen));

        Assert.Equal((Antwerpen, AddressLookup.NotLookedUpYet, (MapPointDto?)null), (read.Place!.Address, read.Place.Lookup, read.Place.Point));
    }

    [Fact]
    public async Task An_address_the_geocoder_cannot_place_is_not_found()
    {
        var id = await Add("Techorama Belgium");

        var read = await PlacedAsync(id, AtAddress(new AddressDto(null, null, "Nergenshuizen", null)));

        Assert.Equal((AddressLookup.NotFound, (MapPointDto?)null), (read.Place!.Lookup, read.Place.Point));
    }

    [Fact]
    public async Task The_next_save_of_a_place_not_looked_up_yet_sends_it_again()
    {
        var id = await Add("Techorama Belgium");
        var gent = new AddressDto(null, null, "Gent", "Belgium");
        await PlacedAsync(id, AtAddress(gent));
        Geocoder.Places("Gent", 51.0543, 3.7174);

        var read = await PlacedAsync(id, AtAddress(gent));

        Assert.Equal(2, Geocoder.Sent.Count);
        Assert.Equal((AddressLookup.Placed, new MapPointDto(51.0543, 3.7174)), (read.Place!.Lookup, read.Place.Point));
    }

    [Fact]
    public async Task An_unchanged_address_not_found_is_not_sent_again_on_save()
    {
        var id = await Add("Techorama Belgium");
        var nowhere = new AddressDto(null, null, "Nergenshuizen", null);
        await PlacedAsync(id, AtAddress(nowhere));

        var read = await PlacedAsync(id, AtAddress(nowhere, venue: "Renamed"));

        Assert.Single(Geocoder.Sent);
        Assert.Equal(AddressLookup.NotFound, read.Place!.Lookup);
    }

    [Fact]
    public async Task An_event_added_with_an_address_is_sent_once_and_placed()
    {
        var saved = Read(await Pane.AddAsync(new NewKnowledgeEventDto("Techorama Belgium", Place: AtAddress(Antwerpen)), Token));
        await LookUpsSettledAsync();
        var added = Read(await Pane.GetAsync(saved.Id, Token));

        Assert.Single(Geocoder.Sent);
        Assert.Equal((AddressLookup.Placed, AntwerpenPoint), (added.Place!.Lookup, added.Place.Point));
        Assert.Equal(added.Place, Read(await Pane.GetAsync(added.Id, Token)).Place);
    }

    [Fact]
    public async Task An_address_with_no_part_is_refused_and_nothing_is_sent()
    {
        var id = await Add("Techorama Belgium");

        var refused = await Pane.SetPlaceAsync(id, AtAddress(new AddressDto(" ", null, null, "")), Token);

        Assert.Equal("address.empty", Assert.IsType<Error>(refused.Value).Code);
        Assert.Empty(Geocoder.Sent);
    }

    [Fact]
    public async Task The_lookup_and_point_a_caller_sends_are_not_taken()
    {
        var id = await Add("Techorama Belgium");
        var gent = new AddressDto(null, null, "Gent", "Belgium");

        var read = await PlacedAsync(id, new PlaceDto("Kinepolis", false, gent, AddressLookup.Placed, new MapPointDto(1, 1)));

        Assert.Equal((AddressLookup.NotLookedUpYet, (MapPointDto?)null), (read.Place!.Lookup, read.Place.Point));
    }

    // --- While the call runs --------------------------------------------------------------

    [Fact]
    public async Task A_change_saved_elsewhere_while_the_call_runs_is_kept()
    {
        var id = await Add("Techorama Belgium");
        Geocoder.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Read(await Pane.SetPlaceAsync(id, AtAddress(Antwerpen), Token));
        await Elsewhere().RenameAsync(id, "Techorama Belgium 2027", Token);
        Geocoder.Gate.SetResult();
        await LookUpsSettledAsync();
        var read = Read(await Pane.GetAsync(id, Token));

        Assert.Equal(("Techorama Belgium 2027", AddressLookup.Placed), (read.Title, read.Place!.Lookup));
    }

    [Fact]
    public async Task An_answer_for_an_address_changed_while_the_call_ran_is_not_kept()
    {
        var id = await Add("Techorama Belgium");
        Geocoder.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gent = new AddressDto(null, null, "Gent", "Belgium");

        Read(await Pane.SetPlaceAsync(id, AtAddress(Antwerpen), Token));
        Read(await Elsewhere().SetPlaceAsync(id, AtAddress(gent), Token));
        Geocoder.Gate.SetResult();
        await LookUpsSettledAsync();

        var read = Read(await Pane.GetAsync(id, Token));
        Assert.Equal((gent, AddressLookup.NotLookedUpYet, (MapPointDto?)null), (read.Place!.Address, read.Place.Lookup, read.Place.Point));
    }

    /// <summary>The pane's mark ends with the add's save, not with the geocoder call after it:
    /// work that save started — the sync loop's settle timer — and that announces a change while
    /// the call is still out is heard as made elsewhere, as after <c>SetPlaceAsync</c>. The
    /// lookup's own answer is then heard once more.</summary>
    [Fact]
    public async Task Work_started_by_an_add_is_heard_while_its_address_is_still_being_looked_up()
    {
        var heard = 0;
        Pane.Changed += () => heard++;
        ExecutionContext? started = null;
        Announced.Changed += () => started ??= ExecutionContext.Capture();
        Geocoder.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Read(await Pane.AddAsync(new NewKnowledgeEventDto("Techorama Belgium", Place: AtAddress(Antwerpen)), Token));
        ExecutionContext.Run(started!, _ => Announced.Raise(), null);

        Assert.Equal(1, heard);

        Geocoder.Gate.SetResult();
        await LookUpsSettledAsync();

        Assert.Equal(2, heard);
    }

    // --- The save answers first -----------------------------------------------------------

    [Fact]
    public async Task A_place_save_answers_once_it_is_saved_and_the_address_is_looked_up_after_it()
    {
        var id = await Add("Techorama Belgium");
        Geocoder.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var saved = Read(await Pane.SetPlaceAsync(id, AtAddress(Antwerpen), Token));

        Assert.Equal((Antwerpen, AddressLookup.NotLookedUpYet), (saved.Place!.Address, saved.Place.Lookup));
        Assert.True(Pane.IsLookingUp(id));
        Assert.Equal(AddressLookup.NotLookedUpYet, Read(await Pane.GetAsync(id, Token)).Place!.Lookup);

        Geocoder.Gate.SetResult();
        await LookUpsSettledAsync();

        Assert.False(Pane.IsLookingUp(id));
        var read = Read(await Pane.GetAsync(id, Token));
        Assert.Equal((AddressLookup.Placed, AntwerpenPoint), (read.Place!.Lookup, read.Place.Point));
    }

    [Fact]
    public async Task An_add_with_an_address_answers_once_it_is_saved_and_looks_it_up_after()
    {
        Geocoder.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var added = Read(await Pane.AddAsync(new NewKnowledgeEventDto("Techorama Belgium", Place: AtAddress(Antwerpen)), Token));

        Assert.Equal(AddressLookup.NotLookedUpYet, added.Place!.Lookup);
        Assert.True(Pane.IsLookingUp(added.Id));

        Geocoder.Gate.SetResult();
        await LookUpsSettledAsync();

        Assert.Equal(AddressLookup.Placed, Read(await Pane.GetAsync(added.Id, Token)).Place!.Lookup);
    }

    /// <summary>The answer's save is the pane's own, so it is not heard back as one made
    /// elsewhere; the pane says so itself once the lookup is over, whatever came back, so a
    /// window showing the lookup in progress reads the event again.</summary>
    [Theory]
    [InlineData("Antwerpen")]
    [InlineData("Gent")]
    public async Task The_pane_says_it_changed_once_its_lookup_is_over(string town)
    {
        var id = await Add("Techorama Belgium");
        var heard = 0;
        var lookingUpWhenHeard = true;
        Pane.Changed += () =>
        {
            heard++;
            lookingUpWhenHeard = Pane.IsLookingUp(id);
        };

        Read(await Pane.SetPlaceAsync(id, AtAddress(new AddressDto(null, null, town, null)), Token));
        await LookUpsSettledAsync();

        Assert.Equal(1, heard);
        Assert.False(lookingUpWhenHeard);
    }

    /// <summary>The second save comes once the first lookup has sent, so it has started and
    /// the second queues behind it rather than merging into it; held at the gate, the first
    /// keeps the second from sending.</summary>
    [Fact]
    public async Task Lookups_for_one_event_run_one_at_a_time_and_the_last_address_wins()
    {
        var id = await Add("Techorama Belgium");
        Geocoder.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Read(await Pane.SetPlaceAsync(id, AtAddress(Antwerpen), Token));
        await Geocoder.SentAsync(1).WaitAsync(Token);
        Read(await Pane.SetPlaceAsync(id, AtAddress(Brussel), Token));

        Assert.Equal(["Antwerpen"], Geocoder.Sent.Select(sent => sent.Town));
        Assert.True(Pane.IsLookingUp(id));

        Geocoder.Gate.SetResult();
        await LookUpsSettledAsync();

        Assert.Equal(["Antwerpen", "Brussel"], Geocoder.Sent.Select(sent => sent.Town));
        var read = Read(await Pane.GetAsync(id, Token));
        Assert.Equal((Brussel, AddressLookup.Placed, BrusselPoint), (read.Place!.Address, read.Place.Lookup, read.Place.Point));
    }

    /// <summary>
    /// A write of this pane that loaded the event before a lookup's answer is kept does not undo
    /// that answer: every load-change-save of the pane, the answer's record among them, runs one
    /// at a time per event. The rename is held between its load and its save; the answer then
    /// either waits for it (the fix) or lands first and is written over (the bug).
    /// </summary>
    [Fact]
    public async Task A_rename_that_loaded_before_the_answer_was_kept_does_not_undo_the_point()
    {
        var id = await Add("Techorama Belgium");
        Geocoder.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Read(await Pane.SetPlaceAsync(id, AtAddress(Antwerpen), Token));
        await Geocoder.SentAsync(1).WaitAsync(Token);

        var renameHeld = Store.PauseNextSave();
        var renaming = Pane.RenameAsync(id, "Techorama Belgium 2027", Token);
        var placedWritten = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Store.Written += written => { if (written.Place?.Lookup == AddressLookup.Placed) placedWritten.TrySetResult(); };
        Geocoder.Gate.SetResult();
        await Task.WhenAny(placedWritten.Task, Task.Delay(500, Token));
        renameHeld.SetResult();
        Read(await renaming);
        await LookUpsSettledAsync();

        var read = Read(await Pane.GetAsync(id, Token));
        Assert.Equal(("Techorama Belgium 2027", AddressLookup.Placed, AntwerpenPoint), (read.Title, read.Place!.Lookup, read.Place.Point));
    }

    /// <summary>Saves of a place still waiting to be looked up queue one lookup behind the one
    /// running, not one each: the queued one reads the event when its turn comes anyway.</summary>
    [Fact]
    public async Task Saves_while_a_lookup_runs_queue_one_more_lookup_not_one_each()
    {
        var id = await Add("Techorama Belgium");
        var offline = new AddressDto(null, null, "Gent", "Belgium");
        Geocoder.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Read(await Pane.SetPlaceAsync(id, AtAddress(offline), Token));
        await Geocoder.SentAsync(1).WaitAsync(Token);
        Read(await Pane.SetPlaceAsync(id, AtAddress(offline, venue: "Kinepolis Gent"), Token));
        Read(await Pane.SetPlaceAsync(id, AtAddress(offline, venue: "Kinepolis Gent 2"), Token));
        Read(await Pane.SetPlaceAsync(id, AtAddress(offline, venue: "Kinepolis Gent 3"), Token));
        Geocoder.Gate.SetResult();
        await LookUpsSettledAsync();

        Assert.Equal(2, Geocoder.Sent.Count);
        Assert.False(Pane.IsLookingUp(id));
    }

    [Fact]
    public async Task Look_up_now_queued_behind_a_queued_save_lookup_still_retries_an_address_not_found()
    {
        var id = await Add("Techorama Belgium");
        var nowhere = new AddressDto(null, null, "Nergenshuizen", null);
        await PlacedAsync(id, AtAddress(nowhere));
        var other = await Add("Cloudbrew");
        Geocoder.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Read(await Pane.SetPlaceAsync(id, AtAddress(new AddressDto(null, null, "Gent", null)), Token));
        await Geocoder.SentAsync(2).WaitAsync(Token);
        Read(await Pane.SetPlaceAsync(id, AtAddress(nowhere), Token));

        var retry = Pane.LookUpAddressAsync(id, Token);
        Geocoder.Gate.SetResult();
        var lookedUp = LookedUp(await retry);
        await LookUpsSettledAsync();

        Assert.Equal(["Nergenshuizen", "Gent", "Nergenshuizen"], Geocoder.Sent.Select(sent => sent.Town));
        Assert.Equal(AddressLookup.NotFound, lookedUp.Answer);
        Assert.False(Pane.IsLookingUp(other));
    }

    [Fact]
    public async Task A_save_whose_caller_gives_up_still_looks_the_address_up()
    {
        var id = await Add("Techorama Belgium");
        using var cancelled = new CancellationTokenSource();

        Read(await Pane.SetPlaceAsync(id, AtAddress(Antwerpen), cancelled.Token));
        await cancelled.CancelAsync();
        await LookUpsSettledAsync();

        Assert.Equal(AddressLookup.Placed, Read(await Pane.GetAsync(id, Token)).Place!.Lookup);
    }

    // --- Look up now ----------------------------------------------------------------------

    [Fact]
    public async Task Look_up_now_sends_an_address_not_looked_up_yet()
    {
        var id = await Add("Techorama Belgium");
        var gent = new AddressDto(null, null, "Gent", "Belgium");
        await PlacedAsync(id, AtAddress(gent));
        Geocoder.Places("Gent", 51.0543, 3.7174);

        var lookedUp = LookedUp(await Pane.LookUpAddressAsync(id, Token));

        Assert.Equal(2, Geocoder.Sent.Count);
        Assert.Equal((AddressLookup.Placed, AddressLookup.Placed), (lookedUp.Answer, lookedUp.Event.Place!.Lookup));
    }

    [Fact]
    public async Task Look_up_now_sends_an_address_not_found_again()
    {
        var id = await Add("Techorama Belgium");
        var nowhere = new AddressDto(null, null, "Nergenshuizen", null);
        await PlacedAsync(id, AtAddress(nowhere));
        Geocoder.Places("Nergenshuizen", 52.0, 5.0);

        var read = LookedUp(await Pane.LookUpAddressAsync(id, Token)).Event;

        Assert.Equal(2, Geocoder.Sent.Count);
        Assert.Equal((AddressLookup.Placed, new MapPointDto(52.0, 5.0)), (read.Place!.Lookup, read.Place.Point));
    }

    [Fact]
    public async Task Look_up_now_that_finds_nothing_again_says_the_address_was_not_found()
    {
        var id = await Add("Techorama Belgium");
        await PlacedAsync(id, AtAddress(new AddressDto(null, null, "Nergenshuizen", null)));

        var lookedUp = LookedUp(await Pane.LookUpAddressAsync(id, Token));

        Assert.Equal((AddressLookup.NotFound, AddressLookup.NotFound), (lookedUp.Answer, lookedUp.Event.Place!.Lookup));
    }

    // Offline, no key or a failed call on an address not found: the event stays not found (ADR
    // 0061 §5 has no edge back), and the answer says no answer came back, so the editor does not
    // read it as the geocoder refusing the address a second time.
    [Fact]
    public async Task Look_up_now_that_gets_no_answer_says_so_and_keeps_the_address_not_found()
    {
        var id = await Add("Techorama Belgium");
        await PlacedAsync(id, AtAddress(new AddressDto(null, null, "Nergenshuizen", null)));
        Geocoder.Throws = new HttpRequestException("No connection.");

        var lookedUp = LookedUp(await Pane.LookUpAddressAsync(id, Token));

        Assert.Equal(2, Geocoder.Sent.Count);
        Assert.Equal((AddressLookup.NotLookedUpYet, AddressLookup.NotFound), (lookedUp.Answer, lookedUp.Event.Place!.Lookup));
    }

    [Fact]
    public async Task Look_up_now_on_a_placed_address_sends_nothing()
    {
        var id = await Add("Techorama Belgium");
        await PlacedAsync(id, AtAddress(Antwerpen));

        var lookedUp = LookedUp(await Pane.LookUpAddressAsync(id, Token));

        Assert.Single(Geocoder.Sent);
        Assert.Null(lookedUp.Answer);
        Assert.Equal(AntwerpenPoint, lookedUp.Event.Place!.Point);
    }

    /// <summary>Look up now asked while a save's lookup is out queues behind it; that one
    /// places the address, so the retry sends nothing and says so (Answer null).</summary>
    [Fact]
    public async Task Look_up_now_waits_for_the_lookup_a_save_started_and_sends_nothing_twice()
    {
        var id = await Add("Techorama Belgium");
        Geocoder.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Read(await Pane.SetPlaceAsync(id, AtAddress(Antwerpen), Token));
        await Geocoder.SentAsync(1).WaitAsync(Token);

        var lookingUp = Pane.LookUpAddressAsync(id, Token);
        Geocoder.Gate.SetResult();
        var lookedUp = LookedUp(await lookingUp);

        Assert.Single(Geocoder.Sent);
        Assert.Null(lookedUp.Answer);
        Assert.Equal(AddressLookup.Placed, lookedUp.Event.Place!.Lookup);
    }

    /// <summary>Look up now asked before the save's lookup has started merges into it: one
    /// send, and the answer is that lookup's, since it sent. The save's lookup is held from
    /// starting by one queued before it for the same event.</summary>
    [Fact]
    public async Task Look_up_now_merged_into_a_lookup_not_started_answers_what_that_lookup_sent()
    {
        var id = await Add("Techorama Belgium");
        var offline = new AddressDto(null, null, "Gent", "Belgium");
        Geocoder.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Read(await Pane.SetPlaceAsync(id, AtAddress(offline), Token));
        await Geocoder.SentAsync(1).WaitAsync(Token);
        Read(await Pane.SetPlaceAsync(id, AtAddress(Antwerpen), Token));

        var lookingUp = Pane.LookUpAddressAsync(id, Token);
        Geocoder.Gate.SetResult();
        var lookedUp = LookedUp(await lookingUp);
        await LookUpsSettledAsync();

        Assert.Equal(["Gent", "Antwerpen"], Geocoder.Sent.Select(sent => sent.Town));
        Assert.Equal(AddressLookup.Placed, lookedUp.Answer);
        Assert.Equal(AddressLookup.Placed, lookedUp.Event.Place!.Lookup);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("Jaarbeurs", false)]
    public async Task Look_up_now_is_refused_for_a_place_with_no_address(string? venue, bool online)
    {
        var id = await Add("dotNET Friday");
        await PlacedAsync(id, new PlaceDto(venue, online));

        var refused = await Pane.LookUpAddressAsync(id, Token);

        Assert.Equal("address.missing", Assert.IsType<Error>(refused.Value).Code);
        Assert.Empty(Geocoder.Sent);
    }

    [Fact]
    public async Task Look_up_now_is_refused_for_an_event_with_no_place_and_an_unknown_one()
    {
        var id = await Add("dotNET Friday");

        Assert.Equal("address.missing", Assert.IsType<Error>((await Pane.LookUpAddressAsync(id, Token)).Value).Code);
        Assert.Equal("knowledge_event.not_found", Assert.IsType<Error>((await Pane.LookUpAddressAsync(Guid.NewGuid(), Token)).Value).Code);
    }

    // --- The key's first save -------------------------------------------------------------

    [Fact]
    public async Task The_pending_pass_sends_each_address_not_looked_up_yet_once()
    {
        var gent = new AddressDto(null, null, "Gent", "Belgium");
        var leuven = new AddressDto(null, null, "Leuven", "Belgium");
        var first = await Add("Techorama Belgium");
        var second = await Add("Cloudbrew");
        var third = await Add("DevDays");
        var placed = await Add("NDC");
        var notFound = await Add("Nowhere conf");
        var online = await Add("Build");
        await PlacedAsync(first, AtAddress(gent));
        await PlacedAsync(second, AtAddress(leuven));
        await PlacedAsync(third, AtAddress(new AddressDto(null, null, "Hasselt", null)));
        await PlacedAsync(placed, AtAddress(Antwerpen));
        await PlacedAsync(notFound, AtAddress(new AddressDto(null, null, "Nergenshuizen", null)));
        await PlacedAsync(online, new PlaceDto(null, true));
        Geocoder.ClearSent();
        Geocoder.Places("Gent", 51.0543, 3.7174);
        Geocoder.FindsNothingIn("Leuven");

        var pass = await Pane.LookUpPendingAsync(Token);

        Assert.Equal(["Gent", "Hasselt", "Leuven"], Geocoder.Sent.Select(sent => sent.Town).Order());
        Assert.Equal(new AddressLookupPassDto(Placed: 1, NotFound: 1, NotLookedUpYet: 1), pass);
        Assert.Equal(AddressLookup.Placed, Read(await Pane.GetAsync(first, Token)).Place!.Lookup);
        Assert.Equal(AddressLookup.NotFound, Read(await Pane.GetAsync(second, Token)).Place!.Lookup);
        Assert.Equal(AddressLookup.NotLookedUpYet, Read(await Pane.GetAsync(third, Token)).Place!.Lookup);
    }

    /// <summary>The key's first save shares the per-event queue: an address a save is already
    /// looking up is not sent a second time, and each event the pass is on reads as being
    /// looked up.</summary>
    [Fact]
    public async Task The_pending_pass_waits_for_a_lookup_a_save_started_and_sends_nothing_twice()
    {
        var id = await Add("Techorama Belgium");
        Geocoder.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Read(await Pane.SetPlaceAsync(id, AtAddress(Antwerpen), Token));
        await Geocoder.SentAsync(1).WaitAsync(Token);

        var passing = Pane.LookUpPendingAsync(Token);
        Geocoder.Gate.SetResult();
        var pass = await passing;
        await LookUpsSettledAsync();

        Assert.Single(Geocoder.Sent);
        Assert.Equal(new AddressLookupPassDto(Placed: 1, NotFound: 0, NotLookedUpYet: 0), pass);
        Assert.Equal(AddressLookup.Placed, Read(await Pane.GetAsync(id, Token)).Place!.Lookup);
    }

    [Fact]
    public async Task The_pending_pass_shows_each_event_it_is_on_as_being_looked_up()
    {
        var id = await Add("Techorama Belgium");
        await PlacedAsync(id, AtAddress(new AddressDto(null, null, "Gent", null)));
        Geocoder.Places("Gent", 51.0543, 3.7174);
        Geocoder.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var passing = Pane.LookUpPendingAsync(Token);
        await Geocoder.SentAsync(2).WaitAsync(Token);

        Assert.True(Pane.IsLookingUp(id));

        Geocoder.Gate.SetResult();
        Assert.Equal(new AddressLookupPassDto(1, 0, 0), await passing);
        Assert.False(Pane.IsLookingUp(id));
    }

    [Fact]
    public async Task The_pending_pass_with_nothing_pending_sends_nothing()
    {
        var id = await Add("Techorama Belgium");
        await PlacedAsync(id, AtAddress(Antwerpen));
        Geocoder.ClearSent();

        var pass = await Pane.LookUpPendingAsync(Token);

        Assert.Empty(Geocoder.Sent);
        Assert.Equal(new AddressLookupPassDto(0, 0, 0), pass);
    }

    /// <summary>Saves the place, waits for the lookup the save started, and reads the event.</summary>
    private async Task<KnowledgeEventDto> PlacedAsync(Guid id, PlaceDto place)
    {
        Read(await Pane.SetPlaceAsync(id, place, Token));
        await LookUpsSettledAsync();
        return Read(await Pane.GetAsync(id, Token));
    }

    private static PlaceDto AtAddress(AddressDto address, string venue = "Kinepolis") => new(venue, false, address);

    private static KnowledgeEventDto Read(Result<KnowledgeEventDto> result) => Assert.IsType<Ok<KnowledgeEventDto>>(result.Value).Value;

    private static AddressLookUpDto LookedUp(Result<AddressLookUpDto> result) => Assert.IsType<Ok<AddressLookUpDto>>(result.Value).Value;
}
