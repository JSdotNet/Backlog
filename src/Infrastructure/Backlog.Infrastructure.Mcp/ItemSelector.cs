using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The one plan, task or pull request a tool's selectors name — <c>planTag</c>, or
/// <c>taskId</c>, or <c>repository</c> and <c>number</c> together — or the refusal saying why
/// there is not exactly one, with the rule and the codes <c>find_item</c> uses, so a session
/// learns one. Shared by the goal tools and the impediment tools, each turning the item into
/// its own port's words.
/// <para>
/// A pull request's repository is resolved through the registry, and the registered
/// <c>owner/name</c> is what is stored, so <c>jsdotnet/backlog</c> and
/// <c>JSdotNet/Backlog</c> name one pull request. A write that adds an item refuses a
/// repository the registry does not know, before anything is stored. One that takes an item
/// away does not (<c>registeredOnly</c> false): a pull request may be held in a repository
/// since removed from the registry, and refusing would leave it there for good. Such a name
/// is passed on as given, and taking away what is not held changes nothing. The registry is
/// never registered in (see <see cref="RepositoryScope"/>).
/// </para>
/// </summary>
internal static class ItemSelector
{
    internal const string RequiredCode = "selector.required";
    internal const string IncompleteCode = "selector.incomplete";

    /// <summary>The item the selectors name, as <paramref name="plan"/>,
    /// <paramref name="task"/> or <paramref name="pullRequest"/> build it; throws the
    /// client's failure when they name none, several, or half a pull request.</summary>
    internal static T One<T>(
        IRepositoryDirectory repositories,
        string? planTag,
        Guid? taskId,
        string? repository,
        int? number,
        bool registeredOnly,
        Func<string, T> plan,
        Func<Guid, T> task,
        Func<string, int, T> pullRequest)
    {
        var byPlan = !string.IsNullOrWhiteSpace(planTag);
        var byTask = taskId is not null;
        var byPullRequest = !string.IsNullOrWhiteSpace(repository) || number is not null;

        if (new[] { byPlan, byTask, byPullRequest }.Count(given => given) != 1)
        {
            throw RepositoryScope.Failure(Error.Validation(
                RequiredCode,
                "Name exactly one item: planTag, or taskId, or repository and number together."));
        }

        if (byPlan) return plan(planTag!.Trim());

        if (byTask) return task(taskId!.Value);

        if (string.IsNullOrWhiteSpace(repository) || number is null)
        {
            throw RepositoryScope.Failure(Error.Validation(
                IncompleteCode,
                "repository and number are one selector and have to be given together."));
        }

        return pullRequest(Repository(repositories, repository, registeredOnly), number.Value);
    }

    /// <summary>A pull request's repository as it is stored: the registered <c>owner/name</c>,
    /// or, when <paramref name="registeredOnly"/> is false, the name as given for one the
    /// registry does not know.</summary>
    internal static string Repository(IRepositoryDirectory repositories, string repository, bool registeredOnly)
    {
        var resolved = RepositoryScope.Resolve(repositories, repository);

        return resolved.IsSuccess ? resolved.Value.Id
            : registeredOnly ? throw RepositoryScope.Failure(resolved.Error)
            : repository.Trim();
    }
}
