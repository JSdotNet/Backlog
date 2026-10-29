namespace Backlog.SharedKernel.UnitTests;

/// <summary>
/// The addresses a surface opens at (local ADR 0041 §6, §7): <c>/window/{surfaceId}</c>
/// and <c>backlog://window/{surfaceId}?…</c>, each key and value escaped, in the order
/// given.
/// </summary>
public class SurfaceLinkTests
{
    [Fact]
    public void A_surface_without_a_query_is_its_path_alone()
    {
        Assert.Equal("/window/roadmap", SurfaceLink.Path("roadmap"));
        Assert.Equal("backlog://window/roadmap", SurfaceLink.Protocol("roadmap"));
    }

    [Fact]
    public void The_protocol_link_carries_the_record_s_keys_in_order_and_escaped()
    {
        var link = SurfaceLink.Protocol("devbook", [new("chapter", ".devbook/arc42/a b.md"), new("scope", "JSdotNet/Backlog")]);

        Assert.Equal("backlog://window/devbook?chapter=.devbook%2Farc42%2Fa%20b.md&scope=JSdotNet%2FBacklog", link);
    }

    [Fact]
    public void The_protocol_link_is_a_uri_whose_host_is_window()
    {
        var uri = new Uri(SurfaceLink.Protocol("tasks", [new("plan", "shell-pop-out-windows"), new("entry", "8cb393df")]));

        Assert.Equal(SurfaceLink.Scheme, uri.Scheme);
        Assert.Equal("window", uri.Host);
        Assert.Equal("/tasks", uri.AbsolutePath);
        Assert.Equal("?plan=shell-pop-out-windows&entry=8cb393df", uri.Query);
    }

    [Fact]
    public void A_blank_surface_is_refused()
    {
        Assert.ThrowsAny<ArgumentException>(() => SurfaceLink.Path(" "));
    }
}
