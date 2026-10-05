using Backlog.Modules.Tasks.Abstractions.Connectors;

namespace Backlog.Modules.Tasks.UnitTests.LinkedTasks;

/// <summary>
/// The id a linked task carries is what lets two desktops sync the same
/// repository into one task: the same item must give the same id everywhere and
/// for good, and two items — or one external id at two connectors — must not
/// collide.
/// </summary>
public sealed class LinkedTaskIdsTests
{
    [Fact]
    public void The_same_item_always_gives_the_same_id()
    {
        Assert.Equal(LinkedTaskIds.For("github", "I_1"), LinkedTaskIds.For("github", "I_1"));
    }

    [Fact]
    public void A_different_item_gives_a_different_id()
    {
        Assert.NotEqual(LinkedTaskIds.For("github", "I_1"), LinkedTaskIds.For("github", "I_2"));
    }

    [Fact]
    public void The_same_external_id_at_another_connector_gives_a_different_id()
    {
        Assert.NotEqual(LinkedTaskIds.For("github", "42"), LinkedTaskIds.For("spec-manager", "42"));
    }

    /// <summary>Pinned, not just stable within a process: a scheme that changed
    /// between builds would make every linked task again beside the old one. The
    /// literal is SHA-256 over the UTF-8 of <c>linked:github:I_kwDOAbc123</c>,
    /// truncated to sixteen bytes with the version-8 and variant bits set — the
    /// scheme <c>CaptureIds</c> uses.</summary>
    [Fact]
    public void The_scheme_is_pinned()
    {
        var id = LinkedTaskIds.For("github", "I_kwDOAbc123");

        Assert.Equal(Guid.Parse("27241d6b-4a99-8e00-aa78-3b19c4112a5d"), id);
        Assert.Equal(8, id.Version);
    }

    [Theory]
    [InlineData("", "I_1")]
    [InlineData("github", "")]
    [InlineData("github", "   ")]
    public void A_blank_connector_or_item_is_refused(string connectorId, string externalId)
    {
        Assert.Throws<ArgumentException>(() => LinkedTaskIds.For(connectorId, externalId));
    }
}
