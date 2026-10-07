using System.Text.Json;
using System.Text.Json.Serialization;

using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.AzureFoundry;

/// <summary>
/// What the model is told when the Inbox asks it to advise triage (local
/// ADR 0023), and how the item and its context are written out for it.
/// <para>
/// Both questions are asked and answered in JSON, unlike the plan drafter's,
/// whose answer is entry text Tasks' import reads: a triage answer is read by
/// nobody but <see cref="AzureFoundryInboxTriageAdvisor"/>, so it is shaped for
/// a parser rather than a person. The user message is the facts ADR 0023 §1
/// lets leave the machine and nothing else: the item being decided in full
/// (<see cref="ItemJson"/>), and of what it is compared with only the title,
/// id, repositories and status — <see cref="TaskJson"/> for an open task,
/// <see cref="OtherItemJson"/> for another unprocessed capture, whose notes and
/// link stay on the machine. Lists go by id as well as name, because a filing
/// names the list by id.
/// </para>
/// <para>
/// <see cref="Marker"/> opens both system messages, so a request can be told
/// from a plan request by its first line. The local stand-in service does not
/// answer it — the desktop harness swaps in its own advisor instead (see
/// <c>FakeInboxTriageAdvisor</c>) — so with that swap off and the stand-in
/// configured, a triage call reads as unanswerable and no card is shown.
/// </para>
/// </summary>
public static class AzureFoundryTriagePrompt
{
    /// <summary>The first line of both system messages.</summary>
    public const string Marker = "You triage the captures in a Backlog inbox.";

    private const string Shared = """
        The user message is one JSON document. "openTasks" are the open backlog tasks, "otherInboxItems" the other captures nobody has decided yet, "repositories" the repositories configured in the app (owner/name), and "lists" the reader's inbox lists. Every id is a GUID; copy ids exactly as written and never invent one.

        Answer with one JSON object and nothing else: no prose, no code fence. Write every reason as one short sentence a person reads on a card. Name a repository only when it is in "repositories" or on the item itself. Leave a field empty rather than guess: a weak proposal teaches the reader to ignore the cards.
        """;

    /// <summary>The system message for the cards over one item.</summary>
    public const string AdviceText = Marker + """


        The reader has opened one capture, "item", and you propose at most two cards for it.
        """ + "\n\n" + Shared + """


        Answer with this shape:
        {
          "duplicate": { "kind": "task" | "item", "id": "<id of the open task or the other inbox item it repeats>", "reason": "<one sentence>" } or null,
          "plan": { "title": "<a short plan title>", "itemIds": ["<the item's id>", "<the id of each other inbox item that belongs in the same plan>"], "reason": "<one sentence>" } or null,
          "repositories": ["<owner/name the item should go to>"]
        }
        Propose a duplicate only when the capture asks for the same work as that task or item, not merely the same topic. Propose a plan only when at least one other inbox item belongs in it with this one.
        """;

