using Backlog.UI.Components.Goals;
using Microsoft.AspNetCore.Components;

namespace Backlog.UI.Storybook.Components.Shared;

/// <summary>
/// What a page stands in for when it shows a goal control on its own components: the
/// <see cref="GoalControlSlot"/> a host cascades, drawing the library's
/// <see cref="GoalControl"/> over the page's sample goals, and the state of the one
/// <see cref="GoalPicker"/> the page lays beside it. The application's slot reads and
/// writes goals through the goal port instead; the controls are the same.
/// </summary>
internal sealed class GoalStoryHost
{
    private readonly Dictionary<GoalSubject, GoalOption> _goals;

    /// <param name="page">The page, so a press re-renders it.</param>
    /// <param name="attached">The goals some subjects count toward at first.</param>
    public GoalStoryHost(IHandleEvent page, IReadOnlyDictionary<GoalSubject, GoalOption> attached)
    {
        _goals = new Dictionary<GoalSubject, GoalOption>(attached);
        Slot = new GoalControlSlot(subject => builder =>
        {
            builder.OpenComponent<GoalControl>(0);
            builder.AddComponentParameter(1, nameof(GoalControl.GoalTitle), GoalOf(subject)?.Title);
            builder.AddComponentParameter(2, nameof(GoalControl.OnOpen), EventCallback.Factory.Create(page, () => Open(subject)));
            builder.SetKey(subject);
            builder.CloseComponent();
        });
    }

    /// <summary>What the page cascades.</summary>
    public GoalControlSlot Slot { get; }

    /// <summary>Whether the picker is open.</summary>
    public bool Picking { get; set; }

    /// <summary>The work the picker was opened for.</summary>
    public GoalSubject? Subject { get; private set; }

    /// <summary>The goal the open subject counts toward.</summary>
    public GoalOption? Current => GoalOf(Subject);

    public GoalOption? GoalOf(GoalSubject? subject) =>
        subject is not null && _goals.TryGetValue(subject, out var goal) ? goal : null;

    public void Attach(Guid id) => Attach(GoalFixtures.Open.First(goal => goal.Id == id));

    public void Create(GoalDraft draft) => Attach(new GoalOption(Guid.NewGuid(), draft.Title, 0, 0, draft.DueDate));

    private void Open(GoalSubject subject)
    {
        Subject = subject;
        Picking = true;
    }

    private void Attach(GoalOption goal)
    {
        if (Subject is { } subject) _goals[subject] = goal;
        Picking = false;
    }
}
