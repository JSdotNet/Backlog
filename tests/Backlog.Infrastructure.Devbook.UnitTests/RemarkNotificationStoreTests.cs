using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// The Devbook context raising an alert when a remark left on another device syncs in,
/// closing the notifications about a remark when it is resolved — from a panel, the MCP
/// tool, or a resolve synced in from another device — and answering the startup probe about
/// it (<c>.devbook/domain/notifications/domain.md#subject</c>).
/// </summary>
public sealed class RemarkNotificationStoreTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly RecordingNotifications _notifications = new RecordingNotifications();
    private readonly MemoryRemarks _inner = new();
    private readonly FakeTimeProvider _clock = new(Noon.AddHours(1));

    [Fact]
    public async Task Resolving_a_remark_closes_the_notifications_about_it_once()
    {
        var store = Store();
        var remark = _inner.Seed(Remark());

        store.SetResolved(remark.Id, resolved: true);
        await store.LastClose;
        store.SetResolved(remark.Id, resolved: true);
        await store.LastClose;

        var closed = Assert.Single(_notifications.Closed);
        Assert.Equal(new NotificationSubject("devbook", "remark", remark.Id.ToString("D")), closed.Subject);
        Assert.Equal("Remark resolved", closed.Reason);
    }

    [Fact]
    public async Task A_resolve_synced_in_from_another_device_closes_them_the_same_way()
    {
        var store = Store();
        var remark = _inner.Seed(Remark());

        store.Apply(remark with { Resolved = true, UpdatedAt = Noon.AddMinutes(5) });
        await store.LastClose;

        Assert.Equal(remark.Id.ToString("D"), Assert.Single(_notifications.Closed).Subject.Id);
    }

    [Fact]
    public async Task Deleting_an_open_remark_closes_them_and_reopening_one_closes_nothing()
    {
        var store = Store();
        var deleted = _inner.Seed(Remark());
        var reopened = _inner.Seed(Remark() with { Resolved = true });

        store.Delete(deleted.Id);
        await store.LastClose;
        store.SetResolved(reopened.Id, resolved: false);
        store.Edit(reopened.Id, "Still wrong.");

        Assert.Equal(deleted.Id.ToString("D"), Assert.Single(_notifications.Closed).Subject.Id);
    }

    [Fact]
    public async Task The_write_lands_when_closing_fails_or_no_notifications_are_composed()
    {
        _notifications.Fails = new InvalidOperationException("locked");
        var failing = Store();
        var absent = new RemarkNotificationStore(_inner, () => null);
        var one = _inner.Seed(Remark());
        var two = _inner.Seed(Remark());

        failing.SetResolved(one.Id, resolved: true);
        await failing.LastClose;
        absent.SetResolved(two.Id, resolved: true);
        await absent.LastClose;

        Assert.True(_inner.Find(one.Id)!.Resolved);
        Assert.True(_inner.Find(two.Id)!.Resolved);
    }

    [Fact]
    public async Task The_probe_calls_a_remark_live_only_while_it_is_open()
    {
        var open = _inner.Seed(Remark());
        var resolved = _inner.Seed(Remark() with { Resolved = true });
        var deleted = _inner.Seed(Remark() with { DeletedAt = Noon });
        var probe = new RemarkSubjectProbe(_inner);

        Assert.Equal("devbook", probe.Context);
        Assert.True(await probe.IsLiveAsync(Subject(open.Id), TestContext.Current.CancellationToken));
        Assert.False(await probe.IsLiveAsync(Subject(resolved.Id), TestContext.Current.CancellationToken));
        Assert.False(await probe.IsLiveAsync(Subject(deleted.Id), TestContext.Current.CancellationToken));
        Assert.False(await probe.IsLiveAsync(Subject(Guid.NewGuid()), TestContext.Current.CancellationToken));
        Assert.True(await probe.IsLiveAsync(new NotificationSubject("devbook", "chapter", open.Id.ToString("D")), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_remark_left_on_another_device_raises_an_info_alert_about_it_linking_to_its_chapter()
    {
        var store = Store();
        var remark = Remark() with { RepositoryAlias = "backlog", ChapterPath = ".devbook/arc42/05 building blocks.md", Body = "Does the reply container\nneed its own TTL?" };

        store.Apply(remark);
        await store.LastAlert;

        var (_, draft) = Assert.Single(_notifications.Published);
        Assert.Equal(NotificationKind.Alert, draft.Kind);
        Assert.Equal(NotificationSeverity.Info, draft.Severity);
        Assert.Null(draft.Answer);
        Assert.Equal(Subject(remark.Id), draft.Subject);
        Assert.Equal(new NotificationSource("devbook", remark.Id.ToString("D")), draft.Source);
        Assert.Equal("Remark left on .devbook/arc42/05 building blocks.md", draft.Title);
        Assert.Equal("Does the reply container need its own TTL?", draft.Summary);
        Assert.Contains("> Does the reply container", draft.BodyMarkdown, StringComparison.Ordinal);
        Assert.Equal("backlog://devbook/backlog/.devbook/arc42/05%20building%20blocks.md", draft.DeepLink);
    }

    [Fact]
    public async Task A_remark_typed_here_a_draft_or_one_already_known_raises_no_alert()
    {
        var store = Store();
        var known = _inner.Seed(Remark());

        var added = store.Add("backlog", "arc42/05.md", 3, "Job");
        store.Edit(added.Id, "Mine.");
        store.Apply(known with { Body = "Edited elsewhere.", UpdatedAt = Noon.AddMinutes(1) });
        store.Apply(Remark() with { Body = string.Empty });
        store.Apply(Remark() with { Resolved = true });
        await store.LastAlert;

        Assert.Empty(_notifications.Published);
    }

    [Fact]
    public async Task A_first_pull_of_old_remarks_raises_no_alert()
    {
        var store = Store();
        _clock.Advance(TimeSpan.FromDays(3));

        store.Apply(Remark());
        store.Apply(Remark());
        await store.LastAlert;

        Assert.Empty(_notifications.Published);
    }

    [Fact]
    public async Task A_draft_known_here_that_arrives_with_words_raises_the_alert_once()
    {
        var store = Store();
        var draft = _inner.Seed(Remark() with { Body = string.Empty });

        store.Apply(draft with { Body = "Typed on the phone.", UpdatedAt = Noon.AddMinutes(1) });
        await store.LastAlert;
        store.Apply(draft with { Body = "Typed on the phone, again.", UpdatedAt = Noon.AddMinutes(2) });
        await store.LastAlert;

        Assert.Single(_notifications.Published);
    }

    [Fact]
    public void The_remark_is_kept_when_raising_its_alert_fails()
    {
        _notifications.Fails = new InvalidOperationException("locked");
        var store = Store();
        var remark = Remark();

        store.Apply(remark);

        Assert.NotNull(store.Find(remark.Id));
    }

    [Fact]
    public void The_decorator_hands_every_other_call_to_the_store_it_wraps()
    {
        var store = Store();
        var changed = 0;
        store.Changed += () => changed++;

        var added = store.Add("backlog", "arc42/05.md", 3, "Job");

        Assert.Same(added, store.Find(added.Id));
        Assert.Single(store.List("backlog", "arc42/05.md"));
        Assert.Equal(1, changed);
    }

    private RemarkNotificationStore Store() => new(_inner, () => _notifications, time: _clock);

    private static DevbookAnnotation Remark() =>
        new(Guid.NewGuid(), "backlog", "arc42/05.md", 2, "This is wrong.", "Job", Noon, Noon);

    private static NotificationSubject Subject(Guid id) => new("devbook", "remark", id.ToString("D"));

    /// <summary>The port's behaviour that matters here, in memory.</summary>
    private sealed class MemoryRemarks : IDevbookAnnotationStore
    {
        private readonly Dictionary<Guid, DevbookAnnotation> _remarks = [];

        public event Action? Changed;

        public DevbookAnnotation Seed(DevbookAnnotation remark)
        {
            _remarks[remark.Id] = remark;
            return remark;
        }

        public IReadOnlyList<DevbookAnnotation> List(string? repositoryAlias, string chapterPath) =>
            [.. _remarks.Values.Where(remark => remark.ChapterPath == chapterPath && remark.DeletedAt is null)];

        public DevbookAnnotation? Find(Guid id) => _remarks.GetValueOrDefault(id);

        public DevbookAnnotation Add(string? repositoryAlias, string chapterPath, int blockIndex, string author, string? blockHash = null)
        {
            var remark = new DevbookAnnotation(Guid.NewGuid(), repositoryAlias ?? string.Empty, chapterPath, blockIndex, string.Empty, author, Noon, Noon, BlockHash: blockHash);
            _remarks[remark.Id] = remark;
            Changed?.Invoke();
            return remark;
        }

        public void Edit(Guid id, string body) => _remarks[id] = _remarks[id] with { Body = body };

        public void SetResolved(Guid id, bool resolved) => _remarks[id] = _remarks[id] with { Resolved = resolved };

        public void Delete(Guid id) => _remarks[id] = _remarks[id] with { DeletedAt = Noon };

        public void Apply(DevbookAnnotation replicated) => _remarks[replicated.Id] = replicated;

        public IReadOnlyList<DevbookAnnotation> ListChangedSince(DateTimeOffset watermark) =>
            [.. _remarks.Values.Where(remark => remark.UpdatedAt > watermark)];
    }
}
