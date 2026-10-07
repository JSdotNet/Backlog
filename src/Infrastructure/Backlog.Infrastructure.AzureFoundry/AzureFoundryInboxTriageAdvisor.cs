using System.Text.Json;

using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.AzureFoundry;

/// <summary>
/// Answers the Inbox's <see cref="IInboxTriageAdvisor"/> port over the Azure
/// Foundry deployment the plan drafter already uses (local ADR 0023 §3): the
/// item and its context become <see cref="AzureFoundryTriagePrompt"/>'s two
/// messages, the model's JSON becomes the Inbox's DTOs, and a thrown transport
/// failure becomes the <c>inbox.triage.failed</c> the pane reads as "no cards".
/// <para>
/// Shaped like <see cref="AzureFoundryInboxPlanDrafter"/> and registered beside
/// it, with the same lifetime and for the same reasons. <see cref="IsAvailable"/>
/// is read straight off the same settings store on every ask, so configuring
/// Foundry in Settings shows the AI surfaces without a restart.
/// </para>
/// <para>
/// Parsing is lenient about the wrapper and strict about the content: prose or
/// a fence around the object is stepped over, but an answer with no JSON
/// object in it is a failure, and an entry whose id is not a GUID or whose
/// kind, action or entry type is not one of the words the prompt names is
/// dropped. Whether an id names something real is the Inbox handlers' check,
/// not this class's.
/// </para>
/// </summary>
public sealed class AzureFoundryInboxTriageAdvisor(IAzureFoundryTriageClient client, AzureFoundrySettingsStore settingsStore) : IInboxTriageAdvisor
{
    /// <summary>What an answer with no JSON object in it fails with.</summary>
    internal const string UnreadableMessage = "Azure Foundry answered with something that is not a triage answer.";

    public bool IsAvailable => settingsStore.Current.IsConfigured;

    public Task<Result<InboxTriageAdviceDto>> AdviseAsync(
        InboxTriageItemDto item,
        InboxTriageContextDto context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(context);

        return AskAsync(
            AzureFoundryTriagePrompt.AdviceText,
            AzureFoundryTriagePrompt.AdviceUser(item, context),
            root => ReadAdvice(root, item, context),
            cancellationToken);
    }

    public Task<Result<InboxTriagePassDto>> ProposePassAsync(
        IReadOnlyList<InboxTriageItemDto> items,
        InboxTriageContextDto context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(context);

        return AskAsync(
            AzureFoundryTriagePrompt.PassText,
            AzureFoundryTriagePrompt.PassUser(items, context),
            ReadPass,
            cancellationToken);
    }

