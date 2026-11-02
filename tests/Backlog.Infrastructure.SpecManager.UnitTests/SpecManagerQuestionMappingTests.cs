using System.Text;

using Backlog.Infrastructure.SpecManager.Api;
using Backlog.Infrastructure.SpecManager.Questions;
using Backlog.Modules.Notifications.Abstractions;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// One spec-manager question as the notification that asks it in Backlog: its id, what it
/// says, what it accepts, where it came from and what it is about.
/// </summary>
public sealed class SpecManagerQuestionMappingTests
{
    private const string Root = "https://spec.test";
    private const string Product = "billing";

    private static readonly BacklogQuestionDto OnItem = new(
        "40000000-0000-0000-0000-000000000001",
        "20000000-0000-0000-0000-000000000007",
        7,
        "Rounding in invoices",
        "Which rounding rule applies to invoice totals?",
        "Totals are summed per line and **rounded once**.",
        [
            new BacklogQuestionOptionDto(2, "Round half up", null),
            new BacklogQuestionOptionDto(1, "Round half even", "Banker's rounding."),
        ],
        new BacklogQuestionPersonDto("me", "Jip Jansen"),
        new BacklogQuestionPersonDto("ada", "Ada Lovelace"),
        new DateTimeOffset(2026, 10, 5, 11, 0, 0, TimeSpan.Zero),
        "agent-session",
        null,
        null,
        CanAnswer: true);

    private static readonly BacklogQuestionDto OnChange = OnItem with
    {
        Id = "40000000-0000-0000-0000-000000000002",
        ItemId = null,
        ItemNumber = null,
        ItemTitle = null,
        OpenSpecChange = "add-export-flag",
        Assignee = null,
        Source = "spec-manager",
    };

    [Fact]
    public void The_id_is_a_version_5_uuid_of_the_installation_and_the_question()
    {
        var id = SpecManagerQuestionIds.For(Root, OnItem.Id);

        Assert.Equal(id, SpecManagerQuestionIds.For(Root + "/", OnItem.Id.ToUpperInvariant()));
        Assert.Equal(5, id.Version);
        Assert.NotEqual(id, SpecManagerQuestionIds.For("https://other.test", OnItem.Id));
        Assert.NotEqual(id, SpecManagerQuestionIds.For(Root, OnChange.Id));
        Assert.NotEqual(id, SpecManagerQuestionIds.RefusalFor(Root, OnItem.Id));
    }

