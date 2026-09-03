using Backlog.UI.Components.Data;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Slice 5 leaned on these parameters to keep the app's DOM identical while the
/// markup moved into the library, so what they are worth pinning for is that
/// they <em>replace</em> the library's own class names rather than adding to them.
/// </summary>
public sealed class ClassHookTests
{
    [Fact]
    public void An_empty_state_can_be_dressed_entirely_in_the_hosts_own_names()
    {
        using var context = new BunitContext();

        var empty = context.Render<EmptyState>(parameters => parameters
            .Add(e => e.BaseClass, "backlog-empty")
            .Add(e => e.TitleCssClass, "backlog-empty__head")
            .Add(e => e.DescriptionCssClass, "backlog-empty__body")
            .Add(e => e.ActionCssClass, "backlog-empty__action")
            .Add(e => e.Title, "Nothing captured yet")
            .Add(e => e.Description, "Write the first entry")
            .Add(e => e.Action, "<button type=\"button\">New</button>"));

        Assert.Equal("backlog-empty", empty.Find("div").GetAttribute("class"));
        Assert.NotNull(empty.Find(".backlog-empty__head"));
        Assert.NotNull(empty.Find(".backlog-empty__body"));
        Assert.NotNull(empty.Find(".backlog-empty__action button"));
        Assert.Empty(empty.FindAll(".empty-state"));
    }

    [Fact]
    public void A_null_action_class_drops_the_action_wrapper_entirely()
    {
        using var context = new BunitContext();

        var empty = context.Render<EmptyState>(parameters => parameters
            .Add(e => e.ActionCssClass, null)
            .Add(e => e.Action, "<button type=\"button\">New</button>"));

        Assert.Empty(empty.FindAll(".empty-state__action"));
        Assert.NotNull(empty.Find(".empty-state > button"));
    }

    [Fact]
    public void A_badge_is_its_base_class_plus_the_kind_and_value_it_carries()
    {
        using var context = new BunitContext();

        var badge = context.Render<Badge>(parameters => parameters
            .Add(b => b.Kind, "status")
            .Add(b => b.Slug, "ready"));

        Assert.Equal("badge badge--status badge--status-ready", badge.Find("span").GetAttribute("class"));
    }

    [Fact]
    public void A_badge_with_no_kind_collapses_to_a_single_modifier()
    {
        using var context = new BunitContext();

        var badge = context.Render<Badge>(parameters => parameters
            .Add(b => b.BaseClass, "chip")
            .Add(b => b.Kind, string.Empty)
            .Add(b => b.Slug, "ready"));

        Assert.Equal("chip chip--ready", badge.Find("span").GetAttribute("class"));
    }

    [Fact]
    public void A_status_badge_derives_its_slug_from_the_status_and_can_still_show_other_text()
    {
        using var context = new BunitContext();

        var badge = context.Render<StatusBadge>(parameters => parameters
            .Add(b => b.Status, "In Progress")
            .Add(b => b.Text, "Working on it"));

        var element = badge.Find("span");

        // Anything that is not a letter, a digit or a hyphen is dropped rather
        // than translated, which is what `.badge--status-inprogress` expects.
        Assert.Contains("badge--status-inprogress", element.ClassList);
        Assert.Equal("Working on it", element.TextContent);
    }

    [Fact]
    public void An_unknown_status_falls_back_rather_than_producing_a_class_of_nothing()
    {
        using var context = new BunitContext();

        var badge = context.Render<StatusBadge>(parameters => parameters
            .Add(b => b.Status, "   ")
            .Add(b => b.FallbackSlug, "draft"));

        Assert.Contains("badge--status-draft", badge.Find("span").ClassList);
    }

    [Fact]
    public void A_badge_select_given_no_slug_wears_the_modifier_its_value_spells()
    {
        // The application path, and the majority of callers: a status, a priority
        // or an area whose values *are* the words the stylesheet knows. Nothing
        // has to be told anything for the badge to be right.
        using var context = new BunitContext();

        var select = context.Render<BadgeSelect>(parameters => parameters
            .Add(s => s.CurrentValue, "ready")
            .Add(s => s.Options, [new SelectorOption("ready", "Ready")]));

        Assert.Equal(
            "status-editor badge badge--status badge--status-ready",
            select.Find("label").GetAttribute("class"));
        Assert.Equal("status-editor__select", select.Find("select").GetAttribute("class"));
    }