    /// <summary>The system message for the pass over a slice.</summary>
    public const string PassText = Marker + """


        The reader asks you to propose a decision for every capture in "items". Place each item at most once; list every item you cannot place under "unplaced".
        """ + "\n\n" + Shared + """


        Answer with this shape:
        {
          "plans": [ { "title": "<plan title>", "itemIds": ["<two or more ids from items>"], "repositories": ["<owner/name>"], "reason": "<one sentence>", "confidence": <0 to 1> } ],
          "duplicates": [ { "itemId": "<id from items>", "kind": "task" | "item", "targetId": "<id of the open task, or of another inbox item>", "action": "merge-into-task" | "keep-one-attach-other" | "keep-both", "reason": "<one sentence>", "confidence": <0 to 1> } ],
          "routes": [ { "itemId": "<id from items>", "entryType": "prompt" | "task" | "test", "repositories": ["<owner/name>"], "reason": "<one sentence>", "confidence": <0 to 1> } ],
          "filings": [ { "itemId": "<id from items>", "listId": "<id from lists>", "reason": "<one sentence>", "confidence": <0 to 1> } ],
          "archives": [ { "itemId": "<id from items>", "reason": "<one sentence>", "confidence": <0 to 1> } ],
          "unplaced": ["<id from items>"]
        }
        "merge-into-task" is only for a duplicate of an open task. A route's entry type is "prompt" for an instruction an agent can carry out in a repository, "task" for a step a person does, "test" for something a person checks. Confidence is how sure you are; below 0.6 the reader sees the proposal unticked.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>The user message for the cards: the item, then its context.</summary>
    public static string AdviceUser(InboxTriageItemDto item, InboxTriageContextDto context)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(context);

        var written = Context(context);
        return JsonSerializer.Serialize(
            new AdviceMessage(ItemJson.From(item), written.OpenTasks, written.OtherInboxItems, context.Repositories, written.Lists),
            JsonOptions);
    }

    /// <summary>The user message for the pass: the items, then their context.</summary>
    public static string PassUser(IReadOnlyList<InboxTriageItemDto> items, InboxTriageContextDto context)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(context);

        var written = Context(context);
        return JsonSerializer.Serialize(
            new PassMessage([.. items.Select(ItemJson.From)], written.OpenTasks, written.OtherInboxItems, context.Repositories, written.Lists),
            JsonOptions);
    }

    private static (IReadOnlyList<TaskJson> OpenTasks, IReadOnlyList<OtherItemJson> OtherInboxItems, IReadOnlyList<ListJson> Lists) Context(InboxTriageContextDto context) =>
        ([.. context.OpenTasks.Select(task => new TaskJson(task.Id.ToString("D"), task.Title, task.RepoIds, task.Status))],
         [.. context.OtherItems.Select(other => new OtherItemJson(other.Id.ToString("D"), other.Title, other.RepoIds, OtherItemJson.Unprocessed))],
         [.. context.Lists.Select(list => new ListJson(list.Id.ToString("D"), list.Name))]);

    /// <summary>One capture as the model reads it: ADR 0023 §1's list, in its
    /// words — the notes as <c>notes</c>, the source link as <c>link</c>.</summary>
    internal sealed record ItemJson(
        string Id,
        string Title,
        string? Notes,
        string? Link,
        string Kind,
        string Source,
        string? Person,
        IReadOnlyList<string> Tags,
        IReadOnlyList<string> Repositories)
    {
        public static ItemJson From(InboxTriageItemDto item) => new(
            item.Id.ToString("D"),
            item.Title,
            string.IsNullOrWhiteSpace(item.BodyMd) ? null : item.BodyMd.Trim(),
            item.SourceUrl,
            item.KindSlug,
            item.Source,
            item.Person,
            item.Tags,
            item.RepoIds);
    }

    /// <summary>One open task: id, title, repositories, status — no body.</summary>
    internal sealed record TaskJson(string Id, string Title, IReadOnlyList<string> Repositories, string Status);

    /// <summary>Another unprocessed capture, as it is compared against: id,
    /// title, repositories, status — never its notes or link (ADR 0023 §1).</summary>
    internal sealed record OtherItemJson(string Id, string Title, IReadOnlyList<string> Repositories, string Status)
    {
        /// <summary>The only status a capture in the context can have.</summary>
        public const string Unprocessed = "unprocessed";
    }

    internal sealed record ListJson(string Id, string Name);

    private sealed record AdviceMessage(
        ItemJson Item,
        IReadOnlyList<TaskJson> OpenTasks,
        IReadOnlyList<OtherItemJson> OtherInboxItems,
        IReadOnlyList<string> Repositories,
        IReadOnlyList<ListJson> Lists);

    private sealed record PassMessage(
        IReadOnlyList<ItemJson> Items,
        IReadOnlyList<TaskJson> OpenTasks,
        IReadOnlyList<OtherItemJson> OtherInboxItems,
        IReadOnlyList<string> Repositories,
        IReadOnlyList<ListJson> Lists);
}
