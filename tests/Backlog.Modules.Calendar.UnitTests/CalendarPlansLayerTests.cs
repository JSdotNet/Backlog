using Backlog.Modules.Calendar.Abstractions.DataTransferObjects;
using Backlog.Modules.Calendar.Abstractions.Services;
using Backlog.Modules.Calendar.Extensions;
using Backlog.Modules.Calendar.Services;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Calendar.UnitTests;

/// <summary>
/// The Plans layer's read policy (local ADR 0055 §3): a read of the roadmap's plans
/// gathers the backlog per plan, so the Calendar reads only when one is owed — on
/// arriving, after the plans or a feature changed, and on a new day — and a read that
/// fails leaves the tasks on the Calendar with no bars.
/// </summary>
public sealed class CalendarPlansLayerTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_a_port_nothing_is_offered_read_or_started()
    {
        using var layer = new CalendarPlansLayer(null, null);

        Assert.False(layer.Offered);
        Assert.False(layer.Available);
        await layer.ReadIfOwedAsync(Today, Ct);
        Assert.Same(CalendarPlansDto.Empty, layer.Plans);
        Assert.Null(await layer.StartAsync("alpha", Today, Ct));
    }

    [Fact]
    public async Task The_first_read_is_owed_and_the_same_day_reads_once()
    {
        var port = new FakePlans();
        using var layer = new CalendarPlansLayer(port, null);

        await layer.ReadIfOwedAsync(Today, Ct);
        await layer.ReadIfOwedAsync(Today, Ct);

        Assert.Equal(1, port.Reads);
        Assert.Equal("Alpha", Assert.Single(layer.Plans.Windows).Title);
        Assert.True(layer.Available);
    }

    [Fact]
    public async Task A_new_day_reads_again()
    {
        var port = new FakePlans();
        using var layer = new CalendarPlansLayer(port, null);
        await layer.ReadIfOwedAsync(Today, Ct);

        await layer.ReadIfOwedAsync(Today.AddDays(1), Ct);

        Assert.Equal(2, port.Reads);
    }

    [Fact]
    public async Task Arriving_reads_again_whatever_was_read_before()
    {
        var port = new FakePlans();
        using var layer = new CalendarPlansLayer(port, null);
        await layer.ReadIfOwedAsync(Today, Ct);

        layer.Arrive();

        await layer.ReadIfOwedAsync(Today, Ct);
        Assert.Equal(2, port.Reads);
    }

    [Fact]
    public async Task A_change_from_the_port_owes_a_read_and_says_so()
    {
        var port = new FakePlans();
        using var layer = new CalendarPlansLayer(port, null);
        await layer.ReadIfOwedAsync(Today, Ct);
        var said = 0;
        layer.Changed += () => said++;

        port.RaiseChanged();

        Assert.Equal(1, said);
        await layer.ReadIfOwedAsync(Today, Ct);
        Assert.Equal(2, port.Reads);
    }

    [Fact]
    public async Task A_feature_switch_owes_a_read_and_says_so()
    {
        var port = new FakePlans();
        var features = new FakeFeatures();
        using var layer = new CalendarPlansLayer(port, features);
        await layer.ReadIfOwedAsync(Today, Ct);
        var said = 0;
        layer.Changed += () => said++;

        features.SetEnabled("roadmap", false);

        Assert.Equal(1, said);
        await layer.ReadIfOwedAsync(Today, Ct);
        Assert.Equal(2, port.Reads);
    }

    /// <summary>Today's behaviour, kept: a failed read is <see cref="CalendarPlansDto.Empty"/>,
    /// which still says there is a roadmap, so the box stays and only the bars go.</summary>
    [Fact]
    public async Task A_read_that_fails_leaves_the_plans_empty_and_still_available()
    {
        var port = new FakePlans();
        using var layer = new CalendarPlansLayer(port, null);
        await layer.ReadIfOwedAsync(Today, Ct);

        port.Failure = new IOException("The roadmap could not be read.");
        await layer.ReadAsync(Today, Ct);

        Assert.Same(CalendarPlansDto.Empty, layer.Plans);
        Assert.True(layer.Available);
    }

    [Fact]
    public async Task A_cancelled_read_is_not_swallowed()
    {
        var port = new FakePlans { Failure = new OperationCanceledException() };
        using var layer = new CalendarPlansLayer(port, null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => layer.ReadAsync(Today, Ct));
    }

    [Fact]
    public async Task A_start_passes_the_tag_and_day_and_answers_the_refusal()
    {
        var port = new FakePlans { Refusal = "+alpha is no longer waiting on the shelf." };
        using var layer = new CalendarPlansLayer(port, null);

        var refusal = await layer.StartAsync("alpha", Today.AddDays(3), Ct);

        Assert.Equal(("alpha", Today.AddDays(3)), port.Started);
        Assert.Equal("+alpha is no longer waiting on the shelf.", refusal);
    }

    [Fact]
    public void Disposing_stops_listening()
    {
        var port = new FakePlans();
        var features = new FakeFeatures();
        var layer = new CalendarPlansLayer(port, features);

        layer.Dispose();

        Assert.Equal(0, port.Listeners);
        Assert.Equal(0, features.Listeners);
    }

    /// <summary>One layer per window, as the port it reads is: Scoped, resolved through a
    /// validating container with and without a port composed.</summary>
    [Fact]
    public void AddCalendarModule_resolves_one_layer_per_scope_and_is_safe_to_call_twice()
    {
        var services = new ServiceCollection().AddCalendarModule();
        var once = services.Count;
        services.AddCalendarModule();
        Assert.Equal(once, services.Count);

        services.AddScoped<ICalendarPlans, FakePlans>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var layer = first.ServiceProvider.GetRequiredService<ICalendarPlansLayer>();

        Assert.True(layer.Offered);
        Assert.Same(layer, first.ServiceProvider.GetRequiredService<ICalendarPlansLayer>());
        Assert.NotSame(layer, second.ServiceProvider.GetRequiredService<ICalendarPlansLayer>());
    }

    [Fact]
    public void AddCalendarModule_without_a_port_resolves_a_layer_that_offers_no_plans()
    {
        using var provider = new ServiceCollection().AddCalendarModule()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        Assert.False(scope.ServiceProvider.GetRequiredService<ICalendarPlansLayer>().Offered);
    }

    private sealed class FakePlans : ICalendarPlans
    {
        public event Action? Changed;

        public int Reads { get; private set; }

        public CalendarPlansDto Answer { get; set; } = new(
            [new CalendarPlanWindowDto(Guid.NewGuid(), "Alpha", "alpha", Today, Today.AddDays(4), ["repo"], 1, 3)],
            [],
            []);

        public Exception? Failure { get; set; }

        public string? Refusal { get; set; }

        public (string Tag, DateOnly Start)? Started { get; private set; }

        public int Listeners => Changed?.GetInvocationList().Length ?? 0;

        public Task<CalendarPlansDto> ReadAsync(DateOnly today, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Failure is { } failure ? Task.FromException<CalendarPlansDto>(failure) : Task.FromResult(Answer);
        }

        public Task<string?> StartAsync(string tag, DateOnly start, CancellationToken cancellationToken = default)
        {
            Started = (tag, start);
            return Task.FromResult(Refusal);
        }

        public void RaiseChanged() => Changed?.Invoke();
    }

    private sealed class FakeFeatures : IAppFeatureSettings
    {
        public event Action? Changed;

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "features.json";

        public int Listeners => Changed?.GetInvocationList().Length ?? 0;

        public bool IsEnabled(string key) => true;

        public string? SetEnabled(string key, bool enabled)
        {
            Changed?.Invoke();
            return null;
        }
    }
}
