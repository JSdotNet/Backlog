namespace Backlog.Modules.Progress.Abstractions.Services;

/// <summary>
/// The repositories a sprint series may be given, by the alias a series holds them
/// under (<c>.devbook/domain/progress/dependencies.md</c>, Progress = customer).
/// <para>
/// Progress does not own the repository list and must not become a second place it is
/// configured, so it asks — the shape Tasks' own repository port takes, for the same
/// reason. The Sprints settings page reads it to offer the repositories in no series;
/// the rule that a repository belongs to one series stays the module's.
/// </para>
/// </summary>
public interface ISprintRepositories
{
    /// <summary>Every repository the registry knows, by alias, in the order it holds
    /// them. Empty when none is configured.</summary>
    IReadOnlyList<string> Aliases { get; }
}