    [Fact]
    public void A_badge_select_handed_a_slug_wears_that_modifier_instead()
    {
        // For a caller whose vocabulary is not the badge's. `adopted` is a real
        // status in `.tech` and no rule anywhere defines
        // `.badge--status-adopted`, so left to spell its own modifier it would
        // land on plain grey beside a read-only badge that had been told which
        // state it meant. Same meaning as Slug on Badge, and only the modifier
        // moves: `.status-editor` and `.status-editor__select` are how the
        // stylesheet strips the control's chrome and places it on the line.
        using var context = new BunitContext();

        var select = context.Render<BadgeSelect>(parameters => parameters
            .Add(s => s.CurrentValue, "adopted")
            .Add(s => s.Slug, "active")
            .Add(s => s.Options, [new SelectorOption("adopted", "adopted")]));

        Assert.Equal(
            "status-editor badge badge--status badge--status-active",
            select.Find("label").GetAttribute("class"));
        Assert.Equal("status-editor__select", select.Find("select").GetAttribute("class"));
    }

    /// <summary>
    /// An empty slug claims no state and says so in its own class.
    /// <para>
    /// Null and empty are different answers, exactly as on Badge. Null is "not
    /// specified", so the value spells the modifier; empty is a caller saying
    /// there is no state to claim, and claiming one would be painting a verdict
    /// nobody reached.
    /// </para>
    /// <para>
    /// What is new is <c>badge--unset</c>, and it is the difference between "no
    /// state" and "no appearance". A badge's own fill is
    /// <c>--color-background-raised</c>, which is also the fill of the raised
    /// strips an editable badge sits on, so a badge with the kind and no modifier
    /// came out the same colour as the surface behind it: correct markup, and a
    /// control nobody could see. This is the outline that replaces the fill it has
    /// no value for. It is not a state modifier — there is deliberately no
    /// <c>badge--status-*</c> here — which is why the name has no kind in it.
    /// </para>
    /// </summary>
    [Fact]
    public void An_empty_slug_leaves_the_badge_with_no_state_modifier_at_all()
    {
        using var context = new BunitContext();

        var select = context.Render<BadgeSelect>(parameters => parameters
            .Add(s => s.CurrentValue, "adopted")
            .Add(s => s.Slug, string.Empty)
            .Add(s => s.Options, [new SelectorOption("adopted", "adopted")]));

        var classes = select.Find("label").GetAttribute("class");

        Assert.Equal("status-editor badge badge--status badge--unset", classes);
        Assert.DoesNotContain("badge--status-", classes, StringComparison.Ordinal);
    }

    /// <summary>
    /// The knowledge panels reach the same state through their vocabulary, and
    /// that is the third caller rather than a side effect.
    /// <para>
    /// <c>MetadataStatusVocabulary.SlugFor</c> answers the empty string for a
    /// status nobody stated — deliberately, so a file with no status cannot be
    /// painted as <c>draft</c> — and both knowledge panels hand that answer
    /// straight to <c>Slug</c>. So a chapter or a record that states no status now
    /// draws the outline too, which is the same "no value recorded" fact the bulk
    /// bar's status is showing and the same reason it must not read as a filled
    /// pill.
    /// </para>
    /// <para>
    /// Pinned as the chain rather than through a panel, because the chain is what
    /// is load-bearing: the vocabulary answers empty, and empty is what the badge
    /// turns into <c>badge--unset</c>. A panel test would prove one screen; this
    /// proves the contract every screen using that vocabulary depends on.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_status_no_file_stated_draws_the_unset_outline(string? status)
    {
        var vocabulary = KnowledgeStatus.Vocabulary(KnowledgeFolder.Design);

        // The half the knowledge panels rely on: no status, no modifier.
        Assert.Equal(string.Empty, vocabulary.SlugFor(status));

        using var context = new BunitContext();

        var select = context.Render<StatusSelector>(parameters => parameters
            .Add(s => s.CurrentValue, status ?? string.Empty)
            .Add(s => s.Slug, vocabulary.SlugFor(status))
            .Add(s => s.Options, [new SelectorOption("draft", "draft")]));

        var classes = select.Find("label").GetAttribute("class");

        Assert.Contains("badge--unset", classes, StringComparison.Ordinal);
        Assert.DoesNotContain("badge--status-draft", classes, StringComparison.Ordinal);
    }

