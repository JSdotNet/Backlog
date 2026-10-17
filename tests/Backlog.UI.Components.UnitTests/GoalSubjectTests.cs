using Backlog.UI.Components.Goals;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// <see cref="GoalSubject"/>: the work a goal control is for, named the way a host can
/// hand it to the goal port — a pull request read back out of the key a work card
/// carries, and each kind named in the picker's sentence.
/// </summary>
public sealed class GoalSubjectTests
{
    [Theory]
    [InlineData("JSdotNet/Backlog#655", 655, "JSdotNet/Backlog")]
    [InlineData(" owner/name #12", 12, "owner/name")]
    public void A_pull_request_key_names_its_repository(string key, int number, string repository)
    {
        var subject = GoalSubject.FromPullRequestKey(key, number);

        Assert.Equal(GoalSubject.ForPullRequest(repository, number), subject);
        Assert.Equal($"pull request #{number}", subject!.Noun);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("655")]
    [InlineData("#655")]
    [InlineData("backlog#655")]
    [InlineData("owner/name/extra#655")]
    [InlineData("owner/#655")]
    public void A_key_of_another_shape_names_no_pull_request(string? key)
    {
        Assert.Null(GoalSubject.FromPullRequestKey(key, 655));
    }

    [Fact]
    public void Each_kind_of_work_is_named_for_the_picker()
    {
        Assert.Equal("task", GoalSubject.ForTask(Guid.NewGuid()).Noun);
        Assert.Equal("plan +release-q4", GoalSubject.ForPlan("release-q4").Noun);
        Assert.Equal("plan +release-q4", GoalSubject.ForPlan("+release-q4").Noun);
    }
}
