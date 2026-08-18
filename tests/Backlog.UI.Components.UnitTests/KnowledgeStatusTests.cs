namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Each folder's vocabulary is fixed by its own instructions file, and the tone
/// is the one axis all five share. Both are pinned here: a folder quietly losing
/// a value stops a real status being recognised, and a tone drifting makes two
/// folders disagree about what a colour means.
/// </summary>
public sealed class KnowledgeStatusTests
{
    [Theory]
    [InlineData(KnowledgeFolder.Arc42, "draft,proposed,active,deprecated")]
    [InlineData(KnowledgeFolder.Domain, "draft,proposed,active,deprecated")]
    [InlineData(KnowledgeFolder.Design, "draft,active,deprecated")]
    [InlineData(KnowledgeFolder.Backlog, "draft,ready,in-progress,done,blocked")]
    [InlineData(KnowledgeFolder.Tech, "candidate,trial,adopted,hold,retired")]
    public void Each_folder_carries_exactly_the_vocabulary_its_instructions_define(KnowledgeFolder folder, string expected)
    {
        Assert.Equal(expected.Split(','), KnowledgeStatus.Values(folder));
    }

    [Fact]
    public void An_unknown_folder_has_no_vocabulary_to_check_against()
    {
        Assert.Empty(KnowledgeStatus.Values(KnowledgeFolder.Unknown));
        Assert.False(KnowledgeStatus.IsKnown(KnowledgeFolder.Unknown, "active"));
    }

    [Theory]
    [InlineData(KnowledgeFolder.Arc42, "draft", KnowledgeStatusTone.Provisional)]
    [InlineData(KnowledgeFolder.Arc42, "proposed", KnowledgeStatusTone.Planned)]
    [InlineData(KnowledgeFolder.Arc42, "active", KnowledgeStatusTone.Active)]
    [InlineData(KnowledgeFolder.Arc42, "deprecated", KnowledgeStatusTone.Retired)]
    [InlineData(KnowledgeFolder.Domain, "draft", KnowledgeStatusTone.Provisional)]
    [InlineData(KnowledgeFolder.Domain, "proposed", KnowledgeStatusTone.Planned)]
    [InlineData(KnowledgeFolder.Domain, "active", KnowledgeStatusTone.Active)]
    [InlineData(KnowledgeFolder.Domain, "deprecated", KnowledgeStatusTone.Retired)]
    [InlineData(KnowledgeFolder.Design, "draft", KnowledgeStatusTone.Provisional)]
    [InlineData(KnowledgeFolder.Design, "active", KnowledgeStatusTone.Active)]
    [InlineData(KnowledgeFolder.Design, "deprecated", KnowledgeStatusTone.Retired)]
    [InlineData(KnowledgeFolder.Backlog, "draft", KnowledgeStatusTone.Provisional)]
    [InlineData(KnowledgeFolder.Backlog, "ready", KnowledgeStatusTone.Planned)]
    [InlineData(KnowledgeFolder.Backlog, "in-progress", KnowledgeStatusTone.Active)]
    [InlineData(KnowledgeFolder.Backlog, "done", KnowledgeStatusTone.Complete)]
    [InlineData(KnowledgeFolder.Backlog, "blocked", KnowledgeStatusTone.Attention)]
    [InlineData(KnowledgeFolder.Tech, "candidate", KnowledgeStatusTone.Planned)]
    [InlineData(KnowledgeFolder.Tech, "trial", KnowledgeStatusTone.Provisional)]
    [InlineData(KnowledgeFolder.Tech, "adopted", KnowledgeStatusTone.Active)]
    [InlineData(KnowledgeFolder.Tech, "hold", KnowledgeStatusTone.Attention)]
    [InlineData(KnowledgeFolder.Tech, "retired", KnowledgeStatusTone.Retired)]
    public void Every_value_of_every_vocabulary_maps_onto_the_shared_scale(
        KnowledgeFolder folder,
        string status,
        KnowledgeStatusTone expected)
    {
        Assert.True(KnowledgeStatus.IsKnown(folder, status));
        Assert.Equal(expected, KnowledgeStatus.Tone(folder, status));
    }

    [Fact]
    public void A_value_another_folder_uses_is_still_a_typo_here()
    {
        // `done` is a backlog status; architecture describes a standing decision
        // and has no such state. Reading it as one would hide the mistake.
        Assert.False(KnowledgeStatus.IsKnown(KnowledgeFolder.Arc42, "done"));
        Assert.Equal(KnowledgeStatusTone.Unknown, KnowledgeStatus.Tone(KnowledgeFolder.Arc42, "done"));

        Assert.False(KnowledgeStatus.IsKnown(KnowledgeFolder.Design, "proposed"));
        Assert.Equal(KnowledgeStatusTone.Unknown, KnowledgeStatus.Tone(KnowledgeFolder.Design, "proposed"));
    }

    [Fact]
    public void Without_a_folder_no_status_gets_a_tone()
    {
        // The same word means different things in different folders, so a tone
        // guessed without one would be wrong about as often as right.
        Assert.Equal(KnowledgeStatusTone.Unknown, KnowledgeStatus.Tone(KnowledgeFolder.Unknown, "active"));
        Assert.Equal(KnowledgeStatusTone.Unknown, KnowledgeStatus.Tone(KnowledgeFolder.Unknown, "done"));
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("  active  ")]
    [InlineData("ACTIVE")]
    public void A_stray_capital_or_a_stray_space_is_not_a_different_status(string status)
    {
        Assert.True(KnowledgeStatus.IsKnown(KnowledgeFolder.Domain, status));
        Assert.Equal(KnowledgeStatusTone.Active, KnowledgeStatus.Tone(KnowledgeFolder.Domain, status));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("shipped")]
    public void A_status_nobody_recognises_carries_no_tone(string? status)
    {
        Assert.False(KnowledgeStatus.IsKnown(KnowledgeFolder.Tech, status));
        Assert.Equal(KnowledgeStatusTone.Unknown, KnowledgeStatus.Tone(KnowledgeFolder.Tech, status));
    }
}