    /// <summary>The scheme is pinned: a change would give every open question a second
    /// notification beside its first.</summary>
    [Fact]
    public void The_id_scheme_is_pinned()
    {
        // RFC 9562's own example: "www.example.com" in the DNS namespace.
        Assert.Equal(
            new Guid("2ed6657d-e927-568b-95e1-2665a8aea6a2"),
            SpecManagerQuestionIds.Version5(new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8"), "www.example.com"));

        Assert.Equal(
            SpecManagerQuestionIds.Version5(SpecManagerQuestionIds.Version5(new Guid("6ba7b811-9dad-11d1-80b4-00c04fd430c8"), Root), OnItem.Id),
            SpecManagerQuestionIds.For(Root, OnItem.Id));
    }

    [Fact]
    public void A_question_assigned_to_the_person_is_a_warning_with_its_choices_other_and_note()
    {
        var draft = SpecManagerQuestionMapper.ToDraft(OnItem, Product, Root, defaultInstallation: true, me: "me", linked: null);

        Assert.Equal(SpecManagerQuestionIds.For(Root, OnItem.Id), draft.Id);
        Assert.Equal(NotificationKind.Question, draft.Kind);
        Assert.Equal(NotificationSeverity.Warning, draft.Severity);
        Assert.Equal(new NotificationSource("spec-manager", $"{Product}/{OnItem.Id}"), draft.Source);
        Assert.Equal("Which rounding rule applies to invoice totals?", draft.Title);
        Assert.Equal("#7 Rounding in invoices", draft.Summary);

        var spec = draft.Answer!;
        Assert.Equal([new AnswerChoice("1", "Round half even"), new AnswerChoice("2", "Round half up")], spec.Choices);
        Assert.All(spec.Choices, choice => Assert.Equal(AnswerChoiceStyle.Default, choice.Style));
        Assert.Equal(4_000, spec.Open!.MaxLength);
        Assert.Equal(4_000, spec.Note!.MaxLength);
        Assert.False(spec.AllowMultiple);

        Assert.Equal($"{Root}/producten/{Product}/questions?question={OnItem.Id}", draft.DeepLink);
    }

    [Fact]
    public void The_body_holds_the_question_its_context_and_each_options_description()
    {
        var body = SpecManagerQuestionMapper.ToDraft(OnItem, Product, Root, true, "me", null).BodyMarkdown;

        Assert.Contains("**Which rounding rule applies to invoice totals?**", body, StringComparison.Ordinal);
        Assert.Contains("Totals are summed per line and **rounded once**.", body, StringComparison.Ordinal);
        Assert.Contains("1. **Round half even** — Banker's rounding.", body, StringComparison.Ordinal);
        Assert.Contains("2. **Round half up**", body, StringComparison.Ordinal);
        Assert.True(body.IndexOf("Round half even", StringComparison.Ordinal) < body.IndexOf("Round half up", StringComparison.Ordinal));
    }

    [Fact]
    public void A_long_question_is_cut_to_a_one_line_title_and_the_body_stays_within_its_bound()
    {
        var question = OnItem with { Text = new string('q', 120) + "\nsecond line", Context = new string('c', 40_000) };

        var draft = SpecManagerQuestionMapper.ToDraft(question, Product, Root, true, "me", null);

        Assert.Equal(80, draft.Title.Length);
        Assert.EndsWith("…", draft.Title, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(draft.BodyMarkdown) <= 32 * 1024);
    }

    [Fact]
    public void An_unassigned_question_is_info_and_one_on_a_change_names_the_change()
    {
        var draft = SpecManagerQuestionMapper.ToDraft(OnChange, Product, Root, true, "me", null);

        Assert.Equal(NotificationSeverity.Info, draft.Severity);
        Assert.Equal("add-export-flag", draft.Summary);
        Assert.Equal($"{Root}/producten/{Product}?openspec=add-export-flag", draft.DeepLink);
        Assert.Equal(new NotificationSubject("spec-manager", "question", OnChange.Id), draft.Subject);
        Assert.Equal($"{Product} · add-export-flag", draft.Origin!.Place);
        Assert.Equal("typed in spec-manager", draft.Origin.Via);
    }

    [Fact]
    public void The_origin_names_the_system_the_place_the_asker_and_how_it_was_asked()
    {
        var origin = SpecManagerQuestionMapper.ToDraft(OnItem, Product, Root, defaultInstallation: true, "me", null).Origin!;

        Assert.Equal("spec-manager", origin.System);
        Assert.Equal("billing · #7 Rounding in invoices", origin.Place);
        Assert.Equal("Ada Lovelace", origin.AskedBy);
        Assert.Equal("Agent session", origin.Via);
        Assert.Equal($"{Root}/producten/{Product}/questions?question={OnItem.Id}", origin.OpenLink);
    }

    [Fact]
    public void Another_installation_is_named_by_its_host()
    {
        var origin = SpecManagerQuestionMapper.ToDraft(OnItem, Product, "https://localhost:7443", defaultInstallation: false, "me", null).Origin!;

        Assert.Equal("spec-manager (localhost:7443)", origin.System);
    }

    [Fact]
    public void A_question_on_a_linked_task_is_about_that_task_and_names_its_one_session()
    {
        var task = Guid.Parse("50000000-0000-0000-0000-000000000001");

        var draft = SpecManagerQuestionMapper.ToDraft(OnItem, Product, Root, true, "me", new SpecManagerLinkedWork(task, "Rounding fix"));

        Assert.Equal(new NotificationSubject("tasks", "task", task.ToString("D")), draft.Subject);
        Assert.Equal("Agent session · Rounding fix", draft.Origin!.Via);
    }

    [Fact]
    public void A_linked_task_without_one_session_says_agent_session_alone()
    {
        var draft = SpecManagerQuestionMapper.ToDraft(OnItem, Product, Root, true, "me", new SpecManagerLinkedWork(Guid.NewGuid(), null));

        Assert.Equal("Agent session", draft.Origin!.Via);
    }

    [Theory]
    [InlineData("me", false, true)]
    [InlineData("me", true, true)]
    [InlineData("someone-else", false, false)]
    [InlineData(null, false, false)]
    public void Which_assigned_questions_concern_the_person(string? me, bool onlyMine, bool concerns) =>
        Assert.Equal(concerns, SpecManagerQuestionMapper.Concerns(OnItem, me, onlyMine));

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void Which_unassigned_questions_concern_the_person(bool canAnswer, bool onlyMine, bool concerns) =>
        Assert.Equal(concerns, SpecManagerQuestionMapper.Concerns(OnChange with { CanAnswer = canAnswer }, "me", onlyMine));

    [Fact]
    public void An_answer_in_spec_manager_closes_with_who_and_what()
    {
        var answer = new BacklogAnswerDto(null, null, "Neither, see the note", null, new BacklogQuestionPersonDto("kees", "Kees de Vries"), DateTimeOffset.UnixEpoch);

        Assert.Equal("Answered in spec-manager by Kees de Vries: Neither, see the note", SpecManagerQuestionMapper.AnsweredReason(answer));
        Assert.Equal("Answered in spec-manager by Kees de Vries: No", SpecManagerQuestionMapper.AnsweredReason(answer with { Option = 2, OptionLabel = "No", Other = null }));
    }

    [Theory]
    [InlineData("billing/40000000-0000-0000-0000-000000000001", "billing", "40000000-0000-0000-0000-000000000001")]
    [InlineData("a/b/c", "a/b", "c")]
    public void A_source_key_names_its_product_and_question(string key, string product, string question) =>
        Assert.Equal((product, question), SpecManagerQuestionMapper.ParseSourceKey(key));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-slash")]
    [InlineData("/q")]
    [InlineData("p/")]
    public void A_source_key_without_both_parts_names_nothing(string? key) =>
        Assert.Null(SpecManagerQuestionMapper.ParseSourceKey(key));
}
