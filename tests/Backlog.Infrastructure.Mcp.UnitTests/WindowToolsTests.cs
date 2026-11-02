using System.ComponentModel;
using System.Reflection;

using Backlog.SharedKernel;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// <c>open_window</c> (local ADR 0043 §7): what it opens, what it links to, and what it
/// refuses before doing either.
/// </summary>
public class WindowToolsTests
{
    [Fact]
    public async Task Desktop_opens_one_native_window_and_answers_the_backlog_link()
    {
        var host = new FakeSurfaceWindows();
        var tools = new WindowTools(FakeSurfaceWindows.Catalog(), host);

        var answer = await tools.OpenAsync("tasks", "desktop", plan: "+shell-pop-out-windows");

        Assert.True(answer.Opened);
        Assert.Equal("tasks", answer.Surface);
        Assert.Equal("desktop", answer.Target);
        Assert.Null(answer.Url);
        Assert.Equal("backlog://window/tasks?plan=shell-pop-out-windows", answer.BacklogLink);

        var (surfaceId, query) = Assert.Single(host.Opened);
        Assert.Equal("tasks", surfaceId);
        Assert.Equal(new Dictionary<string, string> { ["plan"] = "shell-pop-out-windows" }, query);
        Assert.Empty(host.Linked);
    }

    [Fact]
    public async Task Every_desktop_call_opens_another_window()
    {
        var host = new FakeSurfaceWindows();
        var tools = new WindowTools(FakeSurfaceWindows.Catalog(), host);

        await tools.OpenAsync("devbook", "desktop");
        await tools.OpenAsync("devbook", "desktop");

        Assert.Equal(2, host.Opened.Count);
    }

    [Fact]
    public async Task Web_answers_the_tokenized_link_and_opens_nothing()
    {
        var host = new FakeSurfaceWindows();
        var tools = new WindowTools(FakeSurfaceWindows.Catalog(), host);

        var answer = await tools.OpenAsync("devbook", "web", chapter: ".devbook/arc42/adr/0043-a-surface-can-open-in-its-own-window.md", scope: "JSdotNet/Backlog");

        Assert.False(answer.Opened);
        Assert.Empty(host.Opened);
        Assert.Equal(
            "http://localhost:5757/window/devbook?chapter=.devbook%2Farc42%2Fadr%2F0043-a-surface-can-open-in-its-own-window.md&scope=JSdotNet%2FBacklog&t=token",
            answer.Url);
        Assert.Equal(
            "backlog://window/devbook?chapter=.devbook%2Farc42%2Fadr%2F0043-a-surface-can-open-in-its-own-window.md&scope=JSdotNet%2FBacklog",
            answer.BacklogLink);
    }

    [Fact]
    public async Task The_link_carries_the_keys_in_the_order_the_record_lists_them()
    {
        var tools = new WindowTools(FakeSurfaceWindows.Catalog(), new FakeSurfaceWindows());

        var answer = await tools.OpenAsync("tasks", "web", entry: "8cb393df-7bf7-4573-b1c1-ba09882ca70d", plan: "shell-pop-out-windows");

        Assert.Equal(
            "backlog://window/tasks?plan=shell-pop-out-windows&entry=8cb393df-7bf7-4573-b1c1-ba09882ca70d",
            answer.BacklogLink);
    }

    [Fact]
    public async Task Scope_brain_opens_a_brain_note()
    {
        var tools = new WindowTools(FakeSurfaceWindows.Catalog(), new FakeSurfaceWindows());

        var answer = await tools.OpenAsync("devbook", "desktop", chapter: "notes/today.md", scope: "brain");

        Assert.Equal("backlog://window/devbook?chapter=notes%2Ftoday.md&scope=brain", answer.BacklogLink);
    }

    [Fact]
    public async Task A_host_without_native_windows_says_so_and_still_answers_the_link()
    {
        var tools = new WindowTools(FakeSurfaceWindows.Catalog(), new FakeSurfaceWindows(nativeWindows: false));

        var answer = await tools.OpenAsync("board", "desktop");

        Assert.False(answer.Opened);
        Assert.Contains("No window was opened for Board", answer.Answer, StringComparison.Ordinal);
        Assert.Equal("backlog://window/board", answer.BacklogLink);
    }

