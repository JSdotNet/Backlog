namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// How the Domain view's functional pages keep to the domain's own language
/// (local ADR 0044 §5–6): a summary is the first sentence without the list of
/// fields it may close on, an enum's values read as words, and the sentence
/// after them shows only when it names no field.
/// </summary>
public sealed class DomainUnitSummaryTests
{
    [Theory]
    [InlineData(
        "An immutable link to a downstream external artifact created from the task: `repo_id`, `external_id`, and `target_type`. More.",
        "An immutable link to a downstream external artifact created from the task.")]
    [InlineData(
        "An immutable record that an AI-assisted action contributed to the task: timestamp, ai_tool, activity_kind, and optional outcome_ref.",
        "An immutable record that an AI-assisted action contributed to the task.")]
    [InlineData(
        "An immutable audit record of a prompt copy/use: timestamp and action.",
        "An immutable audit record of a prompt copy/use: timestamp and action.")]
    [InlineData(
        "Creates and closes downstream artifacts for a multi-repo task, recording each as a `Projection Ref`. It is a service.",
        "Creates and closes downstream artifacts for a multi-repo task, recording each as a Projection Ref.")]
    public void A_summary_leaves_a_closing_list_of_fields_to_model(string prose, string summary) =>
        Assert.Equal(summary, DomainUnits.FunctionalSummary(prose));

    [Theory]
    [InlineData("Classification of the task: `prompt`, `task`. A follow-up is a `Dependency` on the task it comes after.", "A follow-up is a Dependency on the task it comes after.")]
    [InlineData("Where a task stands: `done`, `ready`, `blocked`. It is read from `depends_on`, and how it is derived is an invariant.", "")]
    [InlineData("Lifecycle state: `draft`, `ready`.", "")]
    public void An_enum_note_is_the_next_sentence_when_it_names_no_field(string prose, string note) =>
        Assert.Equal(note, DomainUnits.EnumNote(prose));

    [Theory]
    [InlineData("in_progress", "In progress")]
    [InlineData("draft", "Draft")]
    [InlineData("sub-item", "Sub item")]
    public void A_value_reads_as_words(string value, string words) =>
        Assert.Equal(words, DomainUnits.Humanize(value));

    [Theory]
    [InlineData("Task", "a task")]
    [InlineData("Inbox Item", "an inbox item")]
    [InlineData("AI Work Log", "an AI work log")]
    public void A_heading_reads_inside_a_sentence(string title, string phrase) =>
        Assert.Equal(phrase, DomainUnits.WithArticle(title));

    [Theory]
    [InlineData(".devbook/domain/tasks/flow.md#task-lifecycle", true)]
    [InlineData(".devbook/domain/tasks/flow.recurring.md", true)]
    [InlineData(".devbook/domain/tasks/domain.md#flowing", false)]
    public void A_flow_reference_names_a_flow_file(string reference, bool flow) =>
        Assert.Equal(flow, DomainUnits.IsFlowReference(reference));
}
