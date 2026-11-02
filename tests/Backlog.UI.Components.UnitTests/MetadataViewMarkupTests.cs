using System.Text.RegularExpressions;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The whole of a metadata record's rendering, pinned.
///
/// <para>Written when the record's field-by-field drawing moved out of
/// <c>MetadataView</c> and into one component per value shape. Every other
/// suite around this one asks about a field at a time — an alias
/// badge, a reference that is not a control — which is the right shape for a rule
/// about what a field means, and blind to what a move like that risks: a class that
/// quietly went missing three elements away from whatever the test was looking at,
/// or a newline that appeared between two rows because a component element replaced
/// a code block.</para>
///
/// <para>That last one is why this is a whole-markup test rather than a set of
/// selectors. Razor emits the whitespace between two sibling expressions as
/// content and trims it either side of a code block, so where the value components
/// are written inside the record is part of what the record renders. No CSS
/// selector can see that, and the stylesheet can.</para>
///
/// <para>
/// Six fields were redrawn in the round that followed, on the owner's review of the
/// storybook, and this is where that shows. <c>kind</c> is a classification chip
/// and <c>version</c> takes a <c>v</c>; both now hide their label rather than
/// printing it, so their rows carry <c>--bare</c> and their <c>dt</c> carries
/// <c>sr-only</c>. <c>aliases</c> is a superscript badge per name.
/// <c>feature-flag</c> is badges in the feature family rather than tag chips.
/// </para>
///
/// <para>Since then <c>issue</c>, <c>effort</c> and <c>roadmap</c> have left the
/// chapter view — the plan lives in Backlog's own entries — and <c>change</c> is
/// recognised and dropped like <c>order</c>. Their rows and the whitespace between
/// their call sites went with them.</para>
///
/// <para><c>related</c>, <c>depends-on</c>, <c>implements</c>, <c>alternatives</c>,
/// the two Extra rows, the headline in both of its shapes, and every scrap of
/// whitespace between the rows are byte-for-byte what they were before that round.
/// The <c>order</c> row is still absent, because the field is read, recognised and
/// dropped.</para>
/// </summary>
public sealed class MetadataViewMarkupTests
{
    [Fact]
    public void A_record_renders_what_it_has_always_rendered()
    {
        using var context = new BunitContext();

        var view = context.Render<MetadataView>(parameters => parameters
            .Add(record => record.Metadata, MetadataReader.Parse(EveryShape))
            .Add(record => record.Vocabulary, DevbookStatus.Vocabulary(DevbookFolder.Tech)));

        Assert.Equal(Normalize(TechMarkup), Normalize(view.Markup));
    }

    [Fact]
    public void The_same_record_without_a_folder_renders_what_it_has_always_rendered()
    {
        // The headline is the only difference: no vocabulary to offer means the
        // static badge instead of the select. Pinned separately because the select
        // is the branch that carries markup of its own, and a change to it must not
        // be able to hide behind the pill.
        using var context = new BunitContext();

        var view = context.Render<MetadataView>(parameters => parameters
            .Add(record => record.Metadata, MetadataReader.Parse(EveryShape)));

        Assert.Equal(Normalize(FolderBlindMarkup), Normalize(view.Markup));
    }

    [Fact]
    public void A_record_with_one_field_keeps_the_whitespace_the_absent_ones_leave()
    {
        // The one case that catches a refactor of the field list: ten fields that
        // draw nothing still leave the newline and indentation between their call
        // sites, and that whitespace is inside the description list the stylesheet
        // spaces. A row moved from an expression into a code block, or the reverse,
        // shows up here and nowhere else.
        using var context = new BunitContext();

        var view = context.Render<MetadataView>(parameters => parameters
            .Add(record => record.Metadata, MetadataReader.Parse(FeatureFlagOnly))
            .Add(record => record.Vocabulary, DevbookStatus.Vocabulary(DevbookFolder.Backlog)));

        Assert.Equal(Normalize(FeatureFlagMarkup), Normalize(view.Markup));
    }

    /// <summary>
    /// Line endings, so neither side depends on how this file was checked out; the
    /// sequence number Blazor mints per event handler, which counts renders in the
    /// test context rather than saying anything about this component; and the path
    /// data of the provider mark.
    ///
    /// <para>The mark is the last of those and the only one that is a judgement.
    /// <c>ProviderMark</c> draws GitHub's published monochrome logo as an inline
    /// path, and it has its own tests. Copied in here it would be six hundred
    /// characters of trademark geometry in the middle of a record's markup, and this
    /// test would fail — three times, unreadably — the day somebody updates the
    /// logo, which is a change about a vendor's mark and not about a knowledge
    /// field. What is pinned is that the mark is <em>there</em>, in that element,
    /// with those attributes.</para>
    /// </summary>
    private static string Normalize(string markup) =>
        Regex.Replace(
            Regex.Replace(markup.Replace("\r\n", "\n"), "blazor:onchange=\"[0-9]+\"", "blazor:onchange=\"ID\""),
            "<path d=\"[^\"]*\"",
            "<path d=\"MARK\"");

