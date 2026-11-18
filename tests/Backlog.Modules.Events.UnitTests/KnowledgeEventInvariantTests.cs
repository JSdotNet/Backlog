using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// One test or more per rule of <c>.devbook/domain/events/domain.invariants.md#knowledge-event</c>
/// that the aggregate itself enforces, named after the invariant it proves. The value objects'
/// rules are in <see cref="KnowledgeEventValueTests"/>.
/// </summary>
public sealed class KnowledgeEventInvariantTests
{
    // --- A knowledge event has a title --------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_knowledge_event_without_a_title_is_refused(string title)
    {
        var result = KnowledgeEvent.Create(Guid.NewGuid(), title);

        Assert.Equal("knowledge_event.title_missing", result.Error.Code);
    }

    [Fact]
    public void A_title_alone_makes_a_spotted_event_with_every_other_field_empty()
    {
        var id = Guid.NewGuid();

        var spotted = KnowledgeEvent.Create(id, "  dotNET Friday  ").Value;

        Assert.Equal(id, spotted.Id);
        Assert.Equal("dotNET Friday", spotted.Title);
        Assert.Equal(KnowledgeEventState.Spotted, spotted.State);
        Assert.Null(spotted.Series);
        Assert.Null(spotted.Start);
        Assert.Null(spotted.End);
        Assert.Null(spotted.DateSource);
        Assert.Null(spotted.Place);
        Assert.Null(spotted.Link);
        Assert.Null(spotted.SkipReason);
        Assert.Null(spotted.WhatToLearn);
        Assert.Empty(spotted.KeyDates);
        Assert.Null(spotted.Ticket);
        Assert.Null(spotted.Cost);
        Assert.Empty(spotted.Notes);
        Assert.Empty(spotted.Tasks);
        Assert.Null(spotted.Verdict);
    }

    [Fact]
    public void Renaming_to_a_blank_title_is_refused_and_keeps_the_title()
    {
        var spotted = Spotted();

        var result = spotted.Rename(" ");

        Assert.Equal("knowledge_event.title_missing", result.Error.Code);
        Assert.Equal("Techorama", spotted.Title);
    }

    [Fact]
    public void Renaming_trims_the_new_title()
    {
        var spotted = Spotted();

        Assert.True(spotted.Rename("  Techorama 2026 ").IsSuccess);
        Assert.Equal("Techorama 2026", spotted.Title);
    }

    // --- The end is not before the start ------------------------------------------------

    [Fact]
    public void An_end_before_the_start_is_refused()
    {
        var spotted = Spotted();

        var result = spotted.SetDates(Day(2026, 10, 28), Day(2026, 10, 26), DateSource.EventPage);

        Assert.Equal("knowledge_event.dates_out_of_order", result.Error.Code);
        Assert.Null(spotted.Start);
    }

    [Fact]
    public void An_end_on_the_start_day_is_accepted_whatever_the_times()
    {
        var spotted = Spotted();
        var afternoon = EventDate.Create(new DateOnly(2026, 11, 20), new TimeOnly(16, 30), TimeSpan.FromHours(1)).Value;
        var noon = EventDate.Create(new DateOnly(2026, 11, 20), new TimeOnly(12, 0), TimeSpan.FromHours(1)).Value;

        Assert.True(spotted.SetDates(afternoon, noon, DateSource.Typed).IsSuccess);
        Assert.Equal(noon, spotted.End);
    }

    [Fact]
    public void An_end_without_a_start_is_refused()
    {
        var result = Spotted().SetDates(start: null, Day(2026, 10, 28), DateSource.Typed);

        Assert.Equal("knowledge_event.dates_out_of_order", result.Error.Code);
    }

    [Fact]
    public void A_start_without_an_end_is_accepted_and_lasts_its_start_day()
    {
        var spotted = Spotted();

        Assert.True(spotted.SetDates(Day(2026, 10, 26), end: null, DateSource.EventPage).IsSuccess);
        Assert.Equal(Day(2026, 10, 26), spotted.Start);
        Assert.Null(spotted.End);
        Assert.Equal(DateSource.EventPage, spotted.DateSource);
    }

    // --- Dates name their source --------------------------------------------------------

    [Fact]
    public void A_start_without_a_date_source_is_refused()
    {
        var result = Spotted().SetDates(Day(2026, 10, 26), Day(2026, 10, 28), source: null);

        Assert.Equal("knowledge_event.date_source_mismatch", result.Error.Code);
    }

    [Fact]
    public void A_date_source_without_dates_is_refused()
    {
        var result = Spotted().SetDates(start: null, end: null, DateSource.Typed);

        Assert.Equal("knowledge_event.date_source_mismatch", result.Error.Code);
    }