    [Fact]
    public async Task Web_while_nothing_serves_the_pages_answers_no_url()
    {
        var host = new FakeSurfaceWindows(listenerPort: null);
        var tools = new WindowTools(FakeSurfaceWindows.Catalog(), host);

        var answer = await tools.OpenAsync("tasks", "web");

        Assert.Null(answer.Url);
        Assert.Equal("backlog://window/tasks", answer.BacklogLink);
        Assert.Empty(host.Linked);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData(ShellSurface.ProposalsId)]
    [InlineData("roadmap")]
    [InlineData("")]
    public async Task A_surface_the_catalog_does_not_offer_is_not_available_and_lists_what_is(string surface)
    {
        var features = new FakeAppFeatureSettings(FakeSurfaceWindows.RoadmapFeature);
        features.SetEnabled(FakeSurfaceWindows.RoadmapFeature, enabled: false);
        var host = new FakeSurfaceWindows();
        var tools = new WindowTools(FakeSurfaceWindows.Catalog(features), host);

        foreach (var target in new[] { "desktop", "web" })
        {
            var refusal = await Assert.ThrowsAsync<McpException>(() => tools.OpenAsync(surface, target));

            Assert.Contains("not available", refusal.Message, StringComparison.Ordinal);
            Assert.Contains("Available: tasks, board, devbook", refusal.Message, StringComparison.Ordinal);
        }

        Assert.Empty(host.Opened);
        Assert.Empty(host.Linked);
    }

    [Fact]
    public async Task A_key_the_surface_did_not_register_is_refused_before_anything_opens()
    {
        var host = new FakeSurfaceWindows();
        var tools = new WindowTools(FakeSurfaceWindows.Catalog(), host);

        var refusal = await Assert.ThrowsAsync<McpException>(() => tools.OpenAsync("board", "web", entry: "8cb393df-7bf7-4573-b1c1-ba09882ca70d"));

        Assert.Contains("Board does not take entry", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(host.Linked);
    }

    [Theory]
    [InlineData("JSdotNet/Backlog", null)]
    [InlineData("not a repo", "chapter.md")]
    [InlineData("a/b/c", "chapter.md")]
    public async Task A_scope_without_its_chapter_or_out_of_shape_is_refused(string scope, string? chapter)
    {
        var host = new FakeSurfaceWindows();
        var tools = new WindowTools(FakeSurfaceWindows.Catalog(), host);

        await Assert.ThrowsAsync<McpException>(() => tools.OpenAsync("devbook", "desktop", chapter: chapter, scope: scope));
        Assert.Empty(host.Opened);
    }

    [Fact]
    public async Task A_plan_that_is_only_its_sigil_is_refused()
    {
        var host = new FakeSurfaceWindows();
        var tools = new WindowTools(FakeSurfaceWindows.Catalog(), host);

        await Assert.ThrowsAsync<McpException>(() => tools.OpenAsync("tasks", "desktop", plan: "+"));
        Assert.Empty(host.Opened);
    }

    [Theory]
    [InlineData("window")]
    [InlineData("")]
    public async Task A_target_that_is_neither_desktop_nor_web_is_refused(string target)
    {
        var host = new FakeSurfaceWindows();
        var tools = new WindowTools(FakeSurfaceWindows.Catalog(), host);

        await Assert.ThrowsAsync<McpException>(() => tools.OpenAsync("tasks", target));
        Assert.Empty(host.Opened);
        Assert.Empty(host.Linked);
    }

    /// <summary>A model reads the description to decide how to call the tool, so it has
    /// to say that only <c>desktop</c> opens a window — or every call opens one.</summary>
    [Fact]
    public void The_description_says_only_desktop_opens_a_window()
    {
        var description = typeof(WindowTools)
            .GetMethod(nameof(WindowTools.OpenAsync), BindingFlags.Public | BindingFlags.Instance)!
            .GetCustomAttribute<DescriptionAttribute>()!
            .Description;

        Assert.Contains("With target \"web\" it opens nothing", description, StringComparison.Ordinal);
        Assert.Contains("Only with target \"desktop\" does it open a window", description, StringComparison.Ordinal);
    }

    /// <summary>No flag gates the tool (local ADR 0043 §7): it is listed with every
    /// feature off.</summary>
    [Fact]
    public void Open_window_is_exposed_with_every_feature_off()
    {
        var everythingOff = new FakeAppFeatureSettings([.. BacklogMcpTools.Groups.Select(group => group.FeatureKey).OfType<string>()]);
        foreach (var key in everythingOff.Enabled.ToList()) everythingOff.SetEnabled(key, enabled: false);

        Assert.True(BacklogMcpTools.IsExposed(WindowTools.OpenWindow, everythingOff));
        Assert.True(BacklogMcpTools.IsCallable(WindowTools.OpenWindow, everythingOff));
    }
}
