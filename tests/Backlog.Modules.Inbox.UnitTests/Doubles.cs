using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>Items the way the tests want them: captured at a known instant,
/// through a known channel, with nothing else decided.</summary>
internal static class Items
{
    public static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    public static InboxItem Manual(string title = "Call the dentist") =>
        InboxItem.Capture(
            title,
            new InboxSource(InboxEnumMap.ManualChannel, Person: null),
            sourceUrl: null,
            ContentKind.Text,
            capturedAt: Noon,
            receivedAt: Noon,
            replicaBacked: false);

    public static InboxItem FromPhone(string title = "Call the dentist", Guid? id = null) =>
        InboxItem.FromCapture(
            id ?? Guid.CreateVersion7(),
            title,
            new InboxSource("mobile", Person: null),
            sourceUrl: null,
            ContentKind.Text,
            capturedAt: Noon,
            receivedAt: Noon.AddMinutes(5));
}

/// <summary>
/// The Tasks side of routing, recorded rather than performed: every request it
/// was handed, and the ids it answered with. Fails on demand so the handlers'
/// "the item is not touched" rule can be asserted.
/// </summary>
internal sealed class FakeBacklogTarget : IInboxBacklogTarget
{
    public List<InboxRouteRequestDto> Requests { get; } = [];

    public List<(string Plan, Guid SourceInboxId, IReadOnlyList<string> AllowedRepoIds)> Imports { get; } = [];

    public Error? FailWith { get; set; }

    public Task<Result<IReadOnlyList<Guid>>> CreateTasksAsync(InboxRouteRequestDto request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);

        if (FailWith is { } error) return Task.FromResult(Result.Failure<IReadOnlyList<Guid>>(error));

        // One id per repository, or one for none — the adapter's own rule,
        // mirrored so the handler's assertions about counts mean something.
        var count = Math.Max(request.RepoIds.Count, 1);
        return Task.FromResult(Result.Success<IReadOnlyList<Guid>>(
            [.. Enumerable.Range(0, count).Select(_ => Guid.CreateVersion7())]));
    }

    public Task<Result<IReadOnlyList<Guid>>> ImportPlanAsync(
        string planMarkdown,
        Guid sourceInboxId,
        IReadOnlyList<string> allowedRepoIds,
        CancellationToken cancellationToken = default)
    {
        Imports.Add((planMarkdown, sourceInboxId, allowedRepoIds));

        if (FailWith is { } error) return Task.FromResult(Result.Failure<IReadOnlyList<Guid>>(error));

        var entries = planMarkdown.Split('\n').Count(line => line.StartsWith("# ", StringComparison.Ordinal));
        return Task.FromResult(Result.Success<IReadOnlyList<Guid>>(
            [.. Enumerable.Range(0, entries).Select(_ => Guid.CreateVersion7())]));
    }
}

/// <summary>A drafter that answers with whatever the test set, and remembers
/// what it was asked. A <c>{tag}</c> in the answer is replaced by the plan tag
/// the request carried, for the tests about what the tag is.</summary>
internal sealed class FakePlanDrafter : IInboxPlanDrafter
{
    public bool IsAvailable { get; set; } = true;

    public string? UnavailableReason { get; set; }

    public string Answer { get; set; } = "# Step one\n`prompt` `!ready` `#plan`\n\nDo it.\n";

    public Error? FailWith { get; set; }

    public List<InboxPlanDraftRequestDto> Requests { get; } = [];

    public Task<Result<InboxPlanDraftDto>> DraftAsync(InboxPlanDraftRequestDto request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);

        return Task.FromResult(FailWith is { } error
            ? Result.Failure<InboxPlanDraftDto>(error)
            : Result.Success(new InboxPlanDraftDto(Answer.Replace("{tag}", request.PlanTag, StringComparison.Ordinal))));
    }
}
