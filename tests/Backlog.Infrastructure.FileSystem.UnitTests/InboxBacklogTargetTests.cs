using Backlog.Infrastructure.FileSystem.Inbox;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The one place an inbox item becomes entry text. What these pin is that the
/// text the adapter writes is text Tasks' own parser reads back to the same
/// facts — title, tags, repository, body — and that the routing rules the port
/// promises (one entry per repository, the item's id as provenance) are what
/// the Tasks facade actually receives.
/// </summary>
public sealed class InboxBacklogTargetTests
{
    private static readonly Guid Item = Guid.Parse("0199a3f2-7c41-7d1a-9d0f-3d2c5e6f7a8b");

    [Fact]
    public void The_composed_text_parses_back_to_the_items_facts()
    {
        var request = new InboxRouteRequestDto(
            Item,
            "Ship the thing",
            "Some notes about it.",
            "https://example.com/spec.pdf",
            ["Deploy", "#infra"],
            ["JSdotNet/Backlog"]);

        var parsed = EntryTextParser.Parse(InboxBacklogTarget.Compose(request, "JSdotNet/Backlog"));

        Assert.Equal("Ship the thing", parsed.Title);
        Assert.Equal(EntryType.Task, parsed.Type);
        Assert.Equal(EntryStatus.Draft, parsed.Status);
        Assert.Equal(["deploy", "infra"], parsed.Tags);
        Assert.Equal(["JSdotNet/Backlog"], parsed.RepoIds);
        Assert.Contains("Some notes about it.", parsed.Body, StringComparison.Ordinal);
        Assert.Contains("Source: https://example.com/spec.pdf", parsed.Body, StringComparison.Ordinal);
        Assert.Empty(parsed.Unreadable ?? []);
    }

    /// <summary>The parser reads line two as the metadata line, so a title with
    /// a line break in it would put half the title where the tokens go and the
    /// tokens into the body. Every whitespace run becomes one space.</summary>
    [Fact]
    public void A_title_with_line_breaks_still_parses_back_as_one_title_line()
    {
        var request = new InboxRouteRequestDto(
            Item,
            "Ship the\nthing\r\n  before   Friday",
            "Notes.",
            null,
            ["deploy"],
            []);

        var parsed = EntryTextParser.Parse(InboxBacklogTarget.Compose(request, null));

        Assert.Equal("Ship the thing before Friday", parsed.Title);
        Assert.Equal(EntryType.Task, parsed.Type);
        Assert.Equal(EntryStatus.Draft, parsed.Status);
        Assert.Equal(["deploy"], parsed.Tags);
        Assert.Equal("Notes.", parsed.Body.Trim());
        Assert.Empty(parsed.Unreadable ?? []);
    }

    /// <summary>An item that arrived with files hands its folder on as the
    /// entry's attachment, and the text parses back to that path whole — a
    /// workspace under a folder with spaces in its name included.</summary>
    [Fact]
    public void The_items_attachment_folder_becomes_the_entrys_attachment()
    {
        var folder = @"C:\Users\Sam Smith\Backlog\_inbox\attachments\" + Item.ToString("D");
        var request = new InboxRouteRequestDto(Item, "Sign the contract", "Due Friday.", null, ["legal"], [], folder);

        var parsed = EntryTextParser.Parse(InboxBacklogTarget.Compose(request, null));

        Assert.Equal(folder, parsed.Attachment?.Path);
        Assert.Equal("Sign the contract", parsed.Title);
        Assert.Equal(["legal"], parsed.Tags);
        Assert.Equal("Due Friday.", parsed.Body.Trim());
        Assert.Empty(parsed.Unreadable ?? []);
    }

    [Fact]
    public void Without_a_repository_no_repo_token_is_written()
    {
        var request = new InboxRouteRequestDto(Item, "Call the dentist", string.Empty, null, [], []);

        var text = InboxBacklogTarget.Compose(request, null);
        var parsed = EntryTextParser.Parse(text);

        Assert.DoesNotContain("repo:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Source:", text, StringComparison.Ordinal);
        Assert.Empty(parsed.RepoIds ?? []);
        Assert.Equal(string.Empty, parsed.Body.Trim());
    }

    [Fact]
    public async Task Each_repository_becomes_its_own_entry_in_rank_order_with_the_items_id()
    {
        var tasks = new RecordingTaskItems(existing: 3);
        var target = new InboxBacklogTarget(tasks, Known());
        var request = new InboxRouteRequestDto(Item, "Ship the thing", string.Empty, null, [], ["JSdotNet/Backlog", "JSdotNet/Other"]);

        var result = await target.CreateTasksAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal(tasks.Saves.Select(save => save.Id), result.Value);

        Assert.Equal([3, 4], tasks.Saves.Select(save => save.Order));
        Assert.All(tasks.Saves, save => Assert.Equal(Item.ToString("D"), save.SourceInboxId));
        Assert.Equal(
            ["JSdotNet/Backlog", "JSdotNet/Other"],
            tasks.Saves.Select(save => EntryTextParser.Parse(save.RawText).RepoIds!.Single()));
    }