    [Fact]
    public void No_dates_and_no_source_clears_the_dates()
    {
        var spotted = Spotted();
        spotted.SetDates(Day(2026, 10, 26), Day(2026, 10, 28), DateSource.EventPage);

        Assert.True(spotted.SetDates(start: null, end: null, source: null).IsSuccess);
        Assert.Null(spotted.Start);
        Assert.Null(spotted.End);
        Assert.Null(spotted.DateSource);
    }

    // --- The state moves only forward ---------------------------------------------------

    public static TheoryData<KnowledgeEventState, KnowledgeEventState> ForwardMoves() => new()
    {
        { KnowledgeEventState.Spotted, KnowledgeEventState.Considering },
        { KnowledgeEventState.Spotted, KnowledgeEventState.Going },
        { KnowledgeEventState.Spotted, KnowledgeEventState.Attended },
        { KnowledgeEventState.Spotted, KnowledgeEventState.Harvested },
        { KnowledgeEventState.Considering, KnowledgeEventState.Going },
        { KnowledgeEventState.Considering, KnowledgeEventState.Attended },
        { KnowledgeEventState.Considering, KnowledgeEventState.Harvested },
        { KnowledgeEventState.Going, KnowledgeEventState.Attended },
        { KnowledgeEventState.Going, KnowledgeEventState.Harvested },
        { KnowledgeEventState.Attended, KnowledgeEventState.Harvested },
    };

    public static TheoryData<KnowledgeEventState, KnowledgeEventState> BackwardOrSameMoves() => new()
    {
        { KnowledgeEventState.Spotted, KnowledgeEventState.Spotted },
        { KnowledgeEventState.Considering, KnowledgeEventState.Spotted },
        { KnowledgeEventState.Considering, KnowledgeEventState.Considering },
        { KnowledgeEventState.Going, KnowledgeEventState.Spotted },
        { KnowledgeEventState.Going, KnowledgeEventState.Considering },
        { KnowledgeEventState.Going, KnowledgeEventState.Going },
        { KnowledgeEventState.Attended, KnowledgeEventState.Spotted },
        { KnowledgeEventState.Attended, KnowledgeEventState.Considering },
        { KnowledgeEventState.Attended, KnowledgeEventState.Going },
        { KnowledgeEventState.Attended, KnowledgeEventState.Attended },
    };

    [Theory]
    [MemberData(nameof(ForwardMoves))]
    public void Every_forward_move_is_accepted_skips_included(KnowledgeEventState from, KnowledgeEventState to)
    {
        var knowledgeEvent = At(from);

        Assert.True(knowledgeEvent.MoveTo(to).IsSuccess);
        Assert.Equal(to, knowledgeEvent.State);
    }

    [Theory]
    [MemberData(nameof(BackwardOrSameMoves))]
    public void A_backward_or_same_state_move_is_refused(KnowledgeEventState from, KnowledgeEventState to)
    {
        var knowledgeEvent = At(from);

        Assert.Equal("knowledge_event.state_backward", knowledgeEvent.MoveTo(to).Error.Code);
        Assert.Equal(from, knowledgeEvent.State);
    }

    [Theory]
    [InlineData(KnowledgeEventState.Spotted)]
    [InlineData(KnowledgeEventState.Considering)]
    [InlineData(KnowledgeEventState.Going)]
    [InlineData(KnowledgeEventState.Attended)]
    public void Skipped_is_reached_only_by_skipping_never_by_a_move(KnowledgeEventState from)
    {
        var knowledgeEvent = At(from);

        var refused = knowledgeEvent.MoveTo(KnowledgeEventState.Skipped).Error;

        Assert.Equal("knowledge_event.state_backward", refused.Code);
        Assert.Equal("Skipped is reached by skipping the event, with a reason.", refused.Message);
        Assert.Equal(from, knowledgeEvent.State);
    }

    // --- Only a Spotted or Considering event is skipped ---------------------------------

    [Theory]
    [InlineData(KnowledgeEventState.Spotted)]
    [InlineData(KnowledgeEventState.Considering)]
    public void A_spotted_or_considering_event_is_skipped(KnowledgeEventState from)
    {
        var knowledgeEvent = At(from);

        Assert.True(knowledgeEvent.Skip("Clashes with the sprint review").IsSuccess);
        Assert.Equal(KnowledgeEventState.Skipped, knowledgeEvent.State);
    }

    [Theory]
    [InlineData(KnowledgeEventState.Going)]
    [InlineData(KnowledgeEventState.Attended)]
    public void A_going_or_attended_event_cannot_be_skipped(KnowledgeEventState from)
    {
        var knowledgeEvent = At(from);

        Assert.Equal("knowledge_event.cannot_skip", knowledgeEvent.Skip("Changed my mind").Error.Code);
        Assert.Equal(from, knowledgeEvent.State);
        Assert.Null(knowledgeEvent.SkipReason);
    }