    /// <summary>Every value shape at once, which is a block no folder would write:
    /// the schema is folder-specific and half of these fields are meaningless
    /// together. <c>order</c> and <c>change</c> are in it on purpose — the fields
    /// this record must read and not draw — and so are <c>issue</c>, <c>effort</c>
    /// and <c>roadmap</c>, which it reads and the chapter view no longer draws; and
    /// so are the two keys that end up under Extra, one invented and one a
    /// <c>related</c> entry that will not parse.</summary>
    private const string EveryShape = """
        status: adopted
        kind: framework
        version: "10.0"
        effort: 5
        issue: JSdotNet/Backlog#118
        depends-on: [".tech/shared.md#net-runtime", ".tech/shared.md#c-language"]
        related: [".arc42/04-solution-strategy.md#thin-cloud-rich-desktop"]
        implements: [.domain/tasks/features.md#feature-roadmap-planning]
        roadmap: [sync-service, mobile-mvp]
        feature-flag: [inbox-pane, inbox-filters]
        aliases: [TaskItem, backlog_entry_id]
        alternatives: ["Azure Functions", "Controller-based ASP.NET Core"]
        order: [overview.md, backlog]
        change: add-remark-kind
        owner: platform-team
        related-typo: [not an address]
        """;

    /// <summary>A backlog block stating one field, so every other row in the list
    /// is an absent one.</summary>
    private const string FeatureFlagOnly = """
        status: ready
        feature-flag: [inbox-pane]
        """;

    // The expectations are written flush left, closing delimiter included. A raw
    // string literal strips whatever indentation its closing delimiter carries from
    // every line, and the indentation inside this markup is not decoration: it is
    // the source indentation of the field list, emitted as content between one row
    // and the next.
    /// <summary>Read as .tech, where the status is one of the folder's own and the
    /// headline offers the select.</summary>
    private const string TechMarkup = """
<div class="devbook-record" role="group" aria-label="Devbook metadata"><div class="devbook-record__headline"><label class="status-editor badge badge--status badge--status-active"><span class="sr-only">Change status</span>
    <select class="status-editor__select" value="adopted" aria-label="Change status" title="Change status" blazor:onchange="ID"><option value="candidate">candidate</option><option value="trial">trial</option><option value="adopted" selected>adopted</option><option value="hold">hold</option><option value="retired">retired</option></select></label></div><dl class="devbook-fields"><div class="devbook-fields__row"><dt class="devbook-fields__label">related</dt>
                    <dd class="devbook-fields__value"><code class="devbook-ref devbook-ref--inert">.arc42/04-solution-strategy.md#thin-cloud-rich-desktop</code></dd></div>
                <div class="devbook-fields__row"><dt class="devbook-fields__label">depends-on</dt>
                    <dd class="devbook-fields__value"><code class="devbook-ref devbook-ref--inert">.tech/shared.md#net-runtime</code><code class="devbook-ref devbook-ref--inert">.tech/shared.md#c-language</code></dd></div>
                <div class="devbook-fields__row"><dt class="devbook-fields__label">implements</dt>
                    <dd class="devbook-fields__value"><code class="devbook-ref devbook-ref--inert">.domain/tasks/features.md#feature-roadmap-planning</code></dd></div>
                <div class="devbook-fields__row"><dt class="devbook-fields__label">aliases</dt>
                    <dd class="devbook-fields__value"><span class="badge badge--alias" title="alias: TaskItem">TaskItem</span><span class="badge badge--alias" title="alias: backlog_entry_id">backlog_entry_id</span></dd></div>
                <div class="devbook-fields__row"><dt class="devbook-fields__label">alternatives</dt>
                    <dd class="devbook-fields__value"><code class="devbook-value">Azure Functions</code><code class="devbook-value">Controller-based ASP.NET Core</code></dd></div>
                <div class="devbook-fields__row devbook-fields__row--bare"><dt class="devbook-fields__label sr-only">kind</dt>
                    <dd class="devbook-fields__value"><span class="badge badge--kind" title="kind: framework">framework</span></dd></div>
                <div class="devbook-fields__row devbook-fields__row--bare"><dt class="devbook-fields__label sr-only">version</dt>
                    <dd class="devbook-fields__value"><code class="devbook-value" title="version: 10.0">v10.0</code></dd></div>
                <div class="devbook-fields__row"><dt class="devbook-fields__label">feature-flag</dt>
                    <dd class="devbook-fields__value" data-testid="devbook-feature-flag-tags"><span class="badge badge--feature">inbox-pane</span><span class="badge badge--feature">inbox-filters</span></dd></div><div class="devbook-fields__row"><dt class="devbook-fields__label">owner</dt>
                    <dd class="devbook-fields__value"><code class="devbook-value">platform-team</code></dd></div><div class="devbook-fields__row"><dt class="devbook-fields__label">related-typo</dt>
                    <dd class="devbook-fields__value"><code class="devbook-value">not an address</code></dd></div></dl></div>
""";