    [Fact]
    public async Task No_repository_makes_exactly_one_entry()
    {
        var tasks = new RecordingTaskItems(existing: 0);
        var request = new InboxRouteRequestDto(Item, "Call the dentist", string.Empty, null, [], []);

        var result = await new InboxBacklogTarget(tasks, Known()).CreateTasksAsync(request, TestContext.Current.CancellationToken);

        Assert.Single(result.Value);
        Assert.Equal(0, Assert.Single(tasks.Saves).Order);
    }

    [Fact]
    public async Task A_failure_part_way_names_what_was_already_created()
    {
        var tasks = new RecordingTaskItems(existing: 0) { FailOnSave = 2 };
        var request = new InboxRouteRequestDto(Item, "Ship it", string.Empty, null, [], ["a/one", "a/two"]);

        var result = await new InboxBacklogTarget(tasks, Known()).CreateTasksAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("entry.needs_title", result.Error.Code);
        Assert.Contains("1 of 2", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains(tasks.Saves[0].Id.ToString("D"), result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plan_is_imported_with_the_items_id_and_answers_with_every_entry()
    {
        var tasks = new RecordingTaskItems(existing: 0);

        var result = await new InboxBacklogTarget(tasks, Known()).ImportPlanAsync(
            "# One\n`prompt`\n\n# Two\n`prompt`\n", Item, allowedRepoIds: [], cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);

        var import = Assert.Single(tasks.Imports);
        Assert.Equal(Item.ToString("D"), import.SourceInboxId);
        Assert.Null(import.DefaultRepo);
    }

    /// <summary>The one thing about a drafted plan the adapter checks itself:
    /// Tasks' import registers a repository it has never heard of, so a model
    /// that writes one the item does not name must be stopped before Tasks is
    /// asked at all — no entries, and nothing for the registry to learn.</summary>
    [Fact]
    public async Task A_plan_naming_a_repository_the_item_does_not_have_is_refused_before_tasks_sees_it()
    {
        var tasks = new RecordingTaskItems(existing: 0);

        var result = await new InboxBacklogTarget(tasks, Known()).ImportPlanAsync(
            "# One\n`prompt` `repo:JSdotNet/Backlog`\n\n# Two\n`prompt` `repo:evil/x`\n",
            Item,
            allowedRepoIds: ["JSdotNet/Backlog"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("inbox.plan.unknown_repository", result.Error.Code);
        Assert.Contains("evil/x", result.Error.Message, StringComparison.Ordinal);
        Assert.Empty(tasks.Imports);
        Assert.Empty(tasks.Saves);
    }

    /// <summary>A drafter's order for a batch is read, never imported: each
    /// entry's <c>id:</c> is the item that waits and each <c>after:</c> the item
    /// it waits on, whether written bare or with the <c>/repo</c> a batch uses
    /// for an item with several repositories.</summary>
    [Fact]
    public void A_drafted_order_is_read_as_edges_between_the_items_and_tasks_is_not_asked()
    {
        var tasks = new RecordingTaskItems(existing: 0);
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();
        var plan =
            $"# A\n`prompt` `!draft` `+inbox-batch-1a2b3c4d` `id:{a:D}` `repo:JSdotNet/Backlog`\n\n" +
            $"# B\n`prompt` `!draft` `+inbox-batch-1a2b3c4d` `id:{b:D}/JSdotNet/Backlog` `after:{a:D}`\n\n" +
            $"# C\n`prompt` `!draft` `+inbox-batch-1a2b3c4d` `id:{c:D}` `after:{b:D}/JSdotNet/Backlog` `after:{a:D}` `after:{c:D}`\n";

        var result = new InboxBacklogTarget(tasks, Known()).ReadDraftedOrder(plan, [a, b, c], ["jsdotnet/backlog"]);

        Assert.True(result.IsSuccess);
        Assert.Equal([new InboxBatchEdge(b, a), new InboxBatchEdge(c, b), new InboxBatchEdge(c, a)], result.Value);
        Assert.Empty(tasks.Imports);
        Assert.Empty(tasks.Saves);
    }

    /// <summary>The drafted plan's rule, held for an order too: a repository
    /// none of the batch's items goes to is refused, and so is the whole
    /// answer — none of its edges comes back.</summary>
    [Fact]
    public void A_drafted_order_naming_a_repository_outside_the_batch_is_refused_whole()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var plan =
            $"# A\n`prompt` `id:{a:D}`\n\n" +
            $"# B\n`prompt` `id:{b:D}` `after:{a:D}` `repo:evil/x`\n";

        var result = new InboxBacklogTarget(new RecordingTaskItems(existing: 0), Known()).ReadDraftedOrder(plan, [a, b], ["JSdotNet/Backlog"]);

        Assert.Equal("inbox.order.unknown_repository", result.Error.Code);
        Assert.Contains("evil/x", result.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("step-1", null)]
    [InlineData(null, "step-1")]
    [InlineData(null, "00000000-0000-0000-0000-000000000001")]
    [InlineData("", null)]
    public void A_drafted_order_naming_an_entry_outside_the_batch_is_refused_whole(string? idOverride, string? afterOverride)
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var id = idOverride is null ? $" `id:{b:D}`" : idOverride.Length == 0 ? string.Empty : $" `id:{idOverride}`";
        var after = $" `after:{afterOverride ?? a.ToString("D")}`";
        var plan =
            $"# A\n`prompt` `id:{a:D}`\n\n" +
            $"# B\n`prompt`{id}{after}\n";

        var result = new InboxBacklogTarget(new RecordingTaskItems(existing: 0), Known()).ReadDraftedOrder(plan, [a, b], []);

        Assert.Equal("inbox.order.unknown_item", result.Error.Code);
    }

    [Fact]
    public async Task A_plan_naming_only_the_items_repositories_is_imported_whatever_their_case()
    {
        var tasks = new RecordingTaskItems(existing: 0);

        var result = await new InboxBacklogTarget(tasks, Known()).ImportPlanAsync(
            "# One\n`prompt` `repo:jsdotnet/backlog`\n\n# Two\n`prompt` `repo:JSdotNet/Other`\n",
            Item,
            allowedRepoIds: ["JSdotNet/Backlog", "jsdotnet/other"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Single(tasks.Imports);
    }

    /// <summary>Create plan's half of "the file goes where the work goes": every
    /// task entry the drafted plan makes carries the item's folder, written by
    /// Tasks' own writer so a path with spaces survives, and a <c>plan</c> entry
    /// — a roadmap item, never a task — is left as the model wrote it.</summary>
    [Fact]
    public async Task A_plans_task_entries_carry_the_items_attachment_folder()
    {
        var tasks = new RecordingTaskItems(existing: 0);
        const string folder = @"C:\Users\me\My Documents\_inbox\attachments\item";

        var result = await new InboxBacklogTarget(tasks, Known()).ImportPlanAsync(
            "# The plan\n`plan` `+talk`\n\n# One\n`prompt` `+talk`\n\nDo it.\n\n# Two\n`prompt` `+talk`\n",
            Item,
            allowedRepoIds: [],
            attachmentPath: folder,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);

        var parsed = EntryTextParser.SplitSegments(Assert.Single(tasks.Imports).RawText).Select(EntryTextParser.Parse).ToList();
        Assert.Equal(["The plan", "One", "Two"], parsed.Select(entry => entry.Title));
        Assert.Null(parsed[0].Attachment);
        Assert.All(parsed.Skip(1), entry => Assert.Equal(folder, entry.Attachment?.Path));
        Assert.Equal("Do it.", parsed[1].Body.Trim());
    }

    [Fact]
    public async Task A_plan_without_an_attachment_folder_is_imported_as_drafted()
    {
        var tasks = new RecordingTaskItems(existing: 0);
        const string plan = "# One\n`prompt`\n\n# Two\n`prompt`\n";

        await new InboxBacklogTarget(tasks, Known()).ImportPlanAsync(
            plan, Item, allowedRepoIds: [], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(plan, Assert.Single(tasks.Imports).RawText);
    }

    private static readonly Guid Other = Guid.Parse("0199a3f2-7c41-7d1a-9d0f-000000000002");

    private static readonly Guid Third = Guid.Parse("0199a3f2-7c41-7d1a-9d0f-000000000003");

    private const string BatchTag = "+inbox-batch-1a2b3c4d";

    /// <summary>The repositories the workspace knows in these tests.</summary>
    private static KnownRepositories Known() => new("JSdotNet/Backlog", "JSdotNet/Other", "a/one", "a/two");

    /// <summary>A batch is one document for Tasks' import, and what pins it is
    /// Tasks' own parser: every entry reads back with the batch's plan tag, an
    /// <c>id:</c> naming its item — with the repository on the end when the item
    /// has several, <c>/</c> and all — its one repository, and the rest of what
    /// a single route would have written, attachment included.</summary>
    [Fact]
    public async Task A_batch_is_one_document_whose_entries_parse_back_with_the_tag_the_id_and_the_repository()
    {
        var tasks = new RecordingTaskItems(existing: 0);
        var folder = @"C:\Users\Sam Smith\Backlog\_inbox\attachments\" + Other.ToString("D");
        var request = new InboxBatchRouteRequestDto(
            [
                new InboxRouteRequestDto(Item, "Ship the thing", "Notes.", null, ["deploy"], ["JSdotNet/Backlog", "JSdotNet/Other"]),
                new InboxRouteRequestDto(Other, "Read the contract", string.Empty, null, [], [], folder),
            ],
            BatchTag);

        var result = await new InboxBacklogTarget(tasks, Known()).CreateBatchTasksAsync(request, TestContext.Current.CancellationToken);

        Assert.Null(result.DocumentRefused);
        Assert.Empty(result.Refused);
        var import = Assert.Single(tasks.Imports);
        Assert.Empty(tasks.Saves);

        var parsed = EntryTextParser.SplitSegments(import.RawText).Select(EntryTextParser.Parse).ToList();
        Assert.Equal(3, parsed.Count);
        Assert.All(parsed, entry =>
        {
            Assert.Contains(BatchTag, entry.Tags);
            Assert.Equal(EntryStatus.Draft, entry.Status);
            Assert.Empty(entry.Unreadable ?? []);
        });

        Assert.Equal(
            [$"{Item:D}/JSdotNet/Backlog", $"{Item:D}/JSdotNet/Other", Other.ToString("D")],
            parsed.Select(entry => entry.ImportItemId));
        Assert.Equal(["JSdotNet/Backlog"], parsed[0].RepoIds);
        Assert.Equal(["JSdotNet/Other"], parsed[1].RepoIds);
        Assert.Empty(parsed[2].RepoIds ?? []);
        Assert.Equal(["deploy", SiblingTag, BatchTag], parsed[0].Tags);
        Assert.EndsWith("Notes.", parsed[0].Body.Trim(), StringComparison.Ordinal);
        Assert.Equal("Read the contract", parsed[2].Title);
        Assert.Equal(folder, parsed[2].Attachment?.Path);
    }

    [Fact]
    public async Task A_batch_names_each_entrys_own_item_as_its_source_and_nothing_else()
    {
        var tasks = new RecordingTaskItems(existing: 0);
        var request = new InboxBatchRouteRequestDto(
            [
                new InboxRouteRequestDto(Item, "One", string.Empty, null, [], ["a/one", "a/two"]),
                new InboxRouteRequestDto(Other, "Two", string.Empty, null, [], []),
            ],
            BatchTag);

        await new InboxBacklogTarget(tasks, Known()).CreateBatchTasksAsync(request, TestContext.Current.CancellationToken);

        var import = Assert.Single(tasks.Imports);
        Assert.Null(import.SourceInboxId);
        Assert.Null(import.DefaultRepo);
        Assert.Equal(
            new Dictionary<string, string>
            {
                [$"{Item:D}/a/one"] = Item.ToString("D"),
                [$"{Item:D}/a/two"] = Item.ToString("D"),
                [Other.ToString("D")] = Other.ToString("D"),
            },
            import.SourceInboxIds);
    }

    /// <summary>The import's answer is matched back to items by <c>id:</c>, not
    /// by position — the double answers in reverse — and each item gets only its
    /// own entries, in the order of its repositories.</summary>
    [Fact]
    public async Task Each_item_gets_back_only_its_own_entries_in_repository_order()
    {
        var tasks = new RecordingTaskItems(existing: 0);
        var request = new InboxBatchRouteRequestDto(
            [
                new InboxRouteRequestDto(Item, "One", string.Empty, null, [], ["a/one", "a/two"]),
                new InboxRouteRequestDto(Other, "Two", string.Empty, null, [], []),
            ],
            BatchTag);

        var result = await new InboxBacklogTarget(tasks, Known()).CreateBatchTasksAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal([Item, Other], result.Routed.Select(routed => routed.InboxItemId));
        Assert.Equal(2, result.Routed[0].TaskIds.Count);
        Assert.Single(result.Routed[1].TaskIds);
        Assert.Empty(result.Routed[0].TaskIds.Intersect(result.Routed[1].TaskIds));

        var created = tasks.Created.ToDictionary(entry => entry.ImportItemId!, entry => entry.Id);
        Assert.Equal([created[$"{Item:D}/a/one"], created[$"{Item:D}/a/two"]], result.Routed[0].TaskIds);
    }

    /// <summary>Tasks takes the document whole or not at all; its refusal comes
    /// back as it gave it, beside the items the adapter had already left out,
    /// and nothing is routed.</summary>
    [Fact]
    public async Task A_refusal_from_tasks_comes_back_as_tasks_gave_it_beside_the_items_left_out()
    {
        var refusal = Error.Validation("import.duplicate_item_id", "Two entries both claim it.");
        var tasks = new RecordingTaskItems(existing: 0) { FailImportWith = refusal };
        var request = new InboxBatchRouteRequestDto(
            [
                new InboxRouteRequestDto(Item, "One", string.Empty, null, [], []),
                new InboxRouteRequestDto(Other, "Unknown", string.Empty, null, [], ["gone/away"]),
            ],
            BatchTag);

        var result = await new InboxBacklogTarget(tasks, Known()).CreateBatchTasksAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(refusal, result.DocumentRefused);
        Assert.Empty(result.Routed);
        Assert.Equal(Other, Assert.Single(result.Refused).Id);
    }

    /// <summary>Written already, so not a refusal: the other items are routed
    /// and the one Tasks answered for only in part is named with what it did
    /// make.</summary>
    [Fact]
    public async Task An_item_the_import_answered_for_only_in_part_is_named_with_what_was_made_and_the_rest_are_routed()
    {
        var tasks = new RecordingTaskItems(existing: 0) { LeaveOut = $"{Other:D}/a/two" };
        var request = new InboxBatchRouteRequestDto(
            [
                new InboxRouteRequestDto(Item, "One", string.Empty, null, [], []),
                new InboxRouteRequestDto(Other, "Two", string.Empty, null, [], ["a/one", "a/two"]),
            ],
            BatchTag);

        var result = await new InboxBacklogTarget(tasks, Known()).CreateBatchTasksAsync(request, TestContext.Current.CancellationToken);

        Assert.Null(result.DocumentRefused);
        Assert.Equal([Item], result.Routed.Select(routed => routed.InboxItemId));
        var missing = Assert.Single(result.Refused);
        Assert.Equal(Other, missing.Id);
        Assert.Equal("inbox.batch.item_missing", missing.Error.Code);
        Assert.Contains(tasks.Created.Single(entry => entry.ImportItemId == $"{Other:D}/a/one").Id.ToString("D"), missing.Error.Message, StringComparison.Ordinal);
    }

    /// <summary>One item's notes must not become another entry, or swallow the
    /// next item's heading: a top-level heading splits the document there, and
    /// an unclosed fence hides every heading after it. That item is left out,
    /// named, rather than escaped into something the capture did not say — and
    /// the rest of the batch still goes.</summary>
    [Theory]
    [InlineData("Intro.\n\n# A heading of its own\n\nMore.")]
    [InlineData("Some code:\n\n```\nnever closed")]
    public async Task Notes_that_would_split_or_merge_the_document_leave_only_that_item_out(string body)
    {
        var tasks = new RecordingTaskItems(existing: 0);
        var request = new InboxBatchRouteRequestDto(
            [
                new InboxRouteRequestDto(Item, "A clipped article", body, null, [], []),
                new InboxRouteRequestDto(Other, "Two", string.Empty, null, [], []),
            ],
            BatchTag);

        var result = await new InboxBacklogTarget(tasks, Known()).CreateBatchTasksAsync(request, TestContext.Current.CancellationToken);

        var refused = Assert.Single(result.Refused);
        Assert.Equal(Item, refused.Id);
        Assert.Equal(InboxErrors.BatchItemNotSeparable, refused.Error);
        Assert.Equal([Other], result.Routed.Select(routed => routed.InboxItemId));

        var parsed = EntryTextParser.SplitSegments(Assert.Single(tasks.Imports).RawText).Select(EntryTextParser.Parse).ToList();
        Assert.Equal([Other.ToString("D")], parsed.Select(entry => entry.ImportItemId));
    }

    /// <summary>Tasks' import registers a repository it has never seen; the
    /// single route leaves such a name unresolved. So a batch item naming one
    /// the workspace does not know — removed since it was assigned — is left
    /// out, named, and the registry learns nothing, while the rest still go.</summary>
    [Fact]
    public async Task An_item_naming_a_repository_the_workspace_does_not_know_is_left_out_and_never_registered()
    {
        var tasks = new RecordingTaskItems(existing: 0);
        var repositories = Known();
        var request = new InboxBatchRouteRequestDto(
            [
                new InboxRouteRequestDto(Item, "Known", string.Empty, null, [], ["JSdotNet/Backlog"]),
                new InboxRouteRequestDto(Other, "Removed since", string.Empty, null, [], ["a/one", "gone/away"]),
                new InboxRouteRequestDto(Third, "Untargeted", string.Empty, null, [], []),
            ],
            BatchTag);

        var result = await new InboxBacklogTarget(tasks, repositories).CreateBatchTasksAsync(request, TestContext.Current.CancellationToken);

        var refused = Assert.Single(result.Refused);
        Assert.Equal(Other, refused.Id);
        Assert.Equal("inbox.batch.unknown_repository", refused.Error.Code);
        Assert.Equal("gone/away is not a known repository any more; reassign it or route the item on its own.", refused.Error.Message);
        Assert.Equal([Item, Third], result.Routed.Select(routed => routed.InboxItemId));

        Assert.Empty(repositories.Registered);
        Assert.DoesNotContain("gone/away", Assert.Single(tasks.Imports).RawText, StringComparison.Ordinal);
        Assert.DoesNotContain(Other.ToString("D"), tasks.Imports[0].RawText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_every_item_left_out_tasks_is_not_asked()
    {
        var tasks = new RecordingTaskItems(existing: 0);
        var request = new InboxBatchRouteRequestDto(
            [new InboxRouteRequestDto(Item, "Removed since", string.Empty, null, [], ["gone/away"])],
            BatchTag);

        var result = await new InboxBacklogTarget(tasks, Known()).CreateBatchTasksAsync(request, TestContext.Current.CancellationToken);

        Assert.Empty(tasks.Imports);
        Assert.Empty(result.Routed);
        Assert.Null(result.DocumentRefused);
        Assert.Equal(Item, Assert.Single(result.Refused).Id);
    }

    /// <summary>The registry is asked the way the single route's save asks it:
    /// an id matches without regard to case, and the whitespace the entry text
    /// trims does not make a known repository unknown.</summary>
    [Fact]
    public async Task A_known_repository_is_known_whatever_its_case_or_spacing()
    {
        var tasks = new RecordingTaskItems(existing: 0);
        var request = new InboxBatchRouteRequestDto(
            [new InboxRouteRequestDto(Item, "One", string.Empty, null, [], [" jsdotnet/backlog "])],
            BatchTag);

        var result = await new InboxBacklogTarget(tasks, Known()).CreateBatchTasksAsync(request, TestContext.Current.CancellationToken);

        Assert.Empty(result.Refused);
        Assert.Single(result.Routed);
    }

    // --- Dependencies -----------------------------------------------------------

    /// <summary>A confirmed dependency is an <c>after:</c> token the parser
    /// reads back: one on another item of the batch names every <c>id:</c> that
    /// item went in under, and one on a task is written as given.</summary>
    [Fact]
    public async Task A_dependency_on_a_batch_item_names_every_entry_it_went_in_under_and_one_on_a_task_is_written_as_given()
    {
        var tasks = new RecordingTaskItems(existing: 0);
        var request = new InboxBatchRouteRequestDto(
            [
                new InboxRouteRequestDto(Other, "Set up the pipeline", string.Empty, null, [], ["a/one", "a/two"]),
                new InboxRouteRequestDto(Item, "Deploy the preview", string.Empty, null, [], [], null, [Other], ["0199-earlier", "2f7c0a1e-0000-0000-0000-000000000009"]),
            ],
            BatchTag);

        await new InboxBacklogTarget(tasks, Known()).CreateBatchTasksAsync(request, TestContext.Current.CancellationToken);

        var parsed = EntryTextParser.SplitSegments(Assert.Single(tasks.Imports).RawText).Select(EntryTextParser.Parse).ToList();
        Assert.Equal(3, parsed.Count);
        Assert.All(parsed.Take(2), entry => Assert.Empty(entry.DependsOn ?? []));
        Assert.Equal(
            [$"{Other:D}/a/one", $"{Other:D}/a/two", "0199-earlier", "2f7c0a1e-0000-0000-0000-000000000009"],
            parsed[2].DependsOn);
        Assert.Empty(parsed[2].Unreadable ?? []);
    }

    /// <summary>A dependency on an item the adapter left out of the document
    /// names no entry, and an <c>after:</c> naming none would block the task for
    /// good — so it is not written, and the item that waited still goes.</summary>
    [Fact]
    public async Task A_dependency_on_an_item_left_out_of_the_document_is_dropped()
    {
        var tasks = new RecordingTaskItems(existing: 0);
        var request = new InboxBatchRouteRequestDto(
            [
                new InboxRouteRequestDto(Other, "Unknown repository", string.Empty, null, [], ["gone/away"]),
                new InboxRouteRequestDto(Item, "Waits on it", string.Empty, null, [], [], null, [Other]),
            ],
            BatchTag);

        var result = await new InboxBacklogTarget(tasks, Known()).CreateBatchTasksAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(Other, Assert.Single(result.Refused).Id);
        Assert.Equal(Item, Assert.Single(result.Routed).InboxItemId);
        var entry = EntryTextParser.Parse(Assert.Single(tasks.Imports).RawText);
        Assert.Empty(entry.DependsOn ?? []);
        Assert.DoesNotContain("after:", Assert.Single(tasks.Imports).RawText, StringComparison.Ordinal);
    }

    /// <summary>The general tag the siblings of <see cref="Item"/> share: its id
    /// ends in <c>3d2c5e6f7a8b</c>.</summary>
    private const string SiblingTag = "from-inbox-5e6f7a8b";

    /// <summary>The "Same capture in:" lines of one entry, in order.</summary>
    private static List<string> SameCaptureLines(string rawText) =>
        [.. rawText.Split('\n').Where(line => line.StartsWith("Same capture in:", StringComparison.Ordinal))];

    /// <summary>Two repositories make two siblings: each names the other and
    /// never itself, both carry the shared <c>#</c> tag beside the item's own,
    /// and the person's notes follow the line untouched.</summary>
    [Fact]
    public async Task Two_repositories_make_siblings_that_name_each_other_and_share_a_general_tag()
    {
        var tasks = new RecordingTaskItems(existing: 0);
        var request = new InboxRouteRequestDto(Item, "Ship the thing", "Notes.", null, ["deploy"], ["JSdotNet/Backlog", "JSdotNet/Other"]);

        var result = await new InboxBacklogTarget(tasks, Known()).CreateTasksAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, tasks.Saves.Count);

        Assert.Equal(["Same capture in: JSdotNet/Other — Ship the thing"], SameCaptureLines(tasks.Saves[0].RawText));
        Assert.Equal(["Same capture in: JSdotNet/Backlog — Ship the thing"], SameCaptureLines(tasks.Saves[1].RawText));

        Assert.All(tasks.Saves, save =>
        {
            var parsed = EntryTextParser.Parse(save.RawText);
            Assert.Equal(["deploy", SiblingTag], parsed.MetadataTags);
            Assert.Contains($"`#{SiblingTag}`", save.RawText, StringComparison.Ordinal);
            Assert.DoesNotContain($"+{SiblingTag}", save.RawText, StringComparison.Ordinal);
            Assert.Empty(parsed.SubItems);
            Assert.Empty(parsed.Unreadable ?? []);
            Assert.EndsWith("Notes.", parsed.Body.Trim(), StringComparison.Ordinal);
        });
    }

    /// <summary>Three repositories: each sibling names the other two, in the
    /// item's order, and all three share one tag.</summary>
    [Fact]
    public async Task Three_repositories_make_siblings_that_each_name_the_other_two()
    {
        var tasks = new RecordingTaskItems(existing: 0);
        var request = new InboxRouteRequestDto(Item, "Ship the thing", string.Empty, null, [], ["JSdotNet/Backlog", "JSdotNet/Other", "a/one"]);

        await new InboxBacklogTarget(tasks, Known()).CreateTasksAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(3, tasks.Saves.Count);
        Assert.Equal(
            ["Same capture in: JSdotNet/Other — Ship the thing", "Same capture in: a/one — Ship the thing"],
            SameCaptureLines(tasks.Saves[0].RawText));
        Assert.Equal(
            ["Same capture in: JSdotNet/Backlog — Ship the thing", "Same capture in: a/one — Ship the thing"],
            SameCaptureLines(tasks.Saves[1].RawText));
        Assert.Equal(
            ["Same capture in: JSdotNet/Backlog — Ship the thing", "Same capture in: JSdotNet/Other — Ship the thing"],
            SameCaptureLines(tasks.Saves[2].RawText));
        Assert.All(tasks.Saves, save => Assert.Equal([SiblingTag], EntryTextParser.Parse(save.RawText).MetadataTags));
    }

    /// <summary>A single repository, like no repository, has no siblings: no
    /// line and no tag.</summary>
    [Fact]
    public async Task One_repository_writes_no_sibling_line_and_no_sibling_tag()
    {
        var tasks = new RecordingTaskItems(existing: 0);
        var request = new InboxRouteRequestDto(Item, "Ship the thing", "Notes.", null, ["deploy"], ["JSdotNet/Backlog"]);

        await new InboxBacklogTarget(tasks, Known()).CreateTasksAsync(request, TestContext.Current.CancellationToken);

        var raw = Assert.Single(tasks.Saves).RawText;
        Assert.DoesNotContain("Same capture in:", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("from-inbox-", raw, StringComparison.Ordinal);
        Assert.Equal(["deploy"], EntryTextParser.Parse(raw).MetadataTags);
    }

    /// <summary>The lines are written ahead of the notes, so notes that end in
    /// a <c>##</c> chapter or an open fence cannot swallow them: they stay the
    /// entry's own prose, never a sub-item's note or fenced text.</summary>
    [Theory]
    [InlineData("Intro.\n\n## A chapter\n\nIts note.")]
    [InlineData("Intro.\n\n```\nnever closed")]
    public void Sibling_lines_stay_the_entrys_own_prose_whatever_the_notes_hold(string body)
    {
        var request = new InboxRouteRequestDto(Item, "Ship the thing", body, null, [], ["JSdotNet/Backlog", "JSdotNet/Other"]);

        var text = InboxBacklogTarget.Compose(request, "JSdotNet/Backlog");
        var parent = EntryTextParser.GetParentText(text);

        Assert.Contains("Same capture in: JSdotNet/Other — Ship the thing", parent, StringComparison.Ordinal);
        Assert.True(
            text.IndexOf("Same capture in:", StringComparison.Ordinal) < text.IndexOf("Intro.", StringComparison.Ordinal),
            "The sibling line goes ahead of the notes.");
        Assert.All(EntryTextParser.Parse(text).SubItems, subItem => Assert.DoesNotContain("Same capture in:", subItem.Title, StringComparison.Ordinal));
    }

    /// <summary>A batch writes siblings the same way: each of a two- and a
    /// three-repository item's entries names its others and carries its own
    /// item's general tag beside the batch's plan tag, and a one-repository
    /// item in the same batch gets neither.</summary>
    [Fact]
    public async Task A_batch_links_the_siblings_of_each_item_and_only_its_own()
    {
        var tasks = new RecordingTaskItems(existing: 0);
        var request = new InboxBatchRouteRequestDto(
            [
                new InboxRouteRequestDto(Item, "Ship the thing", "Notes.", null, [], ["JSdotNet/Backlog", "JSdotNet/Other"]),
                new InboxRouteRequestDto(Other, "Read the contract", string.Empty, null, [], ["JSdotNet/Backlog", "JSdotNet/Other", "a/one"]),
                new InboxRouteRequestDto(Third, "Call the dentist", string.Empty, null, [], ["a/two"]),
            ],
            BatchTag);

        var result = await new InboxBacklogTarget(tasks, Known()).CreateBatchTasksAsync(request, TestContext.Current.CancellationToken);

        Assert.Null(result.DocumentRefused);
        Assert.Empty(result.Refused);

        var segments = EntryTextParser.SplitSegments(Assert.Single(tasks.Imports).RawText);
        Assert.Equal(6, segments.Count);
        var parsed = segments.Select(EntryTextParser.Parse).ToList();
        Assert.All(parsed, entry => Assert.Empty(entry.Unreadable ?? []));

        Assert.Equal(["Same capture in: JSdotNet/Other — Ship the thing"], SameCaptureLines(segments[0]));
        Assert.Equal(["Same capture in: JSdotNet/Backlog — Ship the thing"], SameCaptureLines(segments[1]));
        Assert.Equal([SiblingTag, BatchTag], parsed[0].MetadataTags);
        Assert.Equal([SiblingTag, BatchTag], parsed[1].MetadataTags);

        Assert.Equal(
            ["Same capture in: JSdotNet/Backlog — Read the contract", "Same capture in: a/one — Read the contract"],
            SameCaptureLines(segments[3]));
        Assert.All(parsed.Skip(2).Take(3), entry => Assert.Equal(["from-inbox-00000002", BatchTag], entry.MetadataTags));
        Assert.All(segments.Skip(2).Take(3), segment => Assert.Equal(2, SameCaptureLines(segment).Count));

        Assert.Empty(SameCaptureLines(segments[5]));
        Assert.Equal([BatchTag], parsed[5].MetadataTags);
    }

    /// <summary>The workspace's repository registry, as Tasks' port shows it:
    /// a fixed set, and a record of anything a caller asked it to register —
    /// which a batch never may.</summary>
    private sealed class KnownRepositories(params string[] ids) : IRepositoryDirectory
    {
        public List<string> Registered { get; } = [];

        public IReadOnlyList<TasksRepositoryRef> Repositories { get; } =
            [.. ids.Select(id => new TasksRepositoryRef(id.Split('/')[1], id.Split('/')[0], id.Split('/')[1]))];

        public TasksRepositoryRef? Resolve(string name) =>
            Repositories.FirstOrDefault(repository => string.Equals(repository.Id, name, StringComparison.OrdinalIgnoreCase));

        public Result<TasksRepositoryRef> Register(string name)
        {
            Registered.Add(name);
            return new TasksRepositoryRef(name, "x", name);
        }
    }

    /// <summary>The Tasks facade, recording what it is asked and answering the
    /// way the real one would in shape: a saved entry with a fresh id, and an
    /// import result naming one entry per top-level heading.</summary>
    private sealed class RecordingTaskItems(int existing) : ITaskItems
    {
        public List<(Guid Id, string RawText, int Order, string? SourceInboxId)> Saves { get; } = [];

        public List<(string RawText, string? DefaultRepo, string? SourceInboxId, IReadOnlyDictionary<string, string>? SourceInboxIds)> Imports { get; } = [];

        /// <summary>What the import answers with instead of entries.</summary>
        public Error? FailImportWith { get; init; }

        /// <summary>Every entry an import answered with.</summary>
        public List<TaskItemDto> Created { get; } = [];

        /// <summary>An <c>id:</c> the import leaves out of its answer, as if it
        /// had made no entry for it.</summary>
        public string? LeaveOut { get; init; }

        /// <summary>Which save, counted from one, answers with a failure.</summary>
        public int? FailOnSave { get; init; }

        public Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItemDto>>([.. Enumerable.Range(0, existing).Select(i => Entry(Guid.NewGuid(), $"Existing {i}"))]);

        public Task<Result<SavedTaskDto>> SaveFromTextAsync(Guid? id, string rawText, int order, string? sourceInboxId = null, CancellationToken cancellationToken = default)
        {
            if (FailOnSave == Saves.Count + 1)
            {
                return Task.FromResult(Result.Failure<SavedTaskDto>(Error.Validation("entry.needs_title", "An entry needs a title line before it can be saved.")));
            }

            var created = Guid.NewGuid();
            Saves.Add((created, rawText, order, sourceInboxId));
            return Task.FromResult(Result.Success(new SavedTaskDto(Entry(created, EntryTextParser.Parse(rawText).Title))));
        }

        public Task<Result<ImportPlanResultDto>> ImportPlanAsync(string rawText, string? defaultRepo = null, IReadOnlyDictionary<string, string>? repoMatches = null, string? sourceInboxId = null, IReadOnlyDictionary<string, string>? sourceInboxIds = null, bool layOutOnRoadmap = false, CancellationToken cancellationToken = default)
        {
            Imports.Add((rawText, defaultRepo, sourceInboxId, sourceInboxIds));

            if (FailImportWith is { } error) return Task.FromResult(Result.Failure<ImportPlanResultDto>(error));

            // Answered in reverse, so a caller that matched entries to items by
            // position rather than by id would be caught.
            var entries = EntryTextParser.SplitSegments(rawText)
                .Select(EntryTextParser.Parse)
                .Where(parsed => parsed.ImportItemId is null || parsed.ImportItemId != LeaveOut)
                .Select(parsed => Entry(Guid.NewGuid(), parsed.Title) with { ImportItemId = parsed.ImportItemId, RepoIds = parsed.RepoIds })
                .Reverse()
                .ToList();
            Created.AddRange(entries);

            return Task.FromResult(Result.Success(new ImportPlanResultDto(entries.Count, 0, 0, 0, 0, entries)));
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Result<TaskItemDto>> LinkToIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<TaskItemDto>> SetDevbookReferencesAsync(Guid id, IReadOnlyList<string> references, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(0));

        public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(0));

        private static TaskItemDto Entry(Guid id, string title) =>
            new(id, title, string.Empty, EntryType.Task, Priority.Medium, EntryStatus.Draft, null, [], 0, 0, 0, []);
    }
}
