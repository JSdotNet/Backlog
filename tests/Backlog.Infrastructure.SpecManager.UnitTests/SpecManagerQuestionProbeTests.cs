using Backlog.Infrastructure.SpecManager.Api;
using Backlog.Infrastructure.SpecManager.Questions;
using Backlog.Modules.Notifications.Abstractions;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// The startup probe for a question that is no linked task's: live while spec-manager still
/// lists it open; no answer at all while nobody is signed in.
/// </summary>
public sealed class SpecManagerQuestionProbeTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_question_spec_manager_still_lists_open_lives()
    {
        using var scenario = new QuestionScenario();
        var probe = Probe(scenario);

        Assert.Equal("spec-manager", probe.Context);
        Assert.True(await probe.IsLiveAsync(Subject(QuestionScenario.Unassigned), Token));
    }

    [Fact]
    public async Task A_question_answered_or_deleted_meanwhile_has_ended()
    {
        using var scenario = new QuestionScenario();

        Assert.False(await Probe(scenario).IsLiveAsync(Subject(QuestionScenario.AnsweredByKees), Token));
    }

    [Fact]
    public async Task One_pass_reads_each_products_open_questions_once()
    {
        using var scenario = new QuestionScenario();
        var probe = Probe(scenario);

        await probe.IsLiveAsync(Subject(QuestionScenario.Unassigned), Token);
        await probe.IsLiveAsync(Subject(QuestionScenario.AssignedToMe), Token);

        Assert.Single(scenario.Connector.Server.To(QuestionScenario.QuestionsPath));
    }

    [Fact]
    public async Task Nobody_signed_in_is_no_answer()
    {
        using var scenario = new QuestionScenario();
        scenario.Connector.Store.Remove(scenario.Root);

        await Assert.ThrowsAsync<SpecManagerSignInRequiredException>(() => Probe(scenario).IsLiveAsync(Subject(QuestionScenario.Unassigned), Token));
    }

    [Fact]
    public async Task A_subject_of_another_type_is_nothing_it_can_say_ended()
    {
        using var scenario = new QuestionScenario();

        Assert.True(await Probe(scenario).IsLiveAsync(new NotificationSubject("spec-manager", "item", "x"), Token));
        Assert.Empty(scenario.Connector.Server.To(QuestionScenario.QuestionsPath));
    }

    private static SpecManagerQuestionProbe Probe(QuestionScenario scenario) =>
        new(scenario.Connector.Client, scenario.Connector.SignIn, scenario.Connector.Targets, scenario.Connector.Time);

    private static NotificationSubject Subject(string questionId) => new("spec-manager", "question", questionId);
}