    /// <summary>And a badge that does have a value never wears it, because there
    /// is a fill for the outline to have replaced.</summary>
    [Fact]
    public void A_badge_select_with_a_value_is_not_marked_unset()
    {
        using var context = new BunitContext();

        var select = context.Render<BadgeSelect>(parameters => parameters
            .Add(s => s.CurrentValue, "ready")
            .Add(s => s.Options, [new SelectorOption("ready", "Ready")]));

        Assert.DoesNotContain("badge--unset", select.Find("label").GetAttribute("class"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A disclosure says it is open, not that it is pressed.
    /// <para>
    /// <c>aria-pressed</c> means "this thing is on". A row that reveals other
    /// controls is not on, it is open, and <c>aria-expanded</c> is the word for
    /// that — with <c>aria-controls</c> naming what appeared, because an
    /// expanded-state with nothing pointing at the thing leaves the reader to go
    /// and find it.
    /// </para>
    /// <para>
    /// Nullable, and the third state is the one worth pinning: most rows in a task
    /// panel reveal nothing, and an <c>aria-expanded="false"</c> on one of those
    /// promises a disclosure that does not exist.
    /// </para>
    /// </summary>
    [Fact]
    public void A_disclosure_row_says_expanded_where_a_toggle_says_pressed()
    {
        using var context = new BunitContext();

        var closed = context.Render<TaskAction>(parameters => parameters
            .Add(a => a.Icon, "◉")
            .Add(a => a.Label, "Classification")
            .Add(a => a.Expanded, false)
            .Add(a => a.Controls, "host-classification")
            .Add(a => a.TestId, "row"));

        var button = closed.Find("[data-testid='row-set']");
        Assert.Equal("false", button.GetAttribute("aria-expanded"));
        Assert.Equal("host-classification", button.GetAttribute("aria-controls"));
        Assert.False(button.HasAttribute("aria-pressed"));

        var open = context.Render<TaskAction>(parameters => parameters
            .Add(a => a.Icon, "◉")
            .Add(a => a.Label, "Classification")
            .Add(a => a.Expanded, true)
            .Add(a => a.TestId, "row"));

        Assert.Equal("true", open.Find("[data-testid='row-set']").GetAttribute("aria-expanded"));

        // A row that opens nothing claims neither, which is the common case and
        // the reason the parameter is nullable.
        var plain = context.Render<TaskAction>(parameters => parameters
            .Add(a => a.Icon, "+")
            .Add(a => a.Label, "Add step")
            .Add(a => a.TestId, "row"));

        var plainButton = plain.Find("[data-testid='row-set']");
        Assert.False(plainButton.HasAttribute("aria-expanded"));
        Assert.False(plainButton.HasAttribute("aria-controls"));
        Assert.False(plainButton.HasAttribute("aria-pressed"));

        // And a toggle still says pressed, which is what it means: My Day is on.
        var toggle = context.Render<TaskAction>(parameters => parameters
            .Add(a => a.Icon, "☀")
            .Add(a => a.Label, "My Day")
            .Add(a => a.Togglable, true)
            .Add(a => a.Set, true)
            .Add(a => a.TestId, "row"));

        var toggleButton = toggle.Find("[data-testid='row-set']");
        Assert.Equal("true", toggleButton.GetAttribute("aria-pressed"));
        Assert.False(toggleButton.HasAttribute("aria-expanded"));
    }

    /// <summary>
    /// The compact field is a modifier over whatever skin the field wears, not a
    /// replacement for it.
    /// <para>
    /// Both panels that wanted a small inline date field were passing a private
    /// class of their own to get one, which is the arrangement
    /// <c>ui-components.instructions.md</c> exists to stop: two hosts naming the
    /// same string to size a control means the size belongs to the library. So it
    /// is added rather than substituted — a host that has replaced the input's
    /// class for an error style of its own still gets the smaller geometry.
    /// </para>
    /// </summary>
    [Fact]
    public void A_compact_field_adds_its_modifier_without_taking_the_skin_away()
    {
        using var context = new BunitContext();

        var plain = context.Render<TextField>(parameters => parameters
            .Add(f => f.Bare, true)
            .Add(f => f.Compact, true)
            .Add(f => f.AriaLabel, "Due date"));

        Assert.Equal("field__input field__input--compact", plain.Find("input").GetAttribute("class"));

        var dressed = context.Render<TextField>(parameters => parameters
            .Add(f => f.Bare, true)
            .Add(f => f.Compact, true)
            .Add(f => f.InputCssClass, "host__own-input")
            .Add(f => f.AriaLabel, "Due date"));

        Assert.Equal("host__own-input field__input--compact", dressed.Find("input").GetAttribute("class"));

        var full = context.Render<TextField>(parameters => parameters
            .Add(f => f.Bare, true)
            .Add(f => f.AriaLabel, "Due date"));

        Assert.Equal("field__input", full.Find("input").GetAttribute("class"));
    }

    [Fact]
    public void A_status_selector_passes_a_slug_through_and_derives_one_without()
    {
        // StatusSelector is a thin wrapper by design, so the only thing worth
        // pinning is that neither half of the contract is lost on the way down.
        using var context = new BunitContext();

        var told = context.Render<StatusSelector>(parameters => parameters
            .Add(s => s.CurrentValue, "adopted")
            .Add(s => s.Slug, "active")
            .Add(s => s.Options, [new SelectorOption("adopted", "adopted")]));

        Assert.Equal(
            "status-editor badge badge--status badge--status-active",
            told.Find("label").GetAttribute("class"));

        var untold = context.Render<StatusSelector>(parameters => parameters
            .Add(s => s.CurrentValue, "done")
            .Add(s => s.Options, [new SelectorOption("done", "Done")]));

        Assert.Equal(
            "status-editor badge badge--status badge--status-done",
            untold.Find("label").GetAttribute("class"));
    }

    [Fact]
    public void A_badge_select_holding_nothing_a_class_can_be_made_of_falls_back()
    {
        // The slug is a class name, so a value that survives filtering as an
        // empty string would leave a dangling `badge--status-`. This used to be
        // written inline here and only checked for a blank value; it is
        // BadgeSlug now, which is the same rule the static badges have always
        // used.
        using var context = new BunitContext();

        var select = context.Render<BadgeSelect>(parameters => parameters
            .Add(s => s.CurrentValue, "!!!")
            .Add(s => s.FallbackSlug, "draft")
            .Add(s => s.Options, []));

        Assert.Contains("badge--status-draft", select.Find("label").ClassList);
    }

    [Fact]
    public void A_button_wearing_a_hosts_base_class_keeps_none_of_ours()
    {
        using var context = new BunitContext();

        var button = context.Render<AppButton>(parameters => parameters
            .Add(b => b.BaseClass, "chip")
            .Add(b => b.Variant, ButtonVariant.Primary)
            .Add(b => b.Size, ButtonSize.Small)
            .Add(b => b.CssClass, "chip--filter"));

        // Every modifier is built on the stem it was given. `btn` brings a
        // padding, a weight and a transition, so leaving it on would restyle
        // the chip rather than adopt the component.
        Assert.Equal("chip chip--primary chip--small chip--filter", button.Find("button").GetAttribute("class"));
    }

    [Fact]
    public void An_icon_button_takes_the_whole_class_name_because_it_has_no_modifiers()
    {
        using var context = new BunitContext();

        var button = context.Render<IconButton>(parameters => parameters
            .Add(b => b.BaseClass, "entry-doc__icon-action")
            .Add(b => b.CssClass, "entry-doc__icon-action--push")
            .Add(b => b.AriaLabel, "Create an issue"));

        Assert.Equal(
            "entry-doc__icon-action entry-doc__icon-action--push",
            button.Find("button").GetAttribute("class"));
    }

    [Fact]
    public void A_toggle_button_names_its_pressed_state_separately_from_its_base()
    {
        using var context = new BunitContext();

        var button = context.Render<ToggleButton>(parameters => parameters
            .Add(b => b.BaseClass, "chip chip--scope")
            .Add(b => b.PressedCssClass, "chip--active")
            .Add(b => b.Pressed, true));

        var element = button.Find("button");

        // A host with chips calls the pressed state something of its own, so it
        // cannot be derived from the base the way a variant modifier is.
        Assert.Equal("chip chip--scope chip--active", element.GetAttribute("class"));
        Assert.Equal("true", element.GetAttribute("aria-pressed"));
    }

    [Fact]
    public void An_unpressed_toggle_button_carries_no_pressed_class_at_all()
    {
        using var context = new BunitContext();

        var button = context.Render<ToggleButton>(parameters => parameters
            .Add(b => b.BaseClass, "chip")
            .Add(b => b.PressedCssClass, "chip--active"));

        Assert.Equal("chip", button.Find("button").GetAttribute("class"));
    }

    /// <summary>
    /// A one-of-many strip drives Pressed from its own selection, so pressing
    /// the already-pressed option must announce the flip without the component
    /// deciding the answer for it.
    /// </summary>
    [Fact]
    public void A_toggle_button_announces_the_flipped_state_it_was_clicked_into()
    {
        using var context = new BunitContext();

        bool? announced = null;

        var button = context.Render<ToggleButton>(parameters => parameters
            .Add(b => b.Pressed, true)
            .Add(b => b.PressedChanged, (bool pressed) => announced = pressed));

        button.Find("button").Click();

        Assert.False(announced);
    }

    [Fact]
    public void A_bare_field_emits_the_parts_without_a_wrapper()
    {
        using var context = new BunitContext();

        var field = context.Render<TextField>(parameters => parameters
            .Add(f => f.Bare, true)
            .Add(f => f.LabelCssClass, "setting__label")
            .Add(f => f.InputCssClass, "setting__input setting__input--error")
            .Add(f => f.Label, "Folder"));

        Assert.Empty(field.FindAll(".field"));
        Assert.Equal("setting__label", field.Find("label").GetAttribute("class"));
        Assert.Equal("setting__input setting__input--error", field.Find("input").GetAttribute("class"));

        // The label still points at the input it labels; dropping the wrapper
        // must not drop the pairing with it.
        Assert.Equal(field.Find("input").Id, field.Find("label").GetAttribute("for"));
    }

    [Fact]
    public void A_textarea_with_no_grow_class_leaves_the_border_to_its_host()
    {
        using var context = new BunitContext();

        var field = context.Render<TextArea>(parameters => parameters
            .Add(f => f.Bare, true)
            .Add(f => f.AutoGrow, false)
            .Add(f => f.GrowCssClass, string.Empty)
            .Add(f => f.InputCssClass, "setting__input"));

        // The grow wrapper is what carries the border, so a host whose box is
        // already bordered would otherwise show two.
        Assert.Empty(field.FindAll(".field__grow"));
        Assert.Empty(field.FindAll(".grow-wrap"));
        Assert.Null(field.Find("div").GetAttribute("class"));
        Assert.Equal("setting__input", field.Find("textarea").GetAttribute("class"));
    }

    [Fact]
    public void A_checkbox_keeps_trailing_content_inside_the_label()
    {
        using var context = new BunitContext();

        var checkbox = context.Render<Checkbox>(parameters => parameters
            .Add(c => c.Label, "Architecture")
            .Add(c => c.ChildContent, "<code>.arc42</code>"));

        // Inside the label, so the whole row stays one click target.
        Assert.NotNull(checkbox.Find("label > code"));
    }

    /// <summary>
    /// The one hook in this library that adds rather than replaces, and the reason
    /// it is the exception: every other class parameter here names an element the
    /// host is dressing, where a table's row is an element the host does not draw
    /// at all. It stays the component's row, wearing one word from the host about
    /// this one of them.
    /// </summary>
    [Fact]
    public void A_data_tables_row_takes_a_hosts_modifier_beside_the_class_it_keeps()
    {
        using var context = new BunitContext();

        var cells = (RenderFragment<string>)(tool => builder =>
        {
            builder.OpenElement(0, "td");
            builder.AddContent(1, tool);
            builder.CloseElement();
        });

        var table = context.Render<DataTable<string>>(parameters => parameters
            .Add(c => c.Columns, [new DataTableColumn("Tool")])
            .Add(c => c.Items, (IReadOnlyList<string>)["architecture"])
            .Add(c => c.Row, cells)
            .Add(c => c.BaseClass, "tools")
            .Add(c => c.RowCssClass, (Func<string, string?>)(_ => "tools__row--disabled")));

        Assert.Equal("tools__row tools__row--disabled", table.Find("tbody tr").GetAttribute("class"));
        Assert.Empty(table.FindAll(".data-table__row"));
    }

    [Fact]
    public void An_alert_with_nothing_to_say_renders_nothing()
    {
        using var context = new BunitContext();

        var alert = context.Render<Alert>();

        Assert.Equal(string.Empty, alert.Markup.Trim());
    }

    [Fact]
    public void An_alert_can_be_pointed_at_the_hosts_own_styling()
    {
        using var context = new BunitContext();

        var alert = context.Render<Alert>(parameters => parameters
            .Add(a => a.Message, "Could not open the folder")
            .Add(a => a.BaseClass, "knowledge-menu__error")
            .Add(a => a.CssClass, "knowledge-menu__error--open"));

        var element = alert.Find("p");

        Assert.Equal("knowledge-menu__error knowledge-menu__error--open", element.GetAttribute("class"));
        Assert.Equal("alert", element.GetAttribute("role"));
    }
}