    private async Task<Result<T>> AskAsync<T>(string system, string user, Func<JsonElement, T> read, CancellationToken cancellationToken)
    {
        string answer;
        try
        {
            answer = await client.CompleteTriageAsync(system, user, cancellationToken).ConfigureAwait(false);
        }
        catch (AzureFoundryException ex)
        {
            return InboxErrors.TriageFailed(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            // For a client that is not AzureFoundryChatClient, in the same
            // words, as the drafter does.
            return InboxErrors.TriageFailed(AzureFoundryChatClient.CouldNotReachMessage(ex.Message));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return InboxErrors.TriageFailed(AzureFoundryChatClient.TimedOutMessage);
        }

        if (!TryParse(answer, out var document)) return InboxErrors.TriageFailed(UnreadableMessage);

        using (document)
        {
            return read(document.RootElement);
        }
    }

    /// <summary>The first JSON object in the answer: the whole answer when it
    /// is one, else the span from its first <c>{</c> to its last <c>}</c>.</summary>
    internal static bool TryParse(string answer, out JsonDocument document)
    {
        document = null!;
        var start = answer.IndexOf('{', StringComparison.Ordinal);
        var end = answer.LastIndexOf('}');
        if (start < 0 || end <= start) return false;

        try
        {
            document = JsonDocument.Parse(answer[start..(end + 1)]);
        }
        catch (JsonException)
        {
            return false;
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object) return true;

        document.Dispose();
        return false;
    }

    internal static InboxTriageAdviceDto ReadAdvice(JsonElement root, InboxTriageItemDto item, InboxTriageContextDto context)
    {
        InboxTriageDuplicateDto? duplicate = null;
        if (Object(root, "duplicate") is { } named
            && Kind(String(named, "kind")) is { } kind
            && Id(String(named, "id")) is { } targetId)
        {
            var title = kind == InboxTriageTargetKind.Task
                ? context.OpenTasks.FirstOrDefault(task => task.Id == targetId)?.Title
                : context.OtherItems.FirstOrDefault(other => other.Id == targetId)?.Title;
            duplicate = new InboxTriageDuplicateDto(kind, targetId, title ?? string.Empty, String(named, "reason") ?? string.Empty);
        }

        InboxTriagePlanGroupingDto? plan = null;
        if (Object(root, "plan") is { } grouping && String(grouping, "title") is { Length: > 0 } planTitle)
        {
            plan = new InboxTriagePlanGroupingDto(planTitle, Ids(grouping, "itemIds"), String(grouping, "reason") ?? string.Empty);
        }

        return new InboxTriageAdviceDto(item.Id, duplicate, plan, Strings(root, "repositories"));
    }

    internal static InboxTriagePassDto ReadPass(JsonElement root)
    {
        var plans = Array(root, "plans")
            .Select(plan => String(plan, "title") is { Length: > 0 } title
                ? new InboxTriagePlanProposalDto(title, Ids(plan, "itemIds"), Strings(plan, "repositories"), Reason(plan), Confidence(plan))
                : null)
            .OfType<InboxTriagePlanProposalDto>()
            .ToList();

        var duplicates = Array(root, "duplicates")
            .Select(pair => Id(String(pair, "itemId")) is { } itemId
                && Kind(String(pair, "kind")) is { } kind
                && Id(String(pair, "targetId")) is { } targetId
                && Action(String(pair, "action")) is { } action
                    ? new InboxTriageDuplicatePairDto(itemId, kind, targetId, action, Reason(pair), Confidence(pair))
                    : null)
            .OfType<InboxTriageDuplicatePairDto>()
            .ToList();

        var routes = Array(root, "routes")
            .Select(route => Id(String(route, "itemId")) is { } itemId
                && String(route, "entryType")?.Trim().ToLowerInvariant() is { } type
                && InboxTriagePassDto.EntryTypes.Contains(type)
                    ? new InboxTriageRouteProposalDto(itemId, type, Strings(route, "repositories"), Reason(route), Confidence(route))
                    : null)
            .OfType<InboxTriageRouteProposalDto>()
            .ToList();

        var filings = Array(root, "filings")
            .Select(filing => Id(String(filing, "itemId")) is { } itemId && Id(String(filing, "listId")) is { } listId
                ? new InboxTriageListFilingDto(itemId, listId, Reason(filing), Confidence(filing))
                : null)
            .OfType<InboxTriageListFilingDto>()
            .ToList();

        var archives = Array(root, "archives")
            .Select(archive => Id(String(archive, "itemId")) is { } itemId
                ? new InboxTriageArchiveDto(itemId, Reason(archive), Confidence(archive))
                : null)
            .OfType<InboxTriageArchiveDto>()
            .ToList();

        var unplaced = Array(root, "unplaced")
            .Select(id => id.ValueKind == JsonValueKind.String ? Id(id.GetString()) : null)
            .OfType<Guid>()
            .ToList();

        return new InboxTriagePassDto(plans, duplicates, routes, filings, archives, unplaced);
    }

    private static JsonElement? Object(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static IEnumerable<JsonElement> Array(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(element => element.ValueKind is JsonValueKind.Object or JsonValueKind.String)
            : [];

    private static string? String(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static List<string> Strings(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray()
                .Where(element => element.ValueKind == JsonValueKind.String)
                .Select(element => element.GetString()!.Trim())
                .Where(text => text.Length > 0)]
            : [];

    private static List<Guid> Ids(JsonElement parent, string name) =>
        [.. Strings(parent, name).Select(Id).OfType<Guid>()];

    private static Guid? Id(string? text) => Guid.TryParse(text, out var id) ? id : null;

    private static string Reason(JsonElement parent) => String(parent, "reason") ?? string.Empty;

    /// <summary>The confidence as a number from 0 to 1; a missing or unreadable
    /// one is 0, so the proposal starts unaccepted rather than trusted.</summary>
    private static double Confidence(JsonElement parent) =>
        parent.TryGetProperty("confidence", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var confidence)
            ? Math.Clamp(confidence, 0, 1)
            : 0;

    private static InboxTriageTargetKind? Kind(string? word) => word?.ToLowerInvariant() switch
    {
        "task" => InboxTriageTargetKind.Task,
        "item" or "inbox-item" or "inboxitem" => InboxTriageTargetKind.InboxItem,
        _ => null,
    };

    private static InboxDuplicateAction? Action(string? word) => word?.ToLowerInvariant() switch
    {
        "merge-into-task" => InboxDuplicateAction.MergeIntoTask,
        "keep-one-attach-other" => InboxDuplicateAction.KeepOneAttachOther,
        "keep-both" => InboxDuplicateAction.KeepBoth,
        _ => null,
    };
}