    // --- A skipped event has a reason ---------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Skipping_without_a_reason_is_refused_and_leaves_the_state(string reason)
    {
        var spotted = Spotted();

        Assert.Equal("knowledge_event.skip_reason_missing", spotted.Skip(reason).Error.Code);
        Assert.Equal(KnowledgeEventState.Spotted, spotted.State);
        Assert.Null(spotted.SkipReason);
    }

    [Fact]
    public void A_skipped_event_keeps_its_reason()
    {
        var spotted = Spotted();

        spotted.Skip("  Too far to travel  ");

        Assert.Equal("Too far to travel", spotted.SkipReason?.Text);
    }

    // --- Harvested and Skipped are final ------------------------------------------------

    public static TheoryData<KnowledgeEventState, KnowledgeEventState> MovesOutOfAFinalState()
    {
        var moves = new TheoryData<KnowledgeEventState, KnowledgeEventState>();

        foreach (var final in new[] { KnowledgeEventState.Harvested, KnowledgeEventState.Skipped })
        {
            foreach (var target in Enum.GetValues<KnowledgeEventState>())
            {
                moves.Add(final, target);
            }
        }

        return moves;
    }

    [Theory]
    [MemberData(nameof(MovesOutOfAFinalState))]
    public void A_harvested_or_skipped_event_moves_to_no_other_state(KnowledgeEventState final, KnowledgeEventState target)
    {
        var knowledgeEvent = At(final);

        Assert.Equal("knowledge_event.state_final", knowledgeEvent.MoveTo(target).Error.Code);
        Assert.Equal(final, knowledgeEvent.State);
    }

    [Theory]
    [InlineData(KnowledgeEventState.Harvested)]
    [InlineData(KnowledgeEventState.Skipped)]
    public void A_harvested_or_skipped_event_is_not_skipped_again(KnowledgeEventState final)
    {
        var knowledgeEvent = At(final);

        Assert.Equal("knowledge_event.state_final", knowledgeEvent.Skip("Once more").Error.Code);
        Assert.Equal(final, knowledgeEvent.State);
    }

    // --- A verdict waits for the event --------------------------------------------------

    [Theory]
    [InlineData(KnowledgeEventState.Spotted)]
    [InlineData(KnowledgeEventState.Considering)]
    [InlineData(KnowledgeEventState.Going)]
    [InlineData(KnowledgeEventState.Skipped)]
    public void A_verdict_before_the_event_is_attended_is_refused(KnowledgeEventState state)
    {
        var knowledgeEvent = At(state);

        Assert.Equal("knowledge_event.verdict_too_early", knowledgeEvent.SetVerdict("Worth it").Error.Code);
        Assert.Null(knowledgeEvent.Verdict);
    }

    [Theory]
    [InlineData(KnowledgeEventState.Attended)]
    [InlineData(KnowledgeEventState.Harvested)]
    public void An_attended_or_harvested_event_takes_a_verdict(KnowledgeEventState state)
    {
        var knowledgeEvent = At(state);

        Assert.True(knowledgeEvent.SetVerdict("  Worth it for the Aspire talk  ").IsSuccess);
        Assert.Equal("Worth it for the Aspire talk", knowledgeEvent.Verdict?.Text);
    }

    [Fact]
    public void A_blank_verdict_clears_it()
    {
        var attended = At(KnowledgeEventState.Attended);
        attended.SetVerdict("Worth it");

        Assert.True(attended.SetVerdict(" ").IsSuccess);
        Assert.Null(attended.Verdict);
    }

    [Fact]
    public void Clearing_a_verdict_waits_for_the_event_too()
    {
        Assert.Equal("knowledge_event.verdict_too_early", Spotted().SetVerdict(null).Error.Code);
    }

    // --- An event holds at most one harvest task ----------------------------------------

    [Fact]
    public void The_first_harvest_task_is_linked()
    {
        var going = At(KnowledgeEventState.Going);

        Assert.True(going.LinkHarvestTask("task-harvest").IsSuccess);
        Assert.Equal(EventTaskLink.Create("task-harvest", TaskPurpose.Harvest), Assert.Single(going.Tasks));
        Assert.Equal(TaskPurpose.Harvest, going.Tasks[0].Purpose);
    }

    [Fact]
    public void A_second_harvest_task_is_refused()
    {
        var going = At(KnowledgeEventState.Going);
        going.LinkHarvestTask("task-harvest");

        Assert.Equal("knowledge_event.harvest_task_exists", going.LinkHarvestTask("task-other").Error.Code);
        Assert.Equal("task-harvest", Assert.Single(going.Tasks).TaskId);
    }

