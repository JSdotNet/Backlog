using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.Extensions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>
/// The published <see cref="IImpediments"/> port as a host composes it through
/// <c>AddProgressModule()</c>: the goals switch, and each feature of
/// <c>.devbook/domain/progress/features.md#tracking-an-impediment</c> end to end over an
/// in-memory repository — raise, move, check again, note, hold back and release, and the
/// list of open ones with those resolved in a window.
/// </summary>
public sealed class ImpedimentsTests : IDisposable
{
    private readonly InMemoryImpedimentRepository _repository = new();
    private readonly FakeGoalFeatures _features = new(goalsOn: true);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero));
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public ImpedimentsTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton<IImpedimentRepository>(_repository);
        services.AddSingleton<IAppFeatureSettings>(_features);
        services.AddProgressModule();

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        _scope = _provider.CreateScope();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private IImpediments Impediments => _scope.ServiceProvider.GetRequiredService<IImpediments>();

    private void On(int day) => _time.SetUtcNow(new DateTimeOffset(2026, 10, day, 9, 0, 0, TimeSpan.Zero));

    private async Task<ImpedimentDto> RaiseAsync(string title = "Bank sample files missing", params Guid[] holds) =>
        (await Impediments.RaiseAsync(new RaiseImpedimentRequest(title, holds), Cancellation)).Value;

    [Fact]
    public async Task With_goals_off_every_call_answers_off_and_nothing_is_written()
    {
        _features.GoalsOn = false;
        var id = Guid.NewGuid();

        Assert.Equal("progress.goals_off", (await Impediments.RaiseAsync(new RaiseImpedimentRequest("x"), Cancellation)).Error.Code);
        Assert.Equal("progress.goals_off", (await Impediments.ListAsync(cancellationToken: Cancellation)).Error.Code);
        Assert.Equal("progress.goals_off", (await Impediments.GetAsync(id, Cancellation)).Error.Code);
        Assert.Equal("progress.goals_off", (await Impediments.MoveAsync(id, ImpedimentState.Reported, cancellationToken: Cancellation)).Error.Code);
        Assert.Equal("progress.goals_off", (await Impediments.SetCheckAgainAsync(id, null, Cancellation)).Error.Code);
        Assert.Equal("progress.goals_off", (await Impediments.EditNoteAsync(id, null, Cancellation)).Error.Code);
        Assert.Equal("progress.goals_off", (await Impediments.HoldBackAsync(id, Guid.NewGuid(), Cancellation)).Error.Code);
        Assert.Equal("progress.goals_off", (await Impediments.ReleaseAsync(id, Guid.NewGuid(), Cancellation)).Error.Code);
        Assert.Equal(0, _repository.Saves);
    }

    [Fact]
    public async Task Raising_stores_a_New_impediment_dated_today_with_its_tasks_and_note()
    {
        var task = Guid.NewGuid();

        var raised = (await Impediments.RaiseAsync(new RaiseImpedimentRequest("Bank sample files missing", [task], "Asked twice"), Cancellation)).Value;

        Assert.Equal(ImpedimentState.New, raised.State);
        Assert.Equal(new DateOnly(2026, 10, 14), raised.RaisedOn);
        Assert.Equal([task], raised.HoldsBack);
        Assert.Equal("Asked twice", raised.Note);
        Assert.Equivalent(raised, (await Impediments.GetAsync(raised.Id, Cancellation)).Value, strict: true);
    }

    [Fact]
    public async Task Raising_without_a_title_is_refused_and_stores_nothing()
    {
        Assert.Equal("progress.impediment_without_title", (await Impediments.RaiseAsync(new RaiseImpedimentRequest(" "), Cancellation)).Error.Code);
        Assert.Equal(0, _repository.Saves);
    }

    /// <summary>Requirements scenario "Escalating on a Friday", through the port.</summary>
    [Fact]
    public async Task Each_move_is_dated_today_and_records_who_it_is_with()
    {
        var bank = await RaiseAsync();
        On(15);
        await Impediments.MoveAsync(bank.Id, ImpedimentState.Reported, "bank support", Cancellation);
        On(16);
        var escalated = (await Impediments.MoveAsync(bank.Id, ImpedimentState.Escalated, "account manager", Cancellation)).Value;

        Assert.Equal(
            [
                new ImpedimentStateChangeDto(ImpedimentState.New, new DateOnly(2026, 10, 14), null),
                new ImpedimentStateChangeDto(ImpedimentState.Reported, new DateOnly(2026, 10, 15), "bank support"),
                new ImpedimentStateChangeDto(ImpedimentState.Escalated, new DateOnly(2026, 10, 16), "account manager"),
            ],
            escalated.History);
        Assert.Equal("account manager", escalated.ReportedTo);
    }

    /// <summary>Requirements scenario "Skipping Escalated".</summary>
    [Fact]
    public async Task A_reported_impediment_moves_straight_to_Being_handled()
    {
        var runner = await RaiseAsync("Android build runner flaky");
        await Impediments.MoveAsync(runner.Id, ImpedimentState.Reported, "platform team", Cancellation);

        var handled = (await Impediments.MoveAsync(runner.Id, ImpedimentState.BeingHandled, "platform team", Cancellation)).Value;

        Assert.Equal(
            [ImpedimentState.New, ImpedimentState.Reported, ImpedimentState.BeingHandled],
            handled.History.Select(step => step.State));
    }

    /// <summary>Requirements scenario "Moving back".</summary>
    [Fact]
    public async Task A_refused_move_stores_nothing()
    {
        var bank = await RaiseAsync();
        await Impediments.MoveAsync(bank.Id, ImpedimentState.Reported, cancellationToken: Cancellation);
        await Impediments.MoveAsync(bank.Id, ImpedimentState.Escalated, cancellationToken: Cancellation);
        var saves = _repository.Saves;

        var back = await Impediments.MoveAsync(bank.Id, ImpedimentState.Reported, cancellationToken: Cancellation);

        Assert.Equal("progress.impediment_move_not_allowed", back.Error.Code);
        Assert.Equal(saves, _repository.Saves);
        Assert.Equal(ImpedimentState.Escalated, (await Impediments.GetAsync(bank.Id, Cancellation)).Value.State);
    }

    /// <summary>Requirements scenarios "Resolved while New" and "A resolved impediment stays resolved".</summary>
    [Fact]
    public async Task A_New_impediment_resolves_and_then_stays_resolved()
    {
        var credit = await RaiseAsync("Credit note numbering unclear");

        var resolved = (await Impediments.MoveAsync(credit.Id, ImpedimentState.Resolved, cancellationToken: Cancellation)).Value;
        Assert.Equal([ImpedimentState.New, ImpedimentState.Resolved], resolved.History.Select(step => step.State));

        Assert.Equal(
            "progress.impediment_resolved",
            (await Impediments.MoveAsync(credit.Id, ImpedimentState.Reported, cancellationToken: Cancellation)).Error.Code);
    }

    [Fact]
    public async Task Check_again_note_and_held_back_tasks_change_and_are_kept()
    {
        var bank = await RaiseAsync();
        var task = Guid.NewGuid();

        await Impediments.SetCheckAgainAsync(bank.Id, new DateOnly(2026, 10, 20), Cancellation);
        await Impediments.EditNoteAsync(bank.Id, "Chased by phone", Cancellation);
        await Impediments.HoldBackAsync(bank.Id, task, Cancellation);
        var held = (await Impediments.HoldBackAsync(bank.Id, task, Cancellation)).Value;

        Assert.Equal(new DateOnly(2026, 10, 20), held.CheckAgain);
        Assert.Equal("Chased by phone", held.Note);
        Assert.Equal([task], held.HoldsBack);

        var released = (await Impediments.ReleaseAsync(bank.Id, task, Cancellation)).Value;
        Assert.Empty(released.HoldsBack);
        Assert.Equivalent(released, (await Impediments.GetAsync(bank.Id, Cancellation)).Value, strict: true);
    }

    [Fact]
    public async Task A_change_to_an_unknown_impediment_is_refused()
    {
        var id = Guid.NewGuid();

        Assert.Equal("progress.impediment_not_found", (await Impediments.GetAsync(id, Cancellation)).Error.Code);
        Assert.Equal("progress.impediment_not_found", (await Impediments.MoveAsync(id, ImpedimentState.Reported, cancellationToken: Cancellation)).Error.Code);
        Assert.Equal("progress.impediment_not_found", (await Impediments.EditNoteAsync(id, "x", Cancellation)).Error.Code);
    }

    [Fact]
    public async Task The_list_holds_the_open_ones_and_those_resolved_in_the_window()
    {
        var open = await RaiseAsync("Bank sample files missing");
        var early = await RaiseAsync("Old blocker");
        await Impediments.MoveAsync(early.Id, ImpedimentState.Resolved, cancellationToken: Cancellation); // 14 Oct
        On(16);
        var late = await RaiseAsync("Credit note numbering unclear");
        await Impediments.MoveAsync(late.Id, ImpedimentState.Resolved, cancellationToken: Cancellation); // 16 Oct

        var openOnly = (await Impediments.ListAsync(cancellationToken: Cancellation)).Value;
        Assert.Equal([open.Id], openOnly.Select(impediment => impediment.Id));

        var window = (await Impediments.ListAsync(new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 16), Cancellation)).Value;
        Assert.Equal([open.Id, late.Id], window.Select(impediment => impediment.Id));
    }

    [Fact]
    public async Task Every_save_raises_Changed_and_a_refused_change_does_not()
    {
        var heard = 0;
        Impediments.Changed += () => heard++;

        var bank = await RaiseAsync();
        await Impediments.MoveAsync(bank.Id, ImpedimentState.Reported, cancellationToken: Cancellation);
        await Impediments.MoveAsync(bank.Id, ImpedimentState.New, cancellationToken: Cancellation);

        Assert.Equal(2, heard);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
