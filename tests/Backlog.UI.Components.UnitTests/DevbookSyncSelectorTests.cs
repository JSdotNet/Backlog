namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The direction beside the status: the picker where the block may state one and
/// the host can write it, the badge where it cannot, and the bare word a record
/// states where nothing places it — and the record asking its pane's scope.
/// </summary>
public sealed class DevbookSyncSelectorTests
{
    private static readonly DevbookSyncState Inherited =
        new(DevbookSyncLevel.Unit, null, new DevbookSyncDirection("push", DevbookSyncLevel.Context));

    [Fact]
    public void Editable_it_is_a_select_wearing_the_direction_in_force_with_an_inherit_entry_naming_its_source()
    {
        using var context = new BunitContext();

        var picker = context.Render<DevbookSyncSelector>(parameters => parameters
            .Add(p => p.State, Inherited)
            .Add(p => p.Editable, true));

        var wrapper = picker.Find("label");
        Assert.Contains("badge--sync-push", wrapper.GetAttribute("class"), StringComparison.Ordinal);

        var select = picker.Find("select");
        Assert.Equal(string.Empty, select.GetAttribute("value"));

        var options = picker.FindAll("option").Select(option => (option.GetAttribute("value"), option.TextContent)).ToList();
        Assert.Equal(("", "sync push · from context"), options[0]);
        Assert.Equal(["push", "pull", "sync", "report", "off"], options.Skip(1).Select(option => option.Item1));
    }

    [Fact]
    public void Picking_sends_the_direction_and_the_empty_entry_sends_null()
    {
        using var context = new BunitContext();
        var picked = new List<string?>();

        var picker = context.Render<DevbookSyncSelector>(parameters => parameters
            .Add(p => p.State, Inherited)
            .Add(p => p.Editable, true)
            .Add(p => p.OnChanged, (string? value) => picked.Add(value)));

        picker.Find("select").Change("pull");
        picker.Find("select").Change("");

        Assert.Equal(["pull", null], picked);
    }

    [Fact]
    public void Read_only_it_is_the_badge_with_the_same_words()
    {
        using var context = new BunitContext();

        var badge = context.Render<DevbookSyncSelector>(parameters => parameters.Add(p => p.State, Inherited));

        Assert.Empty(badge.FindAll("select"));
        var span = badge.Find("span");
        Assert.Equal("badge badge--sync badge--sync-push", span.GetAttribute("class"));
        Assert.Equal("sync push · context", span.TextContent);
        Assert.Contains("inherited from the context", span.GetAttribute("title"), StringComparison.Ordinal);
    }

    [Fact]
    public void Placed_nowhere_it_shows_only_what_the_block_states_and_nothing_when_it_states_nothing()
    {
        using var context = new BunitContext();

        var fallback = context.Render<DevbookSyncSelector>(parameters => parameters.Add(p => p.Fallback, "off"));
        Assert.Equal("sync off", fallback.Find("span").TextContent);

        var nothing = context.Render<DevbookSyncSelector>();
        Assert.Equal(string.Empty, nothing.Markup.Trim());
    }

    [Fact]
    public void A_record_inside_a_scope_asks_it_for_its_own_block_and_writes_through_it()
    {
        using var context = new BunitContext();
        var asked = new List<(DevbookMetadataLevel, string?)>();
        var written = new List<(DevbookMetadataLevel, string?, string?)>();
        var scope = new DevbookSyncScope(
            (level, heading) => { asked.Add((level, heading)); return Inherited; },
            (level, heading, direction) => { written.Add((level, heading, direction)); return Task.CompletedTask; });

        var record = context.Render<CascadingValue<DevbookSyncScope>>(parameters => parameters
            .Add(p => p.Value, scope)
            .AddChildContent<MetadataView>(view => view
                .Add(p => p.Metadata, MetadataReader.Parse("type: aggregate"))
                .Add(p => p.Folder, DevbookFolder.Domain)
                .Add(p => p.HeadingText, "Order")));

        Assert.Contains((DevbookMetadataLevel.Chapter, "Order"), asked);

        record.Find("[data-testid='devbook-sync'] select").Change("off");

        Assert.Equal([(DevbookMetadataLevel.Chapter, (string?)"Order", (string?)"off")], written);
    }

    [Fact]
    public void Outside_a_devbook_folder_a_scope_is_not_asked()
    {
        using var context = new BunitContext();
        var scope = new DevbookSyncScope((_, _) => Inherited);

        var record = context.Render<CascadingValue<DevbookSyncScope>>(parameters => parameters
            .Add(p => p.Value, scope)
            .AddChildContent<MetadataView>(view => view.Add(p => p.Metadata, MetadataReader.Parse("status: draft"))));

        Assert.Empty(record.FindAll("[data-testid='devbook-sync']"));
    }
}
