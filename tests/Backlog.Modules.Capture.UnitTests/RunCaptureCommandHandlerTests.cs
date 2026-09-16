using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.DataTransferObjects;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Capture.Features.RunCapture;
using Backlog.Modules.Capture.Ports;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Capture.UnitTests;

/// <summary>
/// One run over the sources the reader has switched on.
/// <para>
/// The run owns every rule between an adapter and the Inbox: which of what an
/// adapter found is new — decided by handing each entry over under a
/// deterministic id and counting only the ones the receiving side had not seen
/// — what each source's line says, and how one source's failure stays that
/// source's. The adapters and the delivery are fakes here, so what is under
/// test is the run and nothing it talks to.
/// </para>
/// </summary>
public sealed class RunCaptureCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Nothing_enabled_reports_an_empty_run()
    {
        var handler = Handler(new FakeSettings(), new FakeDelivery());

        var result = await handler.Handle(new RunCaptureCommand(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.NothingEnabled);
        Assert.Empty(result.Value.Sources);
        Assert.Equal(0, result.Value.TotalNewItems);
        Assert.Equal(Now, result.Value.RanAt);
    }

    [Fact]
    public async Task An_enabled_source_with_no_adapter_reports_zero_new_items_and_says_so()
    {
        var settings = new FakeSettings();
        settings.SetEnabled(CaptureSourceKind.YouTube, true);
        var handler = Handler(settings, new FakeDelivery());

        var result = await handler.Handle(new RunCaptureCommand(), TestContext.Current.CancellationToken);

        var source = Assert.Single(result.Value.Sources);
        Assert.Equal(CaptureSourceKind.YouTube, source.Kind);
        Assert.Equal(0, source.NewItems);
        Assert.Contains("YouTube", source.Message, StringComparison.Ordinal);
        Assert.Contains("no adapter", source.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.Value.NothingEnabled);
    }

    [Fact]
    public async Task Only_enabled_sources_are_run()
    {
        var settings = new FakeSettings();
        settings.SetEnabled(CaptureSourceKind.Website, true);
        settings.SetEnabled(CaptureSourceKind.Email, true);
        var handler = Handler(settings, new FakeDelivery());

        var result = await handler.Handle(new RunCaptureCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(
            [CaptureSourceKind.Website, CaptureSourceKind.Email],
            result.Value.Sources.Select(source => source.Kind).ToArray());
    }

    [Fact]
    public async Task A_registered_adapter_is_called_with_its_source()
    {
        var settings = new FakeSettings();
        settings.SetEnabled(CaptureSourceKind.YouTube, true);
        settings.SetTargets(CaptureSourceKind.YouTube, ["https://www.youtube.com/@dotnet"]);
        var adapter = new RecordingAdapter(CaptureSourceKind.YouTube, Entry("a"), Entry("b"), Entry("c"));
        var handler = Handler(settings, new FakeDelivery(), adapter);

        var result = await handler.Handle(new RunCaptureCommand(), TestContext.Current.CancellationToken);

        Assert.NotNull(adapter.Received);
        Assert.Equal(["https://www.youtube.com/@dotnet"], adapter.Received.Targets);

        var source = Assert.Single(result.Value.Sources);
        Assert.Equal(3, source.NewItems);
        Assert.Equal(3, result.Value.TotalNewItems);
        Assert.Equal("YouTube: 3 new items.", source.Message);
    }

    [Fact]
    public async Task Each_entry_is_delivered_under_a_deterministic_id_with_what_the_adapter_found()
    {
        var settings = new FakeSettings();
        settings.SetEnabled(CaptureSourceKind.Website, true);
        var delivery = new FakeDelivery();
        var published = Now.AddDays(-2);
        var entry = new CapturedEntry("https://example.org/post/1", "A post", "https://example.org/post/1", "The body.", published);
        var handler = Handler(settings, delivery, new RecordingAdapter(CaptureSourceKind.Website, entry));

        await handler.Handle(new RunCaptureCommand(), TestContext.Current.CancellationToken);

        var item = Assert.Single(delivery.Delivered);
        Assert.Equal(CaptureIds.For(CaptureSourceKind.Website, "https://example.org/post/1"), item.Id);
        Assert.Equal(CaptureSourceKind.Website, item.Kind);
        Assert.Equal("A post", item.Title);
        Assert.Equal("https://example.org/post/1", item.SourceUrl);
        Assert.Equal("The body.", item.BodyMd);
        Assert.Equal(published, item.CapturedAt);
    }

    [Fact]
    public async Task An_entry_the_source_did_not_date_is_stamped_with_now()
    {
        var settings = new FakeSettings();
        settings.SetEnabled(CaptureSourceKind.Website, true);
        var delivery = new FakeDelivery();
        var handler = Handler(settings, delivery, new RecordingAdapter(CaptureSourceKind.Website, new CapturedEntry("undated", "Undated", null, null, PublishedAt: null)));

        await handler.Handle(new RunCaptureCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(Now, Assert.Single(delivery.Delivered).CapturedAt);
    }

    /// <summary>The whole point of the id: the second run over the same feed
    /// hands the same ids over, the receiving side already has them, and the
    /// run counts nothing new.</summary>
    [Fact]
    public async Task Entries_the_receiving_side_already_holds_are_not_counted_as_new()
    {
        var settings = new FakeSettings();
        settings.SetEnabled(CaptureSourceKind.YouTube, true);
        var delivery = new FakeDelivery();
        var handler = Handler(settings, delivery, new RecordingAdapter(CaptureSourceKind.YouTube, Entry("a"), Entry("b")));

        var first = await handler.Handle(new RunCaptureCommand(), TestContext.Current.CancellationToken);
        var second = await handler.Handle(new RunCaptureCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(2, first.Value.TotalNewItems);
        Assert.Equal(0, second.Value.TotalNewItems);
        Assert.Equal("YouTube: 0 new items.", Assert.Single(second.Value.Sources).Message);
        Assert.Equal(4, delivery.Offered.Count);
    }

    [Fact]
    public async Task An_entry_with_no_title_is_skipped_rather_than_delivered()
    {
        var settings = new FakeSettings();
        settings.SetEnabled(CaptureSourceKind.Website, true);
        var delivery = new FakeDelivery();
        var handler = Handler(settings, delivery, new RecordingAdapter(
            CaptureSourceKind.Website,
            new CapturedEntry("blank", "   ", null, null, null),
            Entry("kept")));

        var result = await handler.Handle(new RunCaptureCommand(), TestContext.Current.CancellationToken);

        Assert.Equal("Entry kept", Assert.Single(delivery.Offered).Title);
        Assert.Equal(1, result.Value.TotalNewItems);
    }

    [Fact]
    public async Task An_entry_the_receiving_side_ignored_is_not_counted()
    {
        var settings = new FakeSettings();
        settings.SetEnabled(CaptureSourceKind.Website, true);
        var delivery = new FakeDelivery { Answer = CaptureDeliveryOutcome.Ignored };
        var handler = Handler(settings, delivery, new RecordingAdapter(CaptureSourceKind.Website, Entry("a")));

        var result = await handler.Handle(new RunCaptureCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Value.TotalNewItems);
    }

    [Fact]
    public async Task The_adapters_notes_follow_the_count_on_the_sources_line()
    {
        var settings = new FakeSettings();
        settings.SetEnabled(CaptureSourceKind.Website, true);
        var adapter = new RecordingAdapter(
            CaptureSourceKind.Website,
            new CaptureSourceFindings([Entry("a")], ["https://x: no feed found", "https://y: timed out"]));
        var handler = Handler(settings, new FakeDelivery(), adapter);

        var result = await handler.Handle(new RunCaptureCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(
            "Website: 1 new item · https://x: no feed found · https://y: timed out.",
            Assert.Single(result.Value.Sources).Message);
    }

    [Fact]
    public async Task An_adapter_that_throws_does_not_fail_the_run()
    {
        var settings = new FakeSettings();
        settings.SetEnabled(CaptureSourceKind.YouTube, true);
        settings.SetEnabled(CaptureSourceKind.Website, true);
        var handler = Handler(settings, new FakeDelivery(), new ThrowingAdapter(CaptureSourceKind.YouTube, "The feed timed out."));

        var result = await handler.Handle(new RunCaptureCommand(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Sources.Count);

        var failed = result.Value.Sources.Single(source => source.Kind == CaptureSourceKind.YouTube);
        Assert.Equal(0, failed.NewItems);
        Assert.Contains("The feed timed out.", failed.Message, StringComparison.Ordinal);

        // The source after the one that failed still got its turn.
        var website = result.Value.Sources.Single(source => source.Kind == CaptureSourceKind.Website);
        Assert.Contains("no adapter", website.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A delivery failing part-way is reported on that source's line
    /// with the count so far intact: the entries before it did land, and a
    /// line saying nothing did would leave the pane unrefreshed over items that
    /// are there.</summary>
    [Fact]
    public async Task A_delivery_that_throws_is_that_sources_line_and_keeps_the_count_delivered_before_it()
    {
        var settings = new FakeSettings();
        settings.SetEnabled(CaptureSourceKind.YouTube, true);
        settings.SetEnabled(CaptureSourceKind.Website, true);
        var delivery = new FakeDelivery { ThrowOn = "b", ThrowMessage = "The store is locked." };
        var handler = Handler(
            settings,
            delivery,
            new RecordingAdapter(CaptureSourceKind.YouTube, Entry("a"), Entry("b"), Entry("c")),
            new RecordingAdapter(CaptureSourceKind.Website, Entry("d")));

        var result = await handler.Handle(new RunCaptureCommand(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);

        var failed = result.Value.Sources.Single(source => source.Kind == CaptureSourceKind.YouTube);
        Assert.Equal(1, failed.NewItems);
        Assert.Equal("YouTube: 1 new item · The store is locked.", failed.Message);

        var website = result.Value.Sources.Single(source => source.Kind == CaptureSourceKind.Website);
        Assert.Equal(1, website.NewItems);
        Assert.Equal(2, result.Value.TotalNewItems);
    }

    /// <summary>The run is written down as it is reported — one entry per
    /// source it looked at, with that source's count and line — so the panel's
    /// "last capture" reads the same sentence the pane showed.</summary>
    [Fact]
    public async Task The_run_is_written_to_the_log_as_it_was_reported()
    {
        var settings = new FakeSettings();
        settings.SetEnabled(CaptureSourceKind.YouTube, true);
        settings.SetEnabled(CaptureSourceKind.Website, true);
        var log = new FakeLog();
        var handler = Handler(settings, new FakeDelivery(), log, new RecordingAdapter(CaptureSourceKind.YouTube, Entry("a"), Entry("b")));

        var result = await handler.Handle(new RunCaptureCommand(), TestContext.Current.CancellationToken);

        var run = Assert.Single(log.Recorded);
        Assert.Same(result.Value, run);
        Assert.Equal(Now, run.RanAt);
        Assert.Collection(
            run.Sources,
            youtube => Assert.Equal((CaptureSourceKind.YouTube, 2, "YouTube: 2 new items."), (youtube.Kind, youtube.NewItems, youtube.Message)),
            website => Assert.Equal((CaptureSourceKind.Website, 0), (website.Kind, website.NewItems)));
    }

    /// <summary>An empty run is still a run that happened; the log gets it
    /// and keeps nothing per source, since no source was looked at.</summary>
    [Fact]
    public async Task A_run_with_nothing_enabled_is_still_written_down_as_empty()
    {
        var log = new FakeLog();
        var handler = Handler(new FakeSettings(), new FakeDelivery(), log);

        await handler.Handle(new RunCaptureCommand(), TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(log.Recorded).NothingEnabled);
    }

    [Fact]
    public async Task Cancellation_ends_the_run_rather_than_becoming_a_sources_line()
    {
        var settings = new FakeSettings();
        settings.SetEnabled(CaptureSourceKind.YouTube, true);
        using var cancellation = new CancellationTokenSource();
        var handler = Handler(settings, new FakeDelivery(), new CancellingAdapter(CaptureSourceKind.YouTube, cancellation));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.Handle(new RunCaptureCommand(), cancellation.Token));
    }

    private static RunCaptureCommandHandler Handler(ICaptureSourceSettings settings, ICaptureDelivery delivery, params ICaptureSourceAdapter[] adapters) =>
        Handler(settings, delivery, new FakeLog(), adapters);

    private static RunCaptureCommandHandler Handler(ICaptureSourceSettings settings, ICaptureDelivery delivery, ICaptureRunLog log, params ICaptureSourceAdapter[] adapters) =>
        new(settings, adapters, delivery, log, new FakeTimeProvider(Now));

    /// <summary>The log as a list of what was handed to it. Reading it back
    /// per source is the store's business and is tested with the store.</summary>
    private sealed class FakeLog : ICaptureRunLog
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public List<CaptureRunResultDto> Recorded { get; } = [];

        public CaptureRunLogEntry? LastRunFor(CaptureSourceKind kind) => null;

        public IReadOnlyList<CaptureRunLogEntry> EntriesFor(CaptureSourceKind kind) => [];

        public void Record(CaptureRunResultDto run) => Recorded.Add(run);
    }

    private static CapturedEntry Entry(string id) =>
        new(id, $"Entry {id}", $"https://example.org/{id}", null, Now.AddHours(-1));

    private sealed class FakeSettings : ICaptureSourceSettings
    {
        public event Action? Changed;

        public CaptureSourceSettings Current { get; private set; } = new();

        public string SettingsPath => "memory";

        public string? SetEnabled(CaptureSourceKind kind, bool enabled)
        {
            Current = Current with
            {
                Sources = [.. Current.Sources.Select(source =>
                    source.Kind == kind ? source with { Enabled = enabled } : source)]
            };
            Changed?.Invoke();
            return null;
        }

        public string? SetTargets(CaptureSourceKind kind, IReadOnlyList<string> targets)
        {
            Current = Current with
            {
                Sources = [.. Current.Sources.Select(source =>
                    source.Kind == kind ? source with { Targets = targets } : source)]
            };
            Changed?.Invoke();
            return null;
        }
    }

    /// <summary>The receiving side, remembered rather than stored: an id seen
    /// before is already known, which is exactly the Inbox's rule.</summary>
    private sealed class FakeDelivery : ICaptureDelivery
    {
        private readonly HashSet<Guid> _known = [];

        public List<CaptureItem> Offered { get; } = [];

        public List<CaptureItem> Delivered { get; } = [];

        public CaptureDeliveryOutcome? Answer { get; init; }

        public string? ThrowOn { get; init; }

        public string ThrowMessage { get; init; } = "Delivery failed.";

        public Task<CaptureDeliveryOutcome> DeliverAsync(CaptureItem item, CancellationToken cancellationToken = default)
        {
            Offered.Add(item);

            if (ThrowOn is not null && item.Title == $"Entry {ThrowOn}") throw new InvalidOperationException(ThrowMessage);
            if (Answer is { } answer) return Task.FromResult(answer);
            if (!_known.Add(item.Id)) return Task.FromResult(CaptureDeliveryOutcome.AlreadyKnown);

            Delivered.Add(item);
            return Task.FromResult(CaptureDeliveryOutcome.Delivered);
        }
    }

    private sealed class RecordingAdapter(CaptureSourceKind kind, CaptureSourceFindings findings) : ICaptureSourceAdapter
    {
        public RecordingAdapter(CaptureSourceKind kind, params CapturedEntry[] entries)
            : this(kind, new CaptureSourceFindings(entries, []))
        {
        }

        public CaptureSourceKind Kind => kind;

        public MonitoredSource? Received { get; private set; }

        public Task<CaptureSourceFindings> RunAsync(MonitoredSource source, CancellationToken cancellationToken = default)
        {
            Received = source;
            return Task.FromResult(findings);
        }
    }

    private sealed class ThrowingAdapter(CaptureSourceKind kind, string message) : ICaptureSourceAdapter
    {
        public CaptureSourceKind Kind => kind;

        public Task<CaptureSourceFindings> RunAsync(MonitoredSource source, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(message);
    }

    private sealed class CancellingAdapter(CaptureSourceKind kind, CancellationTokenSource cancellation) : ICaptureSourceAdapter
    {
        public CaptureSourceKind Kind => kind;

        public Task<CaptureSourceFindings> RunAsync(MonitoredSource source, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CaptureSourceFindings.Empty);
        }
    }
}
