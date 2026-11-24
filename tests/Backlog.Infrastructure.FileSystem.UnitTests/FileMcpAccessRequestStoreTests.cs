using Backlog.SharedKernel.Mcp;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The agents' access requests on disk (local ADR 0058 §5): one record per request, answered
/// once, read back as written, and a file that does not parse is never written over.
/// </summary>
public sealed class FileMcpAccessRequestStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _root = new("mcp-access-requests");
    private readonly FakeTimeProvider _clock = new(Now);

    private string FilePath => _root.Combine(FileMcpAccessRequestStore.FileName);

    [Fact]
    public void A_request_is_stored_open_and_read_back_by_a_new_store()
    {
        var store = new FileMcpAccessRequestStore(FilePath, _clock);

        var added = store.Add("g_aaaaaaaaaaaaaaaa", McpAccessRequestKind.Register, "Claude Code", ["tasks:read"], "claude.exe", @"D:\Repos\Backlog");

        Assert.StartsWith(McpAccessRequest.IdPrefix, added.Id, StringComparison.Ordinal);
        var read = Assert.Single(new FileMcpAccessRequestStore(FilePath, _clock).List());
        Assert.Equal(added.Id, read.Id);
        Assert.True(read.IsOpen);
        Assert.Equal(McpAccessRequestKind.Register, read.Kind);
        Assert.Equal(["tasks:read"], read.Scopes);
        Assert.Equal("claude.exe", read.Executable);
        Assert.Equal(@"D:\Repos\Backlog", read.Folder);
        Assert.Equal(Now, read.RequestedAt);
    }

    [Fact]
    public void A_store_whose_folder_does_not_exist_yet_creates_it_on_the_first_add()
    {
        // A folder of the test's own, not yet made: the store makes it, not the fixture.
        var path = _root.Combine("missing", FileMcpAccessRequestStore.FileName);
        var store = new FileMcpAccessRequestStore(path, _clock);

        var added = store.Add("g_aaaaaaaaaaaaaaaa", McpAccessRequestKind.Register, "Claude Code", ["tasks:read"], null, null);

        Assert.True(File.Exists(path));
        Assert.Equal(added.Id, Assert.Single(new FileMcpAccessRequestStore(path, _clock).List()).Id);
    }

    [Fact]
    public void A_request_is_answered_once_at_the_clock()
    {
        var store = new FileMcpAccessRequestStore(FilePath, _clock);
        var added = store.Add("g_aaaaaaaaaaaaaaaa", McpAccessRequestKind.MoreScopes, "Backlog delivery", ["runs:write"], null, null);
        _clock.Advance(TimeSpan.FromMinutes(3));

        var allowed = store.Answer(added.Id, McpAccessRequestState.Allowed, ["runs:write"]);

        Assert.Equal(McpAccessRequestState.Allowed, allowed!.State);
        Assert.Equal(Now.AddMinutes(3), allowed.AnsweredAt);
        Assert.Equal(["runs:write"], allowed.Allowed);
        Assert.Null(store.Answer(added.Id, McpAccessRequestState.Declined, []));
        Assert.Null(store.Answer("r_unknownunknown00", McpAccessRequestState.Declined, []));
    }

    [Fact]
    public void A_decline_keeps_no_allowed_scopes()
    {
        var store = new FileMcpAccessRequestStore(FilePath, _clock);
        var added = store.Add("g_aaaaaaaaaaaaaaaa", McpAccessRequestKind.Register, "Copilot", ["tasks:read"], null, null);

        var declined = store.Answer(added.Id, McpAccessRequestState.Declined, ["tasks:read"]);

        Assert.Empty(declined!.Allowed);
    }

    [Fact]
    public void Changed_is_raised_for_an_add_and_an_answer()
    {
        var store = new FileMcpAccessRequestStore(FilePath, _clock);
        var raised = 0;
        store.Changed += () => raised++;

        var added = store.Add("g_aaaaaaaaaaaaaaaa", McpAccessRequestKind.Register, "Copilot", ["tasks:read"], null, null);
        store.Answer(added.Id, McpAccessRequestState.Declined, []);

        Assert.Equal(2, raised);
    }

    [Fact]
    public void A_file_that_does_not_parse_reads_as_no_requests_and_is_not_written_over()
    {
        File.WriteAllText(FilePath, "{ \"requests\": [ ,");
        var store = new FileMcpAccessRequestStore(FilePath, _clock);

        Assert.Empty(store.List());
        Assert.Throws<UnreadableMcpAccessRequestFileException>(() =>
            store.Add("g_aaaaaaaaaaaaaaaa", McpAccessRequestKind.Register, "Copilot", ["tasks:read"], null, null));
        Assert.Equal("{ \"requests\": [ ,", File.ReadAllText(FilePath));
    }

    public void Dispose() => _root.Dispose();
}
