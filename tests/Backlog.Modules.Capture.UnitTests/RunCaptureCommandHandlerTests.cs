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
/// What is under test is the shape of the answer rather than any capture: no
/// adapter ships yet, so the run's whole job today is to say, per enabled
/// source, that nothing could be fetched and why — and to keep saying it for the
/// other sources when one of them blows up.
/// </para>
/// </summary>
public sealed class RunCaptureCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Nothing_enabled_reports_an_empty_run()
    {
        var handler = Handler(new FakeSettings());

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
        var handler = Handler(settings);

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
        var handler = Handler(settings);

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
        var adapter = new RecordingAdapter(CaptureSourceKind.YouTube, newItems: 3);
        var handler = Handler(settings, adapter);

        var result = await handler.Handle(new RunCaptureCommand(), TestContext.Current.CancellationToken);

        Assert.NotNull(adapter.Received);
        Assert.Equal(["https://www.youtube.com/@dotnet"], adapter.Received.Targets);

        var source = Assert.Single(result.Value.Sources);
        Assert.Equal(3, source.NewItems);
        Assert.Equal(3, result.Value.TotalNewItems);
    }

    [Fact]
    public async Task An_adapter_that_throws_does_not_fail_the_run()
    {
        var settings = new FakeSettings();
        settings.SetEnabled(CaptureSourceKind.YouTube, true);
        settings.SetEnabled(CaptureSourceKind.Website, true);
        var handler = Handler(settings, new ThrowingAdapter(CaptureSourceKind.YouTube, "The feed timed out."));

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

    private static RunCaptureCommandHandler Handler(ICaptureSourceSettings settings, params ICaptureSourceAdapter[] adapters) =>
        new(settings, adapters, new FakeTimeProvider(Now));

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

    private sealed class RecordingAdapter(CaptureSourceKind kind, int newItems) : ICaptureSourceAdapter
    {
        public CaptureSourceKind Kind => kind;

        public MonitoredSource? Received { get; private set; }

        public Task<CaptureRunSourceResult> RunAsync(MonitoredSource source, CancellationToken cancellationToken = default)
        {
            Received = source;
            return Task.FromResult(new CaptureRunSourceResult(kind, newItems, $"{newItems} new."));
        }
    }

    private sealed class ThrowingAdapter(CaptureSourceKind kind, string message) : ICaptureSourceAdapter
    {
        public CaptureSourceKind Kind => kind;

        public Task<CaptureRunSourceResult> RunAsync(MonitoredSource source, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(message);
    }
}