    /// <summary>The same block with no folder given: the status is shown and not
    /// judged, so the headline holds the plain badge.</summary>
    private const string FolderBlindMarkup = """
<div class="devbook-record" role="group" aria-label="Devbook metadata"><div class="devbook-record__headline"><span class="badge badge--status">adopted</span></div><dl class="devbook-fields"><div class="devbook-fields__row"><dt class="devbook-fields__label">related</dt>
                    <dd class="devbook-fields__value"><code class="devbook-ref devbook-ref--inert">.arc42/04-solution-strategy.md#thin-cloud-rich-desktop</code></dd></div>
                <div class="devbook-fields__row"><dt class="devbook-fields__label">depends-on</dt>
                    <dd class="devbook-fields__value"><code class="devbook-ref devbook-ref--inert">.tech/shared.md#net-runtime</code><code class="devbook-ref devbook-ref--inert">.tech/shared.md#c-language</code></dd></div>
                <div class="devbook-fields__row"><dt class="devbook-fields__label">implements</dt>
                    <dd class="devbook-fields__value"><code class="devbook-ref devbook-ref--inert">.domain/tasks/features.md#feature-roadmap-planning</code></dd></div>
                <div class="devbook-fields__row"><dt class="devbook-fields__label">aliases</dt>
                    <dd class="devbook-fields__value"><span class="badge badge--alias" title="alias: TaskItem">TaskItem</span><span class="badge badge--alias" title="alias: backlog_entry_id">backlog_entry_id</span></dd></div>
                <div class="devbook-fields__row"><dt class="devbook-fields__label">alternatives</dt>
                    <dd class="devbook-fields__value"><code class="devbook-value">Azure Functions</code><code class="devbook-value">Controller-based ASP.NET Core</code></dd></div>
                <div class="devbook-fields__row devbook-fields__row--bare"><dt class="devbook-fields__label sr-only">kind</dt>
                    <dd class="devbook-fields__value"><span class="badge badge--kind" title="kind: framework">framework</span></dd></div>
                <div class="devbook-fields__row devbook-fields__row--bare"><dt class="devbook-fields__label sr-only">version</dt>
                    <dd class="devbook-fields__value"><code class="devbook-value" title="version: 10.0">v10.0</code></dd></div>
                <div class="devbook-fields__row"><dt class="devbook-fields__label">feature-flag</dt>
                    <dd class="devbook-fields__value" data-testid="devbook-feature-flag-tags"><span class="badge badge--feature">inbox-pane</span><span class="badge badge--feature">inbox-filters</span></dd></div><div class="devbook-fields__row"><dt class="devbook-fields__label">owner</dt>
                    <dd class="devbook-fields__value"><code class="devbook-value">platform-team</code></dd></div><div class="devbook-fields__row"><dt class="devbook-fields__label">related-typo</dt>
                    <dd class="devbook-fields__value"><code class="devbook-value">not an address</code></dd></div></dl></div>
""";

    /// <summary>One field stated, and the whitespace the absent ones before it
    /// leave behind them inside the description list.</summary>
    private const string FeatureFlagMarkup = """
<div class="devbook-record" role="group" aria-label="Devbook metadata"><div class="devbook-record__headline"><label class="status-editor badge badge--status badge--status-ready"><span class="sr-only">Change status</span>
    <select class="status-editor__select" value="ready" aria-label="Change status" title="Change status" blazor:onchange="ID"><option value="draft">draft</option><option value="ready" selected>ready</option><option value="in-progress">in-progress</option><option value="done">done</option><option value="blocked">blocked</option></select></label></div><dl class="devbook-fields">
                
                
                
                
                
                
                <div class="devbook-fields__row"><dt class="devbook-fields__label">feature-flag</dt>
                    <dd class="devbook-fields__value" data-testid="devbook-feature-flag-tags"><span class="badge badge--feature">inbox-pane</span></dd></div></dl></div>
""";
}
