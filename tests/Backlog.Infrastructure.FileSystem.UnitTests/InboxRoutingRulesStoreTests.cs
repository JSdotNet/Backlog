using Backlog.Modules.Inbox.Abstractions;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The Inbox routing rules on disk: nothing before anybody writes a rule, the
/// rules again after a restart, a line that does not read changing nothing, and
/// a file broken by hand losing only the rule it broke.
/// </summary>
public sealed class InboxRoutingRulesStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "inbox-routing-rules-store-tests-" + Guid.NewGuid().ToString("N"));

    public InboxRoutingRulesStoreTests() => Directory.CreateDirectory(_root);

    private string SettingsFile => Path.Combine(_root, "inbox-routing-rules.json");

    private InboxRoutingRulesStore Store() => new(SettingsFile);

    [Fact]
    public void An_untouched_store_has_no_rules()
    {
        Assert.Empty(Store().Current);
        Assert.False(File.Exists(SettingsFile));
    }

    [Fact]
    public void Rules_written_are_in_use_at_once_and_after_a_restart()
    {
        var store = Store();
        var changed = 0;
        store.Changed += () => changed++;

        Assert.Null(store.SetRules("#design => JSdotNet/Design\n@alice => JSdotNet/Alice"));

        InboxRoutingRule[] expected = [new("#design", "JSdotNet/Design"), new("@alice", "JSdotNet/Alice")];
        Assert.Equal(expected, store.Current);
        Assert.Equal(1, changed);
        Assert.Equal(expected, Store().Current);
    }

    [Fact]
    public void A_line_that_does_not_read_is_refused_and_the_rules_in_use_stay()
    {
        var store = Store();
        store.SetRules("#design => JSdotNet/Design");

        var error = store.SetRules("#design => JSdotNet/Design\n#oops JSdotNet/Oops");

        Assert.NotNull(error);
        Assert.Contains("Line 2", error, StringComparison.Ordinal);
        Assert.Equal([new InboxRoutingRule("#design", "JSdotNet/Design")], store.Current);
        Assert.Equal([new InboxRoutingRule("#design", "JSdotNet/Design")], Store().Current);
    }

    [Fact]
    public void Writing_the_same_rules_again_changes_nothing()
    {
        var store = Store();
        store.SetRules("#design => JSdotNet/Design");
        var changed = 0;
        store.Changed += () => changed++;

        Assert.Null(store.SetRules("  #design   =>  JSdotNet/Design  \n"));
        Assert.Equal(0, changed);
    }

    [Fact]
    public void A_rule_broken_by_hand_is_dropped_and_the_rest_are_read()
    {
        File.WriteAllText(SettingsFile, """
            {
              "rules": [
                { "pattern": "#design", "repository": "JSdotNet/Design" },
                { "pattern": "", "repository": "JSdotNet/Nothing" },
                { "pattern": "youtube", "repository": "not-a-repository" }
              ]
            }
            """);

        Assert.Equal([new InboxRoutingRule("#design", "JSdotNet/Design")], Store().Current);
    }

    [Fact]
    public void A_file_that_is_not_json_reads_as_no_rules()
    {
        File.WriteAllText(SettingsFile, "{ not json");

        Assert.Empty(Store().Current);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
