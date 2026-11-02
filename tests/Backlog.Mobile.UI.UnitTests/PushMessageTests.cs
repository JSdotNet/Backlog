using Backlog.Mobile.UI.Notifications;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The data message the sync service sends a phone through Notification Hubs (local ADR 0039
/// §4), read back on the phone: an open notification is shown, any other state takes the entry
/// out of the tray, and a map that is not one of these messages is passed over.
/// </summary>
public sealed class PushMessageTests
{
    private static readonly Guid Id = Guid.Parse("01920000-0000-7000-8000-0000000000c1");

    private static Dictionary<string, string> Data(string state = "open") => new()
    {
        ["id"] = Id.ToString("D"),
        ["kind"] = "question",
        ["severity"] = "warning",
        ["state"] = state,
        ["title"] = "Run the migration against staging?",
        ["summary"] = "Backlog has a question for you"
    };

    [Fact]
    public void An_open_message_is_shown_with_what_the_service_sent()
    {
        var message = PushMessage.Parse(Data());

        Assert.NotNull(message);
        Assert.Equal(Id, message.Id);
        Assert.True(message.Shows);
        Assert.Equal(("question", "warning", "Run the migration against staging?", "Backlog has a question for you"),
            (message.Kind, message.Severity, message.Title, message.Summary));
    }

    [Fact]
    public void The_origin_is_read_when_the_service_sent_one()
    {
        var data = Data();
        data["origin"] = "spec-manager · Billing";

        Assert.Equal("spec-manager · Billing", PushMessage.Parse(data)!.Origin);
        Assert.Null(PushMessage.Parse(Data())!.Origin);
    }

    [Theory]
    [InlineData("answered")]
    [InlineData("acknowledged")]
    [InlineData("closed")]
    public void A_settled_message_takes_the_entry_out_of_the_tray(string state)
    {
        var message = PushMessage.Parse(Data(state));

        Assert.NotNull(message);
        Assert.False(message.Shows);
    }

    [Fact]
    public void A_message_from_an_owner_that_sent_no_preview_still_carries_id_and_state()
    {
        var message = PushMessage.Parse(new Dictionary<string, string> { ["id"] = Id.ToString("D"), ["state"] = "open" });

        Assert.NotNull(message);
        Assert.True(message.Shows);
        Assert.Null(message.Title);
        Assert.Null(message.Summary);
    }

    [Theory]
    [InlineData(null, "open")]
    [InlineData("not-a-guid", "open")]
    [InlineData("01920000-0000-7000-8000-0000000000c1", null)]
    [InlineData("01920000-0000-7000-8000-0000000000c1", "snoozed")]
    public void A_map_that_is_not_a_push_message_is_ignored(string? id, string? state)
    {
        var data = new Dictionary<string, string>();
        if (id is not null) data["id"] = id;
        if (state is not null) data["state"] = state;

        Assert.Null(PushMessage.Parse(data));
    }

    [Fact]
    public void No_map_at_all_is_ignored() => Assert.Null(PushMessage.Parse(null));
}
