using System.Text.RegularExpressions;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The whole of a metadata record's rendering, pinned.
///
/// <para>Written when the record's field-by-field drawing moved out of
/// <c>MetadataView</c> and into one component per value shape. Every other
/// suite around this one asks about a field at a time — an effort badge, an alias
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
/// and <c>version</c> takes a <c>v</c>; both, with <c>effort</c>, now hide their
/// label rather than printing it, so their rows carry <c>--bare</c> and their
/// <c>dt</c> carries <c>sr-only</c>. <c>issue</c> is drawn through
/// <c>IntegrationLink</c> and reads as the number and its repository behind the
/// GitHub mark. <c>aliases</c> is a superscript badge per name. <c>feature-flag</c>
/// and <c>roadmap</c> are badges in the feature family rather than tag chips.
/// </para>
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
            .Add(record => record.Vocabulary, KnowledgeStatus.Vocabulary(KnowledgeFolder.Tech)));

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
            .Add(record => record.Metadata, MetadataReader.Parse(IssueOnly))
            .Add(record => record.Vocabulary, KnowledgeStatus.Vocabulary(KnowledgeFolder.Backlog)));

        Assert.Equal(Normalize(IssueMarkup), Normalize(view.Markup));
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
    /// together. <c>order</c> is in it on purpose — it is the field this record
    /// must read and not draw — and so are the two keys that end up under Extra,
    /// one invented and one a <c>related</c> entry that will not parse.</summary>
    private const string EveryShape = """
        status: adopted
        kind: framework
        version: "10.0"
        effort: 5
        issue: JSdotNet/Backlog#118
        depends-on: [".tech/shared.md#net-runtime", ".tech/shared.md#c-language"]
        related: [".arc42/04-solution-strategy.md#thin-cloud-rich-desktop"]
        implements: [.domain/backlog/features.md#feature-roadmap-planning]
        roadmap: [sync-service, mobile-mvp]
        feature-flag: [inbox-pane, inbox-filters]
        aliases: [TaskItem, backlog_entry_id]
        alternatives: ["Azure Functions", "Controller-based ASP.NET Core"]
        order: [overview.md, backlog]
        owner: platform-team
        related-typo: [not an address]
        """;

    /// <summary>A backlog block stating one field, so every other row in the list
    /// is an absent one.</summary>
    private const string IssueOnly = """
        status: ready
        issue: https://github.com/JSdotNet/Backlog/issues/118
        """;

    // The expectations are written flush left, closing delimiter included. A raw
    // string literal strips whatever indentation its closing delimiter carries from
    // every line, and the indentation inside this markup is not decoration: it is
    // the source indentation of the field list, emitted as content between one row
    // and the next.
    /// <summary>Read as .tech, where the status is one of the folder's own and the
    /// headline offers the select.</summary>
    private const string TechMarkup = """
<div class="knowledge-record" role="group" aria-label="Knowledge metadata"><div class="knowledge-record__headline"><label class="status-editor badge badge--status badge--status-active"><span class="sr-only">Change status</span>
    <select class="status-editor__select" value="adopted" aria-label="Change status" title="Change status" blazor:onchange="ID"><option value="candidate">candidate</option><option value="trial">trial</option><option value="adopted" selected>adopted</option><option value="hold">hold</option><option value="retired">retired</option></select></label></div><dl class="knowledge-fields"><div class="knowledge-fields__row"><dt class="knowledge-fields__label">related</dt>
                    <dd class="knowledge-fields__value"><code class="knowledge-ref knowledge-ref--inert">.arc42/04-solution-strategy.md#thin-cloud-rich-desktop</code></dd></div>
                <div class="knowledge-fields__row"><dt class="knowledge-fields__label">depends-on</dt>
                    <dd class="knowledge-fields__value"><code class="knowledge-ref knowledge-ref--inert">.tech/shared.md#net-runtime</code><code class="knowledge-ref knowledge-ref--inert">.tech/shared.md#c-language</code></dd></div>
                <div class="knowledge-fields__row"><dt class="knowledge-fields__label">implements</dt>
                    <dd class="knowledge-fields__value"><code class="knowledge-ref knowledge-ref--inert">.domain/backlog/features.md#feature-roadmap-planning</code></dd></div>
                <div class="knowledge-fields__row"><dt class="knowledge-fields__label">aliases</dt>
                    <dd class="knowledge-fields__value"><span class="badge badge--alias" title="alias: TaskItem">TaskItem</span><span class="badge badge--alias" title="alias: backlog_entry_id">backlog_entry_id</span></dd></div>
                <div class="knowledge-fields__row"><dt class="knowledge-fields__label">alternatives</dt>
                    <dd class="knowledge-fields__value"><code class="knowledge-value">Azure Functions</code><code class="knowledge-value">Controller-based ASP.NET Core</code></dd></div>
                <div class="knowledge-fields__row knowledge-fields__row--bare"><dt class="knowledge-fields__label sr-only">kind</dt>
                    <dd class="knowledge-fields__value"><span class="badge badge--kind" title="kind: framework">framework</span></dd></div>
                <div class="knowledge-fields__row knowledge-fields__row--bare"><dt class="knowledge-fields__label sr-only">version</dt>
                    <dd class="knowledge-fields__value"><code class="knowledge-value" title="version: 10.0">v10.0</code></dd></div>
                <div class="knowledge-fields__row"><dt class="knowledge-fields__label">issue</dt>
                    <dd class="knowledge-fields__value"><span class="integration-link integration-link--inert integration-link--inline" title="JSdotNet/Backlog#118">
            <svg class="provider-mark provider-mark--github" viewBox="0 0 24 24" width="16" height="16" fill="currentColor" aria-hidden="true" focusable="false"><path d="MARK"></path></svg>

            <span class="integration-link__label">#118</span><span class="integration-link__repository">JSdotNet/Backlog</span></span></dd></div>
                <div class="knowledge-fields__row knowledge-fields__row--bare"><dt class="knowledge-fields__label sr-only">effort</dt>
                    <dd class="knowledge-fields__value"><span class="badge badge--effort" data-testid="knowledge-effort-badge" title="effort: 5 story points">5 pts</span></dd></div>
                <div class="knowledge-fields__row"><dt class="knowledge-fields__label">roadmap</dt>
                    <dd class="knowledge-fields__value" data-testid="knowledge-roadmap-tags"><span class="badge badge--feature">sync-service</span><span class="badge badge--feature">mobile-mvp</span></dd></div>
                <div class="knowledge-fields__row"><dt class="knowledge-fields__label">feature-flag</dt>
                    <dd class="knowledge-fields__value" data-testid="knowledge-feature-flag-tags"><span class="badge badge--feature">inbox-pane</span><span class="badge badge--feature">inbox-filters</span></dd></div><div class="knowledge-fields__row"><dt class="knowledge-fields__label">owner</dt>
                    <dd class="knowledge-fields__value"><code class="knowledge-value">platform-team</code></dd></div><div class="knowledge-fields__row"><dt class="knowledge-fields__label">related-typo</dt>
                    <dd class="knowledge-fields__value"><code class="knowledge-value">not an address</code></dd></div></dl></div>
""";

    /// <summary>The same block with no folder given: the status is shown and not
    /// judged, so the headline holds the plain badge.</summary>
    private const string FolderBlindMarkup = """
<div class="knowledge-record" role="group" aria-label="Knowledge metadata"><div class="knowledge-record__headline"><span class="badge badge--status">adopted</span></div><dl class="knowledge-fields"><div class="knowledge-fields__row"><dt class="knowledge-fields__label">related</dt>
                    <dd class="knowledge-fields__value"><code class="knowledge-ref knowledge-ref--inert">.arc42/04-solution-strategy.md#thin-cloud-rich-desktop</code></dd></div>
                <div class="knowledge-fields__row"><dt class="knowledge-fields__label">depends-on</dt>
                    <dd class="knowledge-fields__value"><code class="knowledge-ref knowledge-ref--inert">.tech/shared.md#net-runtime</code><code class="knowledge-ref knowledge-ref--inert">.tech/shared.md#c-language</code></dd></div>
                <div class="knowledge-fields__row"><dt class="knowledge-fields__label">implements</dt>
                    <dd class="knowledge-fields__value"><code class="knowledge-ref knowledge-ref--inert">.domain/backlog/features.md#feature-roadmap-planning</code></dd></div>
                <div class="knowledge-fields__row"><dt class="knowledge-fields__label">aliases</dt>
                    <dd class="knowledge-fields__value"><span class="badge badge--alias" title="alias: TaskItem">TaskItem</span><span class="badge badge--alias" title="alias: backlog_entry_id">backlog_entry_id</span></dd></div>
                <div class="knowledge-fields__row"><dt class="knowledge-fields__label">alternatives</dt>
                    <dd class="knowledge-fields__value"><code class="knowledge-value">Azure Functions</code><code class="knowledge-value">Controller-based ASP.NET Core</code></dd></div>
                <div class="knowledge-fields__row knowledge-fields__row--bare"><dt class="knowledge-fields__label sr-only">kind</dt>
                    <dd class="knowledge-fields__value"><span class="badge badge--kind" title="kind: framework">framework</span></dd></div>
                <div class="knowledge-fields__row knowledge-fields__row--bare"><dt class="knowledge-fields__label sr-only">version</dt>
                    <dd class="knowledge-fields__value"><code class="knowledge-value" title="version: 10.0">v10.0</code></dd></div>
                <div class="knowledge-fields__row"><dt class="knowledge-fields__label">issue</dt>
                    <dd class="knowledge-fields__value"><span class="integration-link integration-link--inert integration-link--inline" title="JSdotNet/Backlog#118">
            <svg class="provider-mark provider-mark--github" viewBox="0 0 24 24" width="16" height="16" fill="currentColor" aria-hidden="true" focusable="false"><path d="MARK"></path></svg>

            <span class="integration-link__label">#118</span><span class="integration-link__repository">JSdotNet/Backlog</span></span></dd></div>
                <div class="knowledge-fields__row knowledge-fields__row--bare"><dt class="knowledge-fields__label sr-only">effort</dt>
                    <dd class="knowledge-fields__value"><span class="badge badge--effort" data-testid="knowledge-effort-badge" title="effort: 5 story points">5 pts</span></dd></div>
                <div class="knowledge-fields__row"><dt class="knowledge-fields__label">roadmap</dt>
                    <dd class="knowledge-fields__value" data-testid="knowledge-roadmap-tags"><span class="badge badge--feature">sync-service</span><span class="badge badge--feature">mobile-mvp</span></dd></div>
                <div class="knowledge-fields__row"><dt class="knowledge-fields__label">feature-flag</dt>
                    <dd class="knowledge-fields__value" data-testid="knowledge-feature-flag-tags"><span class="badge badge--feature">inbox-pane</span><span class="badge badge--feature">inbox-filters</span></dd></div><div class="knowledge-fields__row"><dt class="knowledge-fields__label">owner</dt>
                    <dd class="knowledge-fields__value"><code class="knowledge-value">platform-team</code></dd></div><div class="knowledge-fields__row"><dt class="knowledge-fields__label">related-typo</dt>
                    <dd class="knowledge-fields__value"><code class="knowledge-value">not an address</code></dd></div></dl></div>
""";

    /// <summary>One field stated, and the whitespace the ten absent ones leave
    /// behind them inside the description list.</summary>
    private const string IssueMarkup = """
<div class="knowledge-record" role="group" aria-label="Knowledge metadata"><div class="knowledge-record__headline"><label class="status-editor badge badge--status badge--status-ready"><span class="sr-only">Change status</span>
    <select class="status-editor__select" value="ready" aria-label="Change status" title="Change status" blazor:onchange="ID"><option value="draft">draft</option><option value="ready" selected>ready</option><option value="in-progress">in-progress</option><option value="done">done</option><option value="blocked">blocked</option></select></label></div><dl class="knowledge-fields">
                
                
                
                
                
                
                <div class="knowledge-fields__row"><dt class="knowledge-fields__label">issue</dt>
                    <dd class="knowledge-fields__value"><a class="integration-link integration-link--link integration-link--inline" href="https://github.com/JSdotNet/Backlog/issues/118" target="_blank" rel="noopener" title="https://github.com/JSdotNet/Backlog/issues/118">
            <svg class="provider-mark provider-mark--github" viewBox="0 0 24 24" width="16" height="16" fill="currentColor" aria-hidden="true" focusable="false"><path d="MARK"></path></svg>

            <span class="integration-link__label">#118</span><span class="integration-link__repository">JSdotNet/Backlog</span></a></dd></div>
                
                
                </dl></div>
""";
}
