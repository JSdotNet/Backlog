namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Each folder's vocabulary is fixed by its own instructions file, and the tone
/// is the one axis all five share. Both are pinned here: a folder quietly losing
/// a value stops a real status being recognised, and a tone drifting makes two
/// folders disagree about what a colour means.
/// </summary>
public sealed class DevbookStatusTests
{
    [Theory]
    [InlineData(DevbookFolder.Arc42, "draft,proposed,active,deprecated")]
    [InlineData(DevbookFolder.Domain, "draft,proposed,active,deprecated")]
    [InlineData(DevbookFolder.Design, "draft,active,deprecated")]
    [InlineData(DevbookFolder.Backlog, "draft,ready,in-progress,done,blocked")]
    [InlineData(DevbookFolder.Tech, "candidate,trial,adopted,hold,retired")]
    public void Each_folder_carries_exactly_the_vocabulary_its_instructions_define(DevbookFolder folder, string expected)
    {
        Assert.Equal(expected.Split(','), DevbookStatus.Values(folder));
    }

    [Fact]
    public void An_unknown_folder_has_no_vocabulary_to_check_against()
    {
        Assert.Empty(DevbookStatus.Values(DevbookFolder.Unknown));
        Assert.False(DevbookStatus.IsKnown(DevbookFolder.Unknown, "active"));
    }

    [Theory]
    [InlineData(DevbookFolder.Arc42, "draft", DevbookStatusTone.Provisional)]
    [InlineData(DevbookFolder.Arc42, "proposed", DevbookStatusTone.Planned)]
    [InlineData(DevbookFolder.Arc42, "active", DevbookStatusTone.Active)]
    [InlineData(DevbookFolder.Arc42, "deprecated", DevbookStatusTone.Retired)]
    [InlineData(DevbookFolder.Domain, "draft", DevbookStatusTone.Provisional)]
    [InlineData(DevbookFolder.Domain, "proposed", DevbookStatusTone.Planned)]
    [InlineData(DevbookFolder.Domain, "active", DevbookStatusTone.Active)]
    [InlineData(DevbookFolder.Domain, "deprecated", DevbookStatusTone.Retired)]
    [InlineData(DevbookFolder.Design, "draft", DevbookStatusTone.Provisional)]
    [InlineData(DevbookFolder.Design, "active", DevbookStatusTone.Active)]
    [InlineData(DevbookFolder.Design, "deprecated", DevbookStatusTone.Retired)]
    [InlineData(DevbookFolder.Backlog, "draft", DevbookStatusTone.Provisional)]
    [InlineData(DevbookFolder.Backlog, "ready", DevbookStatusTone.Planned)]
    [InlineData(DevbookFolder.Backlog, "in-progress", DevbookStatusTone.Active)]
    [InlineData(DevbookFolder.Backlog, "done", DevbookStatusTone.Complete)]
    [InlineData(DevbookFolder.Backlog, "blocked", DevbookStatusTone.Attention)]
    [InlineData(DevbookFolder.Tech, "candidate", DevbookStatusTone.Planned)]
    [InlineData(DevbookFolder.Tech, "trial", DevbookStatusTone.Provisional)]
    [InlineData(DevbookFolder.Tech, "adopted", DevbookStatusTone.Active)]
    [InlineData(DevbookFolder.Tech, "hold", DevbookStatusTone.Attention)]
    [InlineData(DevbookFolder.Tech, "retired", DevbookStatusTone.Retired)]
    public void Every_value_of_every_vocabulary_maps_onto_the_shared_scale(
        DevbookFolder folder,
        string status,
        DevbookStatusTone expected)
    {
        Assert.True(DevbookStatus.IsKnown(folder, status));
        Assert.Equal(expected, DevbookStatus.Tone(folder, status));
    }

    [Fact]
    public void A_value_another_folder_uses_is_still_a_typo_here()
    {
        // `done` is a backlog status; architecture describes a standing decision
        // and has no such state. Reading it as one would hide the mistake.
        Assert.False(DevbookStatus.IsKnown(DevbookFolder.Arc42, "done"));
        Assert.Equal(DevbookStatusTone.Unknown, DevbookStatus.Tone(DevbookFolder.Arc42, "done"));

        Assert.False(DevbookStatus.IsKnown(DevbookFolder.Design, "proposed"));
        Assert.Equal(DevbookStatusTone.Unknown, DevbookStatus.Tone(DevbookFolder.Design, "proposed"));
    }

    [Fact]
    public void Without_a_folder_no_status_gets_a_tone()
    {
        // The same word means different things in different folders, so a tone
        // guessed without one would be wrong about as often as right.
        Assert.Equal(DevbookStatusTone.Unknown, DevbookStatus.Tone(DevbookFolder.Unknown, "active"));
        Assert.Equal(DevbookStatusTone.Unknown, DevbookStatus.Tone(DevbookFolder.Unknown, "done"));
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("  active  ")]
    [InlineData("ACTIVE")]
    public void A_stray_capital_or_a_stray_space_is_not_a_different_status(string status)
    {
        Assert.True(DevbookStatus.IsKnown(DevbookFolder.Domain, status));
        Assert.Equal(DevbookStatusTone.Active, DevbookStatus.Tone(DevbookFolder.Domain, status));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("shipped")]
    public void A_status_nobody_recognises_carries_no_tone(string? status)
    {
        Assert.False(DevbookStatus.IsKnown(DevbookFolder.Tech, status));
        Assert.Equal(DevbookStatusTone.Unknown, DevbookStatus.Tone(DevbookFolder.Tech, status));
    }
}