    [Fact]
    public void Linking_the_same_harvest_task_again_changes_nothing()
    {
        var going = At(KnowledgeEventState.Going);
        going.LinkHarvestTask("task-harvest");

        Assert.True(going.LinkHarvestTask("task-harvest").IsSuccess);
        Assert.Single(going.Tasks);
    }

    [Fact]
    public void A_task_already_linked_for_another_purpose_becomes_the_harvest_task()
    {
        var going = At(KnowledgeEventState.Going);
        going.LinkTask(EventTaskLink.Create("task-1", TaskPurpose.Linked));

        Assert.True(going.LinkHarvestTask("task-1").IsSuccess);

        Assert.Equal(TaskPurpose.Harvest, Assert.Single(going.Tasks).Purpose);
    }

    [Fact]
    public void A_task_already_linked_is_not_made_a_second_harvest_task()
    {
        var going = At(KnowledgeEventState.Going);
        going.LinkHarvestTask("task-harvest");
        going.LinkTask(EventTaskLink.Create("task-1", TaskPurpose.Prep));

        Assert.Equal("knowledge_event.harvest_task_exists", going.LinkHarvestTask("task-1").Error.Code);
        Assert.Equal(TaskPurpose.Prep, going.Tasks.Single(task => task.TaskId == "task-1").Purpose);
    }

    [Fact]
    public void A_harvest_link_through_link_task_keeps_the_same_rule()
    {
        var going = At(KnowledgeEventState.Going);
        going.LinkHarvestTask("task-harvest");

        var result = going.LinkTask(EventTaskLink.Create("task-other", TaskPurpose.Harvest));

        Assert.Equal("knowledge_event.harvest_task_exists", result.Error.Code);
        Assert.Single(going.Tasks);
    }

    [Fact]
    public void Prep_and_linked_tasks_sit_beside_the_harvest_task()
    {
        var going = At(KnowledgeEventState.Going);
        going.LinkHarvestTask("task-harvest");

        Assert.True(going.LinkTask(EventTaskLink.Create("task-hotel", TaskPurpose.Prep)).IsSuccess);
        Assert.True(going.LinkTask(EventTaskLink.Create("task-slides", TaskPurpose.Linked)).IsSuccess);
        Assert.Equal(["task-harvest", "task-hotel", "task-slides"], going.Tasks.Select(link => link.TaskId));
    }

    // --- A note or task is linked once --------------------------------------------------

    [Fact]
    public void Linking_a_note_twice_leaves_one_link()
    {
        var going = At(KnowledgeEventState.Going);
        going.LinkNote(NoteLink.Create(NoteLinkKind.Talk, "inbox-7", "Keynote"));

        Assert.True(going.LinkNote(NoteLink.Create(NoteLinkKind.Talk, "inbox-7", "Keynote, retitled")).IsSuccess);
        Assert.Equal("Keynote", Assert.Single(going.Notes).Title);
    }

    [Fact]
    public void A_brain_note_and_a_talk_note_are_both_linked()
    {
        var going = At(KnowledgeEventState.Going);

        going.LinkNote(NoteLink.Create(NoteLinkKind.Brain, "events/techorama.md", "Techorama"));
        going.LinkNote(NoteLink.Create(NoteLinkKind.Talk, "inbox-7", "Keynote"));

        Assert.Equal(2, going.Notes.Count);
    }

    [Fact]
    public void Linking_a_task_twice_leaves_one_link_whatever_its_purpose()
    {
        var going = At(KnowledgeEventState.Going);
        going.LinkTask(EventTaskLink.Create("task-hotel", TaskPurpose.Prep));

        Assert.True(going.LinkTask(EventTaskLink.Create("task-hotel", TaskPurpose.Linked)).IsSuccess);
        Assert.Equal(TaskPurpose.Prep, Assert.Single(going.Tasks).Purpose);
    }

    private static KnowledgeEvent Spotted() => KnowledgeEvent.Create(Guid.NewGuid(), "Techorama").Value;

    /// <summary>An event brought to <paramref name="state"/> the way the person would: by a
    /// forward move, or by skipping it with a reason.</summary>
    private static KnowledgeEvent At(KnowledgeEventState state)
    {
        var knowledgeEvent = Spotted();

        switch (state)
        {
            case KnowledgeEventState.Spotted:
                break;
            case KnowledgeEventState.Skipped:
                Assert.True(knowledgeEvent.Skip("Not this year").IsSuccess);
                break;
            case KnowledgeEventState.Considering or KnowledgeEventState.Going or KnowledgeEventState.Attended or KnowledgeEventState.Harvested:
                Assert.True(knowledgeEvent.MoveTo(state).IsSuccess);
                break;
        }

        return knowledgeEvent;
    }

    private static EventDate Day(int year, int month, int day) =>
        EventDate.Create(new DateOnly(year, month, day), time: null, utcOffset: null).Value;
}
