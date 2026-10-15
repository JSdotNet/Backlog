using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// What a host with no My Day preferences store reads: My Day clears overnight, as
/// it always has (local ADR 0032, §1 "The default"), and nothing ever changes it.
/// </summary>
public sealed class DefaultMyDayPreferencesTests
{
    [Fact]
    public void My_day_clears_overnight()
    {
        Assert.True(DefaultMyDayPreferences.Instance.ClearsOvernight);
    }

    [Fact]
    public void Turning_the_switch_off_is_refused_with_a_reason_and_changes_nothing()
    {
        IMyDayPreferences preferences = DefaultMyDayPreferences.Instance;
        var raised = 0;
        preferences.Changed += () => raised++;

        var message = preferences.SetClearsOvernight(false);

        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.True(preferences.ClearsOvernight);
        Assert.Equal(0, raised);
    }
}
