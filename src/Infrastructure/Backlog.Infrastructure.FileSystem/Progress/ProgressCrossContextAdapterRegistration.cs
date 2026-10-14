using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.FileSystem.Progress;

/// <summary>
/// What Progress asks of the world outside it, answered by adapters that may see both
/// sides: the tasks a sprint names, read through Tasks' published
/// <see cref="ITaskItems"/> (<see cref="ISprintTaskSource"/>).
/// <para>
/// Scoped, because <see cref="ITaskItems"/> is: a singleton over a scoped port is a
/// captive dependency a validating provider refuses. Call after <c>AddTasksModule</c>
/// and <c>AddProgressModule</c>.
/// </para>
/// </summary>
public static class ProgressCrossContextAdapterRegistration
{
    public static IServiceCollection AddProgressCrossContextAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ISprintTaskSource>(sp =>
            new SprintTaskSource(
                () => sp.GetRequiredService<ITaskItems>(),
                () => sp.GetService<IRepositoryDirectory>()));

        return services;
    }
}
