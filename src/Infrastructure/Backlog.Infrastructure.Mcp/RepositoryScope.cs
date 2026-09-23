using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// An <c>owner/name</c> a session sent, as a repository this product knows.
/// <para>
/// Local ADR 0012 §4: every tool whose answer depends on a repository takes the
/// argument in <c>owner/name</c> form, read from the git remote by the calling
/// skill, and the server maps it through
/// <see cref="IRepositoryDirectory.Resolve"/> — which already matches a name
/// containing a <c>/</c> against <see cref="TasksRepositoryRef.Id"/> without
/// regard to case. There is no second matching rule here, on purpose: a tool that
/// normalized the name itself would be a second opinion about what a repository
/// is called.
/// </para>
/// <para>
/// <see cref="IRepositoryDirectory.Register"/> is never called from this
/// assembly: "a session mentioning a repository is not a plan introducing one".
/// A name the directory does not know is an ordinary answer that says so.
/// </para>
/// <para>
/// That claim used to be stated as the read-only claim "on this side", and it
/// outlived the read-only assembly: <see cref="TrackerTools"/> writes to the
/// backlog. It survives unweakened about the thing it was always about, which is
/// the <em>registry</em>. A tool that creates an entry against
/// <c>JSdotNet/Backlog</c> still cannot bring <c>JSdotNet/Backlog</c> into
/// existence — the create is refused instead, before anything is written, which
/// is why every tool resolves before it reads or saves.
/// </para>
/// </summary>
internal static class RepositoryScope
{
    /// <summary>The code every unknown-repository answer carries.</summary>
    internal const string NotFoundCode = "repository.not_found";

    /// <summary>The repository, or the error naming what was asked for. Never a
    /// registration.</summary>
    internal static Result<TasksRepositoryRef> Resolve(IRepositoryDirectory directory, string? repository)
    {
        ArgumentNullException.ThrowIfNull(directory);

        var name = repository?.Trim();

        if (string.IsNullOrEmpty(name))
        {
            return Error.Validation(
                "repository.required",
                "A repository is required, in owner/name form — the git remote of the checkout you are working in.");
        }

        return directory.Resolve(name) is { } resolved
            ? resolved
            : Error.NotFound(NotFoundCode, $"No repository is registered as '{name}'.");
    }

    /// <summary>
    /// An <see cref="Error"/> as the failure a client sees.
    /// <para>
    /// A tool returns its payload and throws for everything else, which is the
    /// SDK's own division: an <see cref="McpException"/>'s message is the one
    /// exception text that reaches the client, and anything else comes back as a
    /// generic error so a stack trace cannot leak. So the domain error keeps
    /// being the single source of the code and the wording — <c>Error.ToString</c>
    /// is <c>code: message</c> — and this is only the crossing.
    /// </para>
    /// </summary>
    internal static McpException Failure(Error error) => new(error.ToString());

    /// <summary>The value, or the failure thrown. For a tool whose whole body is
    /// "resolve this, then answer".</summary>
    internal static T ValueOrThrow<T>(this Result<T> result) =>
        result.IsSuccess ? result.Value : throw Failure(result.Error);
}
