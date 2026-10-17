using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.Extensions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>
/// The <c>.devbook/domain/progress/domain.md#blocked-task-policy</c> as a host composes it:
/// the requirements "A blocked task raises one New impediment" and "Unblocking a task
/// leaves its impediment" (<c>.devbook/domain/progress/requirements.md#tracking-an-impediment</c>),
/// scenario by scenario.
/// </summary>
public sealed class BlockedTaskPolicyTests : IDisposable
{
    private static readonly Guid BankImport = Guid.NewGuid();

    private readonly InMemoryImpedimentRepository _repository = new();
    private readonly FakeBlockedTasks _tasks = new();
    private readonly FakeGoalFeatures _features = new(goalsOn: true);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero));
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public BlockedTaskPolicyTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton<IImpedimentRepository>(_repository);
        services.AddSingleton<IBlockedTaskSource>(_tasks);
        services.AddSingleton<IAppFeatureSettings>(_features);
        services.AddProgressModule();

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        _scope = _provider.CreateScope();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private IBlockedTaskPolicy Policy => _scope.ServiceProvider.GetRequiredService<IBlockedTaskPolicy>();

    private IImpediments Impediments => _scope.ServiceProvider.GetRequiredService<IImpediments>();

    private static DateOnly Today => new(2026, 10, 17);

    private void Block(DateOnly since) =>
        _tasks.Blocked.Add(new BlockedTaskDto(BankImport, "Bank import mapping", since));

    /// <summary>Scenario "Marking a task blocked".</summary>
    [Fact]
    public async Task A_task_marked_blocked_raises_a_New_impediment_titled_after_it_that_holds_it_back()
    {
        Block(Today);

        var raised = Assert.Single(await Policy.ReactAsync(Cancellation));

        Assert.Equal("Bank import mapping", raised.Title);
        Assert.Equal(ImpedimentState.New, raised.State);
        Assert.Equal([BankImport], raised.HoldsBack);
        Assert.Equal(Today, raised.RaisedOn);
        Assert.Single(_repository.All);
    }

    [Fact]
    public async Task Running_again_raises_nothing_more()
    {
        Block(Today);
        await Policy.ReactAsync(Cancellation);

        Assert.Empty(await Policy.ReactAsync(Cancellation));
        Assert.Single(_repository.All);
    }

    /// <summary>Scenario "Already held back".</summary>
    [Fact]
    public async Task A_task_an_open_impediment_holds_back_raises_nothing()
    {
        var bank = (await Impediments.RaiseAsync(new RaiseImpedimentRequest("Bank sample files missing", [BankImport]), Cancellation)).Value;
        await Impediments.MoveAsync(bank.Id, ImpedimentState.Reported, "bank support", Cancellation);
        await Impediments.MoveAsync(bank.Id, ImpedimentState.Escalated, "account manager", Cancellation);

        // Marked blocked after the impediment was raised: still the open one tracks it.
        Block(Today.AddDays(2));

        Assert.Empty(await Policy.ReactAsync(Cancellation));
        Assert.Single(_repository.All);
    }

    /// <summary>Scenario "Held back only by a resolved impediment".</summary>
    [Fact]
    public async Task A_task_marked_blocked_again_after_its_impediment_was_resolved_raises_a_new_one()
    {
        // Raised on Wednesday 14 October and resolved the next day.
        var old = MakeImpediment.Raised();
        old.HoldBack(BankImport);
        Assert.True(old.MoveTo(ImpedimentState.Resolved, MakeImpediment.Thu15).IsSuccess);
        await _repository.SaveAsync(old, Cancellation);

        Block(Today);

        var raised = Assert.Single(await Policy.ReactAsync(Cancellation));
        Assert.Equal("Bank import mapping", raised.Title);
        Assert.NotEqual(old.Id, raised.Id);
        Assert.Equal(2, _repository.All.Count);
    }

    [Fact]
    public async Task Resolving_the_raised_impediment_while_the_task_is_still_blocked_does_not_raise_it_again()
    {
        Block(Today);
        var raised = Assert.Single(await Policy.ReactAsync(Cancellation));

        await Impediments.MoveAsync(raised.Id, ImpedimentState.Resolved, cancellationToken: Cancellation);

        Assert.Empty(await Policy.ReactAsync(Cancellation));
    }

    /// <summary>Scenario "The token is removed".</summary>
    [Fact]
    public async Task Unblocking_a_task_leaves_its_impediment_as_it_was()
    {
        var bank = (await Impediments.RaiseAsync(new RaiseImpedimentRequest("Bank sample files missing", [BankImport]), Cancellation)).Value;
        await Impediments.MoveAsync(bank.Id, ImpedimentState.Reported, "bank support", Cancellation);
        await Impediments.MoveAsync(bank.Id, ImpedimentState.BeingHandled, "bank", Cancellation);
        Block(Today);
        await Policy.ReactAsync(Cancellation);

        _tasks.Blocked.Clear();
        Assert.Empty(await Policy.ReactAsync(Cancellation));

        var still = (await Impediments.GetAsync(bank.Id, Cancellation)).Value;
        Assert.Equal(ImpedimentState.BeingHandled, still.State);
        Assert.Equal([BankImport], still.HoldsBack);
    }

    [Fact]
    public async Task With_goals_off_nothing_is_raised()
    {
        _features.GoalsOn = false;
        Block(Today);

        Assert.Empty(await Policy.ReactAsync(Cancellation));
        Assert.Empty(_repository.All);
    }

    [Fact]
    public async Task Each_blocked_task_gets_its_own_impediment()
    {
        Block(Today);
        var other = Guid.NewGuid();
        _tasks.Blocked.Add(new BlockedTaskDto(other, "Credit note numbering", Today));

        var raised = await Policy.ReactAsync(Cancellation);

        Assert.Equal(["Bank import mapping", "Credit note numbering"], raised.Select(impediment => impediment.Title));
        Assert.Equal([[BankImport], [other]], raised.Select(impediment => impediment.HoldsBack.ToArray()));
    }

    [Fact]
    public async Task Two_overlapping_runs_raise_one()
    {
        Block(Today);
        using var other = _provider.CreateScope();
        var second = other.ServiceProvider.GetRequiredService<IBlockedTaskPolicy>();

        var both = await Task.WhenAll(Policy.ReactAsync(Cancellation), second.ReactAsync(Cancellation));

        Assert.Equal(1, both.Sum(raised => raised.Count));
        Assert.Single(_repository.All);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
