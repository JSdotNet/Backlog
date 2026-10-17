using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.SharedKernel;
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
/// <see cref="GoalControlSlot"/>. The slot is offered only while the goals switch is on
/// and a goal port is composed, so a place that wraps the control draws no empty wrapper
/// with goals off; it raises its watch on every feature change, so those places draw the
/// control again the moment goals are turned on. <see cref="GoalAttach"/> reads the switch
/// as well, for the places that draw it without a wrapper.
/// </remarks>
public static class GoalControlRegistration
{
    public static IServiceCollection AddGoalControls(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCascadingValue(provider =>
        {
            var features = provider.GetService<IAppFeatureSettings>();
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
                isOffered: () => composed && (features?.IsEnabled(ProgressFeatures.Goals) ?? false),
                watch: features is null ? null : changed => new FeatureWatch(features, changed));
        });

        return services;
    }

    /// <summary>One subscription to the feature switches, let go on dispose.</summary>
    private sealed class FeatureWatch : IDisposable
    {
        private readonly IAppFeatureSettings _features;
        private readonly Action _changed;
        private bool _disposed;

        public FeatureWatch(IAppFeatureSettings features, Action changed)
        {
            _features = features;
            _changed = changed;
            _features.Changed += _changed;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _features.Changed -= _changed;
        }
    }
}
