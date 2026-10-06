using Backlog.Desktop.UI.Devbook;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.UI.Components.Layout;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A devbook sync sweep's last verdict beside the chapter it is about: the chapter's
/// own badge on each heading the sweep spoke about, the unit's badge on the unit's
/// root chapter, each leading to the pull request or drift issue the sweep opened.
/// </summary>
public sealed class DevbookSyncVerdictBadgeTests
{
    private const string Document = ".domain/sessions/domain.md";

    private const string Body = """
        # Sessions

        The context.

        ## Delivery Run

        One orchestrated delivery run.

        ## Delivery Run Recording

        The capability a session uses.

        ## Session Row

        One row of the session list.
        """;

    [Fact]
    public void A_heading_the_sweep_spoke_about_carries_its_verdict_and_the_root_its_units()
    {
        var store = new FakeVerdicts(
            Verdict("delivery-run", "code-ahead", unitVerdict: "code-ahead", evidence: "Trigger is stored; the chapter does not say so."),
            Verdict("delivery-run-recording", "aligned", unitVerdict: "code-ahead", evidence: "start_run's arguments match."));

        using var context = new BunitContext();

        var view = Render(context, store);

        var heads = view.FindAll(".md-chapter-head");
        var run = heads.Single(head => head.TextContent.Contains("Delivery Run", StringComparison.Ordinal) && !head.TextContent.Contains("Recording", StringComparison.Ordinal));
        var recording = heads.Single(head => head.TextContent.Contains("Delivery Run Recording", StringComparison.Ordinal));
        var row = heads.Single(head => head.TextContent.Contains("Session Row", StringComparison.Ordinal));

        // The root: the unit's badge, then its own chapter's, both anchors to the PR.
        var unit = run.QuerySelector("[data-testid='devbook-sync-unit-verdict']")!;
        Assert.Equal("a", unit.TagName.ToLowerInvariant());
        Assert.Equal("unit code-ahead · pr", unit.TextContent.Trim());
        Assert.Equal("https://github.com/JSdotNet/Backlog/pull/950", unit.GetAttribute("href"));
        Assert.Contains("badge--sync-verdict-code-ahead", unit.ClassName);

        var own = run.QuerySelector("[data-testid='devbook-sync-chapter-verdict']")!;
        Assert.Equal("code-ahead", own.TextContent.Trim());
        Assert.Contains("Trigger is stored; the chapter does not say so.", own.GetAttribute("title"));

        // A member chapter: its own verdict only.
        Assert.Null(recording.QuerySelector("[data-testid='devbook-sync-unit-verdict']"));
        var member = recording.QuerySelector("[data-testid='devbook-sync-chapter-verdict']")!;
        Assert.Equal("aligned", member.TextContent.Trim());
        Assert.Contains("badge--sync-verdict-aligned", member.ClassName);

        // A chapter no sweep spoke about carries nothing.
        Assert.Null(row.QuerySelector(".md-chapter-head__adornment"));
    }

    [Fact]
    public void A_word_outside_the_sweeps_vocabulary_keeps_its_spelling_and_wears_the_unknown_tone()
    {
        var store = new FakeVerdicts(Verdict("session-row", "drifting", unitVerdict: "drifting", evidence: "?", unit: "other"));

        using var context = new BunitContext();

        var badge = Render(context, store).Find("[data-testid='devbook-sync-chapter-verdict']");

        Assert.Equal("drifting", badge.TextContent.Trim());
        Assert.Contains("badge--sync-verdict-unknown", badge.ClassName);
        Assert.Contains("expected one of aligned, code-ahead, spec-ahead, conflict, unresolved", badge.GetAttribute("title"));
    }

    [Fact]
    public void Without_a_verdict_store_or_a_repository_nothing_is_drawn()
    {
        Assert.Null(DevbookSyncAdornment.For(null, new OneRepository("JSdotNet/Backlog"), "backlog", Document));
        Assert.Null(DevbookSyncAdornment.For(new FakeVerdicts(Verdict("delivery-run", "aligned", "aligned", null)), new OneRepository(null), "backlog", Document));
        Assert.Null(DevbookSyncAdornment.For(new FakeVerdicts(), new OneRepository("JSdotNet/Backlog"), "backlog", Document));
    }

    private static IRenderedComponent<FileView> Render(BunitContext context, IDevbookSyncVerdicts store) =>
        context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "domain.md")
            .Add(v => v.Body, Body)
            .Add(v => v.AllowChapterCopy, true)
            .Add(v => v.DevbookDocumentPath, Document)
            .Add(v => v.ChapterAdornment, DevbookSyncAdornment.For(store, new OneRepository("JSdotNet/Backlog"), "backlog", Document))
            .Add(v => v.TestId, "file"));

    private static DevbookSyncVerdict Verdict(string anchor, string verdict, string unitVerdict, string? evidence, string unit = "delivery-run") =>
        new(
            "JSdotNet/Backlog",
            Document,
            anchor,
            verdict,
            evidence,
            $".devbook/domain/sessions/domain.md#{unit}",
            "aggregate",
            unitVerdict,
            "pr",
            "https://github.com/JSdotNet/Backlog/pull/950",
            new DateTimeOffset(2026, 10, 5, 4, 30, 0, TimeSpan.Zero),
            "run-1");

    private sealed class FakeVerdicts(params DevbookSyncVerdict[] verdicts) : IDevbookSyncVerdicts
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<DevbookSyncVerdict> For(string repository, string chapterPath, IReadOnlyList<DevbookFolderSetting>? folders = null) =>
            [.. verdicts.Where(verdict =>
                string.Equals(verdict.Repository, repository, StringComparison.OrdinalIgnoreCase)
                && string.Equals(verdict.ChapterPath, DevbookChapterKey.Canonical(chapterPath, folders), StringComparison.OrdinalIgnoreCase))];

        public IReadOnlyList<DevbookSyncVerdict> ForRepository(string repository) =>
            [.. verdicts.Where(verdict => string.Equals(verdict.Repository, repository, StringComparison.OrdinalIgnoreCase))];

        public Task RecordAsync(IReadOnlyList<DevbookSyncVerdict> recorded, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    /// <summary>One repository, the alias resolving to it — or to none.</summary>
    private sealed class OneRepository(string? repository) : IDevbookFolderSource
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public string StorageDirectory => Path.GetTempPath();

        public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias) => DevbookFolderSetting.Defaults();

        public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null) =>
            new(key, true, null, repository, null, null);

        public void NotifyContentChanged()
        {
        }
    }
}
