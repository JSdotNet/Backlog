using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.UI.Components.Goals;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Progress.UI.Goals;

/// <summary>
/// Cascades the goal control to every screen of the host
/// (<c>.devbook/domain/progress/features.md#attaching-work-to-a-goal</c>).
/// </summary>
/// <remarks>
/// A root-level cascading value rather than a parameter threaded through each screen:
/// the places that offer "Attach to goal" are in the shared library and in contexts that
/// may not reference Progress, and each of them asks only for a
/// <see cref="GoalControlSlot"/>. The slot is offered while a goal port is composed, so a
/// host without Progress draws no empty wrapper. Goals are always on (local ADR 0035 §10),
/// so nothing changes whether it is offered while the host runs.
/// </remarks>
public static class GoalControlRegistration
{
    public static IServiceCollection AddGoalControls(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCascadingValue(provider =>
        {
            var composed = provider.GetService<IGoals>() is not null;

            return new GoalControlSlot(
                subject => builder =>
                {
                    builder.OpenComponent<GoalAttach>(0);
                    builder.AddComponentParameter(1, nameof(GoalAttach.Subject), subject);
                    // Keyed by the work, so a list that reorders keeps each control on its
                    // own item rather than handing one item's read to another.
                    builder.SetKey(subject);
                    builder.CloseComponent();
                },
                isOffered: () => composed);
        });

        return services;
    }
}
