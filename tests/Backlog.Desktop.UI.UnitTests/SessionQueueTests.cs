using Backlog.Desktop.UI.Tasks;
using Backlog.Tests;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The session queue's words (<c>.devbook/domain/sessions/features.md#start-a-session-from-an-entry</c>):
/// the trigger's label, paused or running, and the commands that start the long-running
/// agents — each one the <c>/loop</c> line its own skill names, so a skill renamed without
/// its command fails here rather than handing the person a command that starts nothing.
/// </summary>
public sealed class SessionQueueTests
{
    [Theory]
    [InlineData(0, false, "Session queue, empty")]
    [InlineData(3, false, "Session queue, 3 waiting")]
    [InlineData(0, true, "Session queue, paused, empty")]
    [InlineData(3, true, "Session queue, paused, 3 waiting")]
    public void The_trigger_label_says_how_many_wait_and_whether_the_queue_is_paused(int count, bool paused, string expected)
    {
        Assert.Equal(expected, SessionQueue.TriggerLabel(count, paused));
    }

    [Fact]
    public void The_agents_are_the_dispatcher_the_brain_and_the_goals_in_that_order()
    {
        Assert.Equal(
            [
                "/loop /backlog-tools:dispatch-sessions",
                "/loop /backlog-tools:tend-brain",
                "/loop /backlog-tools:track-goals"
            ],
            LongRunningAgents.All.Select(agent => agent.Command));
    }

    [Fact]
    public void The_dispatcher_warning_names_the_dispatcher_agent_s_command()
    {
        Assert.Equal("/loop /backlog-tools:dispatch-sessions", LongRunningAgents.DispatchSessions);
        Assert.Equal(LongRunningAgents.DispatchSessions, LongRunningAgents.All[0].Command);
    }

    [Theory]
    [InlineData("dispatch-sessions", LongRunningAgents.DispatchSessions)]
    [InlineData("tend-brain", LongRunningAgents.TendBrain)]
    [InlineData("track-goals", LongRunningAgents.TrackGoals)]
    public void Each_command_is_the_loop_line_its_skill_names(string skill, string command)
    {
        var text = File.ReadAllText(RepositoryRoot.File("plugins", "backlog-tools", "skills", skill, "SKILL.md"));
        var name = System.Text.RegularExpressions.Regex.Match(text, @"^name:\s*(\S+)\s*$", System.Text.RegularExpressions.RegexOptions.Multiline).Groups[1].Value;

        Assert.Equal(skill, name);
        Assert.Contains(command + " ", text, StringComparison.Ordinal);
        Assert.Equal($"/loop /backlog-tools:{name}", command);
    }

    [Fact]
    public void Every_agent_has_a_name_and_a_line_of_what_it_does()
    {
        Assert.All(LongRunningAgents.All, agent =>
        {
            Assert.False(string.IsNullOrWhiteSpace(agent.Name));
            Assert.False(string.IsNullOrWhiteSpace(agent.Purpose));
        });
    }
}
