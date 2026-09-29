using Backlog.Modules.Tasks.Abstractions;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// The tokens a task is stored and travels as, pinned member by member. They are
/// persisted values: the SQLite store holds them and the sync wire carries them,
/// so a token that moved would be a row this build writes and an older one
/// cannot read. Every member is listed by hand rather than enumerated, so a
/// member added without a token fails here instead of on somebody's load.
/// </summary>
public sealed class EnumMapTests
{
    public static TheoryData<EntryType, string> Types => new()
    {
        { EntryType.Prompt, "prompt" },
        { EntryType.Task, "task" },
        { EntryType.Idea, "idea" },
        { EntryType.Test, "test" }
    };

    public static TheoryData<EntryStatus, string> Statuses => new()
    {
        { EntryStatus.Draft, "draft" },
        { EntryStatus.Ready, "ready" },
        { EntryStatus.InProgress, "in_progress" },
        { EntryStatus.Done, "done" },
        { EntryStatus.Archived, "archived" }
    };

    public static TheoryData<Priority, string> Priorities => new()
    {
        { Priority.Low, "low" },
        { Priority.Medium, "medium" },
        { Priority.High, "high" },
        { Priority.Critical, "critical" }
    };

    public static TheoryData<SubItemStatus, string> SubItemStatuses => new()
    {
        { SubItemStatus.Pending, "pending" },
        { SubItemStatus.Done, "done" }
    };

    [Theory]
    [MemberData(nameof(Types))]
    public void A_type_round_trips_through_its_token(EntryType member, string token)
    {
        Assert.Equal(token, EnumMap.ToWire(member));
        Assert.Equal(member, EnumMap.ParseType(token));
    }

    [Theory]
    [MemberData(nameof(Statuses))]
    public void A_status_round_trips_through_its_token(EntryStatus member, string token)
    {
        Assert.Equal(token, EnumMap.ToWire(member));
        Assert.Equal(member, EnumMap.ParseStatus(token));
    }

    [Theory]
    [MemberData(nameof(Priorities))]
    public void A_priority_round_trips_through_its_token(Priority member, string token)
    {
        Assert.Equal(token, EnumMap.ToWire(member));
        Assert.Equal(member, EnumMap.ParsePriority(token));
    }

    [Theory]
    [MemberData(nameof(SubItemStatuses))]
    public void A_sub_item_status_round_trips_through_its_token(SubItemStatus member, string token)
    {
        Assert.Equal(token, EnumMap.ToWire(member));
        Assert.Equal(member, EnumMap.ParseSubItemStatus(token));
    }

    [Fact]
    public void Every_member_of_every_enum_has_a_token()
    {
        Assert.Equal(Enum.GetValues<EntryType>().Length, Types.Count);
        Assert.Equal(Enum.GetValues<EntryStatus>().Length, Statuses.Count);
        Assert.Equal(Enum.GetValues<Priority>().Length, Priorities.Count);
        Assert.Equal(Enum.GetValues<SubItemStatus>().Length, SubItemStatuses.Count);
    }

    /// <summary>The spellings a person or an older writer might use are read the
    /// same: case, surrounding space, and a hyphen or none where the token has an
    /// underscore.</summary>
    [Theory]
    [InlineData("in-progress")]
    [InlineData("InProgress")]
    [InlineData(" IN_PROGRESS ")]
    public void A_status_is_read_whatever_its_spelling(string token) =>
        Assert.Equal(EntryStatus.InProgress, EnumMap.ParseStatus(token));

    [Fact]
    public void An_unknown_token_throws_rather_than_being_guessed_at()
    {
        Assert.Equal("Unknown task type 'chore'.", Assert.Throws<FormatException>(() => EnumMap.ParseType("chore")).Message);
        Assert.Equal("Unknown task status 'in_progres'.", Assert.Throws<FormatException>(() => EnumMap.ParseStatus("in_progres")).Message);
        Assert.Equal("Unknown priority 'urgent'.", Assert.Throws<FormatException>(() => EnumMap.ParsePriority("urgent")).Message);
        Assert.Equal("Unknown sub-item status 'skipped'.", Assert.Throws<FormatException>(() => EnumMap.ParseSubItemStatus("skipped")).Message);
    }

    [Fact]
    public void A_member_outside_the_enum_cannot_be_written()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EnumMap.ToWire((EntryType)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnumMap.ToWire((EntryStatus)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnumMap.ToWire((Priority)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnumMap.ToWire((SubItemStatus)99));
    }
}
