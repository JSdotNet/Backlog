using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// A task's waiting-for as a session reads it: <c>list_entries</c> and
/// <c>read_item</c> both name who the entry waits on and the follow-up date
/// (local ADR 0054 §4).
/// </summary>
public class WaitingForToolsTests
{
    private static readonly TasksRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");

    private static readonly WaitingFor Roofer = new("Jansen Roofing", new DateOnly(2026, 11, 20));

    [Fact]
    public async Task List_entries_names_who_an_entry_waits_on_and_when_to_follow_up()
    {
        var entries = new FakeTaskItems(
            Entries.Entry("Fix the roof", order: 1, repoIds: ["JSdotNet/Backlog"]) with { WaitingFor = Roofer },
            Entries.Entry("Paint the fence", order: 2, repoIds: ["JSdotNet/Backlog"]));

        var tools = new WorkTools(entries, new FakeRepositoryDirectory([Backlog]));

        var answer = await tools.ListEntriesAsync("JSdotNet/Backlog", TestContext.Current.CancellationToken);

        var waiting = answer.Entries[0];
        Assert.Equal("Jansen Roofing", waiting.WaitingFor);
        Assert.Equal(new DateOnly(2026, 11, 20), waiting.FollowUpOn);

        Assert.Null(answer.Entries[1].WaitingFor);
        Assert.Null(answer.Entries[1].FollowUpOn);
    }

    [Fact]
    public async Task Read_item_carries_the_tokens_in_the_markdown_and_names_them_beside_it()
    {
        var id = Guid.NewGuid();
        var entry = Entries.Entry("Fix the roof", id: id) with { WaitingFor = Roofer };

        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero));
        var tools = new TrackerTools(
            new FakeTaskItems(entry),
            new FakeRepositoryDirectory([Backlog]),
            new FakeDevbookReferenceResolver(),
            clock);

        var answer = await tools.ReadItemAsync(id, TestContext.Current.CancellationToken);

        Assert.Contains("`waiting:Jansen Roofing` `followup:2026-11-20`", answer.Markdown, StringComparison.Ordinal);
        Assert.Equal("Jansen Roofing", answer.WaitingFor);
        Assert.Equal(new DateOnly(2026, 11, 20), answer.FollowUpOn);
    }
}
