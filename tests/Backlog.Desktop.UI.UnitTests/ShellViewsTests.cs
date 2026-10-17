using Backlog.Desktop.UI.Shell;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The shell keeps its main view in <c>shell-navigation.json</c> by the member's
/// name, so a renamed member still has to read the name it was stored under. The
/// In progress view became Goals (local ADR 0035 §10): a file written before the
/// rename says <c>InProgress</c>, and it reopens on the same view.
/// </summary>
public sealed class ShellViewsTests
{
    [Fact]
    public void The_retired_in_progress_name_reads_as_goals()
    {
        Assert.Equal(ShellView.Goals, ShellViews.Read("InProgress", ShellView.Tasks));
    }

    [Theory]
    [InlineData("Tasks")]
    [InlineData("Board")]
    [InlineData("Calendar")]
    [InlineData("Roadmap")]
    [InlineData("Goals")]
    public void A_member_name_reads_as_that_member(string stored)
    {
        // The fallback is never the member named, so a name that fails to read shows.
        var fallback = stored == "Tasks" ? ShellView.Board : ShellView.Tasks;

        Assert.Equal(stored, ShellViews.Read(stored, fallback).ToString());
    }

    /// <summary>Only names count: a number, an unknown name, a different case of the
    /// retired one, or nothing at all falls back.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("4")]
    [InlineData("Now")]
    [InlineData("inprogress")]
    public void Anything_else_falls_back(string? stored)
    {
        Assert.Equal(ShellView.Board, ShellViews.Read(stored, ShellView.Board));
    }

    /// <summary>The new name is what the shell writes back, so the retired one leaves
    /// the file at the next view change.</summary>
    [Fact]
    public void Goals_is_stored_under_its_own_name()
    {
        Assert.Equal("Goals", ShellView.Goals.ToString());
    }
}
