using System.Net;
using System.Text;
using System.Text.Json;

using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;
using Backlog.Tests;

namespace Backlog.Infrastructure.AzureFoundry.UnitTests;

/// <summary>
/// The adapter between the Inbox's triage-advisor port and the Foundry
/// deployment (local ADR 0023): the prompt it writes, and how it reads the
/// model's answer — tested against canned answers, as the plan drafter's are.
/// </summary>
public sealed class AzureFoundryInboxTriageAdvisorTests : IDisposable
{
    private static readonly Guid ItemId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TaskId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ListId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly List<string> _paths = [];

    public void Dispose()
    {
        foreach (var path in _paths)
        {
            var directory = Path.GetDirectoryName(path);
            if (directory is null) continue;

            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    // --- Availability -------------------------------------------------------------

    [Fact]
    public void Availability_follows_the_foundry_settings_the_drafter_reads()
    {
        var settings = new AzureFoundrySettingsStore(NewSettingsPath());
        var advisor = new AzureFoundryInboxTriageAdvisor(new StubTriageClient(), settings);
        var drafter = new AzureFoundryInboxPlanDrafter(new UnavailableAzureFoundryChatClient(), settings);
        Assert.False(advisor.IsAvailable);

        settings.SetConnection("https://foundry.example.com", "chat", "secret", "2024-10-21");

        Assert.True(advisor.IsAvailable);
        Assert.Equal(drafter.IsAvailable, advisor.IsAvailable);
    }

    // --- The prompt ---------------------------------------------------------------

    [Fact]
    public async Task The_cards_are_asked_with_the_advice_prompt_and_the_item_and_its_context_as_json()
    {
        var client = new StubTriageClient { Answer = "{}" };
        var advisor = Advisor(client);

        await advisor.AdviseAsync(Item(), Context(), TestContext.Current.CancellationToken);

        var (system, user) = Assert.Single(client.Requests);
        Assert.StartsWith(AzureFoundryTriagePrompt.Marker, system, StringComparison.Ordinal);
        Assert.Equal(AzureFoundryTriagePrompt.AdviceText, system);

        using var document = JsonDocument.Parse(user);
        var root = document.RootElement;
        var item = root.GetProperty("item");
        Assert.Equal(ItemId.ToString("D"), item.GetProperty("id").GetString());
        Assert.Equal("Login page returns a server error", item.GetProperty("title").GetString());
        Assert.Equal("It started after the deploy.", item.GetProperty("notes").GetString());
        Assert.Equal("https://example.com/bug", item.GetProperty("link").GetString());
        Assert.Equal("article", item.GetProperty("kind").GetString());
        Assert.Equal("mobile", item.GetProperty("source").GetString());
        Assert.Equal("Ann", item.GetProperty("person").GetString());
        Assert.Equal("auth", item.GetProperty("tags")[0].GetString());
        Assert.Equal("acme/web", item.GetProperty("repositories")[0].GetString());

        var task = root.GetProperty("openTasks")[0];
        Assert.Equal(TaskId.ToString("D"), task.GetProperty("id").GetString());
        Assert.Equal("ready", task.GetProperty("status").GetString());
        Assert.Equal(OtherId.ToString("D"), root.GetProperty("otherInboxItems")[0].GetProperty("id").GetString());
        Assert.Equal("acme/web", root.GetProperty("repositories")[0].GetString());
        Assert.Equal("Reading", root.GetProperty("lists")[0].GetProperty("name").GetString());
    }

    /// <summary>Only what ADR 0023 §1 lists leaves the machine: the item's own
    /// fields and the context's, and nothing else — no attachment, no path, no
    /// task body.</summary>
    [Fact]
    public void The_user_message_carries_only_the_fields_the_decision_lets_leave()
    {
        using var document = JsonDocument.Parse(AzureFoundryTriagePrompt.AdviceUser(Item(), Context()));
        var root = document.RootElement;

        Assert.Equal(
            ["item", "openTasks", "otherInboxItems", "repositories", "lists"],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            ["id", "title", "notes", "link", "kind", "source", "person", "tags", "repositories"],
            root.GetProperty("item").EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            ["id", "title", "repositories", "status"],
            root.GetProperty("openTasks")[0].EnumerateObject().Select(property => property.Name));

        // The other captures are compared by title, not read: their notes and
        // links stay on the machine.
        var other = root.GetProperty("otherInboxItems")[0];
        Assert.Equal(["id", "title", "repositories", "status"], other.EnumerateObject().Select(property => property.Name));
        Assert.Equal("unprocessed", other.GetProperty("status").GetString());
        Assert.DoesNotContain("It started after the deploy.", root.GetProperty("otherInboxItems").GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_pass_is_asked_with_the_pass_prompt_and_every_item()
    {
        var client = new StubTriageClient { Answer = "{}" };

        await Advisor(client).ProposePassAsync([Item(), Item(OtherId, "Login button misaligned")], Context(), TestContext.Current.CancellationToken);

        var (system, user) = Assert.Single(client.Requests);
        Assert.Equal(AzureFoundryTriagePrompt.PassText, system);
        using var document = JsonDocument.Parse(user);
        Assert.Equal(2, document.RootElement.GetProperty("items").GetArrayLength());
    }

    // --- Reading the cards ----------------------------------------------------------

    [Fact]
    public async Task A_canned_advice_answer_is_read_into_both_cards()
    {
        var client = new StubTriageClient
        {
            Answer = $$"""
                {
                  "duplicate": { "kind": "task", "id": "{{TaskId}}", "reason": "It asks for the same fix." },
                  "plan": { "title": "Login work", "itemIds": ["{{ItemId}}", "{{OtherId}}", "not-a-guid"], "reason": "Both are about login." },
                  "repositories": ["acme/web", ""]
                }
                """,
        };

        var advice = (await Advisor(client).AdviseAsync(Item(), Context(), TestContext.Current.CancellationToken)).Value;

        Assert.Equal(ItemId, advice.ItemId);
        Assert.Equal(new InboxTriageDuplicateDto(InboxTriageTargetKind.Task, TaskId, "Fix the login page", "It asks for the same fix."), advice.Duplicate);
        Assert.Equal("Login work", advice.Plan?.Title);
        Assert.Equal([ItemId, OtherId], advice.Plan?.ItemIds);
        Assert.Equal(["acme/web"], advice.Repositories);
    }

    [Fact]
    public async Task Null_cards_and_prose_around_the_object_read_as_no_cards()
    {
        var client = new StubTriageClient { Answer = "Here you go:\n{ \"duplicate\": null, \"plan\": null, \"repositories\": [] }\nHope that helps." };

        var result = await Advisor(client).AdviseAsync(Item(), Context(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HasCards);
    }

    [Fact]
    public async Task A_duplicate_with_an_unknown_kind_is_dropped()
    {
        var client = new StubTriageClient { Answer = $$"""{ "duplicate": { "kind": "epic", "id": "{{TaskId}}", "reason": "x" } }""" };

        var advice = (await Advisor(client).AdviseAsync(Item(), Context(), TestContext.Current.CancellationToken)).Value;

        Assert.Null(advice.Duplicate);
    }

    [Theory]
    [InlineData("I could not find anything.")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{ not json }")]
    public async Task An_answer_that_is_not_a_json_object_fails(string answer)
    {
        var result = await Advisor(new StubTriageClient { Answer = answer }).AdviseAsync(Item(), Context(), TestContext.Current.CancellationToken);

        Assert.Equal("inbox.triage.failed", result.Error.Code);
        Assert.Equal(AzureFoundryInboxTriageAdvisor.UnreadableMessage, result.Error.Message);
    }

    // --- Reading the pass -----------------------------------------------------------

    [Fact]
    public async Task A_canned_pass_answer_is_read_into_every_kind_of_proposal()
    {
        var client = new StubTriageClient
        {
            Answer = $$"""
                {
                  "plans": [ { "title": "Login work", "itemIds": ["{{ItemId}}", "{{OtherId}}"], "repositories": ["acme/web"], "reason": "Both login.", "confidence": 0.8 } ],
                  "duplicates": [
                    { "itemId": "{{ItemId}}", "kind": "task", "targetId": "{{TaskId}}", "action": "merge-into-task", "reason": "Same.", "confidence": 0.9 },
                    { "itemId": "{{OtherId}}", "kind": "item", "targetId": "{{ItemId}}", "action": "keep-one-attach-other", "reason": "Same.", "confidence": 0.7 },
                    { "itemId": "{{OtherId}}", "kind": "item", "targetId": "{{ItemId}}", "action": "shrug", "reason": "x", "confidence": 0.7 }
                  ],
                  "routes": [
                    { "itemId": "{{ItemId}}", "entryType": "Prompt", "repositories": ["acme/web"], "reason": "Code.", "confidence": 0.4 },
                    { "itemId": "{{ItemId}}", "entryType": "epic", "repositories": [], "reason": "x", "confidence": 0.4 }
                  ],
                  "filings": [ { "itemId": "{{OtherId}}", "listId": "{{ListId}}", "reason": "Reading.", "confidence": 2 } ],
                  "archives": [ { "itemId": "{{OtherId}}", "reason": "Noise." } ],
                  "unplaced": ["{{OtherId}}", "nope"]
                }
                """,
        };

        var pass = (await Advisor(client).ProposePassAsync([Item(), Item(OtherId, "Login button misaligned")], Context(), TestContext.Current.CancellationToken)).Value;

        var plan = Assert.Single(pass.Plans);
        Assert.Equal(("Login work", "Both login.", 0.8), (plan.Title, plan.Reason, plan.Confidence));
        Assert.Equal([ItemId, OtherId], plan.ItemIds);
        Assert.Equal(["acme/web"], plan.Repositories);
        Assert.Equal(
            [InboxDuplicateAction.MergeIntoTask, InboxDuplicateAction.KeepOneAttachOther],
            pass.Duplicates.Select(pair => pair.Action));
        Assert.Equal(InboxTriageTargetKind.InboxItem, pass.Duplicates[1].TargetKind);
        var route = Assert.Single(pass.Routes);
        Assert.Equal(("prompt", 0.4), (route.EntryType, route.Confidence));
        Assert.Equal(1, Assert.Single(pass.Filings).Confidence);
        Assert.Equal(0, Assert.Single(pass.Archives).Confidence);
        Assert.Equal([OtherId], pass.Unplaced);
    }

    [Fact]
    public async Task A_fenced_empty_pass_reads_as_nothing_proposed()
    {
        var client = new StubTriageClient { Answer = "```json\n{}\n```" };

        var pass = (await Advisor(client).ProposePassAsync([Item()], Context(), TestContext.Current.CancellationToken)).Value;

        Assert.Empty(pass.Plans);
        Assert.Empty(pass.Routes);
        Assert.Empty(pass.Unplaced);
    }

    // --- Failures -----------------------------------------------------------------

    [Fact]
    public async Task A_client_failure_comes_back_as_a_failed_result_carrying_the_clients_words()
    {
        var client = new StubTriageClient { Failure = new AzureFoundryException("Azure Foundry returned 503: busy") };

        var result = await Advisor(client).AdviseAsync(Item(), Context(), TestContext.Current.CancellationToken);

        Assert.Equal("inbox.triage.failed", result.Error.Code);
        Assert.Equal("Azure Foundry returned 503: busy", result.Error.Message);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
    }

    [Fact]
    public async Task An_unreachable_endpoint_and_a_timeout_come_back_as_failed_results()
    {
        var unreachable = await Advisor(new StubTriageClient { Failure = new HttpRequestException("refused") })
            .ProposePassAsync([Item()], Context(), TestContext.Current.CancellationToken);
        var timedOut = await Advisor(new StubTriageClient { Failure = new TaskCanceledException("timeout") })
            .ProposePassAsync([Item()], Context(), TestContext.Current.CancellationToken);

        Assert.Equal("Could not reach Azure Foundry: refused", unreachable.Error.Message);
        Assert.Equal("Azure Foundry did not answer before the request timed out.", timedOut.Error.Message);
    }

    [Fact]
    public async Task The_callers_cancellation_propagates()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var client = new StubTriageClient { Failure = new OperationCanceledException(cancellation.Token) };

        await Assert.ThrowsAsync<OperationCanceledException>(() => Advisor(client).AdviseAsync(Item(), Context(), cancellation.Token));
    }

    // --- Over the wire --------------------------------------------------------------

    [Fact]
    public async Task The_chat_client_sends_both_messages_to_the_deployment_and_unfences_the_answer()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = "```json\n{ \"repositories\": [] }\n```" } } } }),
                Encoding.UTF8,
                "application/json"),
        });
        var settings = new AzureFoundrySettingsStore(NewSettingsPath());
        settings.SetConnection("https://foundry.example.com", "chat", "secret", "2024-10-21");
        var client = new AzureFoundryChatClient(new HttpClient(handler), settings);

        var answer = await client.CompleteTriageAsync("system words", "user words", TestContext.Current.CancellationToken);

        Assert.Equal("{ \"repositories\": [] }", answer);
        Assert.Equal("https://foundry.example.com/openai/deployments/chat/chat/completions?api-version=2024-10-21", handler.Request!.RequestUri!.ToString());
        using var document = JsonDocument.Parse(handler.Body!);
        var messages = document.RootElement.GetProperty("messages");
        Assert.Equal("system words", messages[0].GetProperty("content").GetString());
        Assert.Equal("user words", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task The_chat_client_asks_nothing_when_foundry_is_not_configured()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new AzureFoundryChatClient(new HttpClient(handler), new AzureFoundrySettingsStore(NewSettingsPath()));

        var ex = await Assert.ThrowsAsync<AzureFoundryException>(() => client.CompleteTriageAsync("s", "u", TestContext.Current.CancellationToken));

        Assert.Equal("Configure Azure Foundry in Settings to use AI triage.", ex.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    private AzureFoundryInboxTriageAdvisor Advisor(StubTriageClient client)
    {
        var settings = new AzureFoundrySettingsStore(NewSettingsPath());
        settings.SetConnection("https://foundry.example.com", "chat", "secret", "2024-10-21");
        return new AzureFoundryInboxTriageAdvisor(client, settings);
    }

    private static InboxTriageItemDto Item(Guid? id = null, string title = "Login page returns a server error") =>
        new(id ?? ItemId, title, "It started after the deploy.", "https://example.com/bug", "article", "mobile", "Ann", ["auth"], ["acme/web"]);

    private static InboxTriageContextDto Context() => new(
        [new InboxTriageTaskDto(TaskId, "Fix the login page", ["acme/web"], "ready")],
        [Item(OtherId, "Login button misaligned")],
        ["acme/web", "acme/api"],
        [new InboxTriageListDto(ListId, "Reading")]);

    private string NewSettingsPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "backlog-foundry-triage", Guid.NewGuid().ToString("n"), "azure-foundry.json");
        _paths.Add(path);
        return path;
    }

    private sealed class StubTriageClient : IAzureFoundryTriageClient
    {
        public string Answer { get; init; } = "{}";

        public Exception? Failure { get; init; }

        public List<(string System, string User)> Requests { get; } = [];

        public Task<string> CompleteTriageAsync(string system, string user, CancellationToken cancellationToken = default)
        {
            Requests.Add((system, user));
            if (Failure is not null) throw Failure;
            return Task.FromResult(Answer);
        }
    }
}
