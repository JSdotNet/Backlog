using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.Extensions;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>
/// The published <see cref="IImpediments"/> port as a host composes it through
/// <c>AddProgressModule()</c>: each feature of
/// <c>.devbook/domain/progress/features.md#tracking-an-impediment</c> end to end over an
/// in-memory repository — raise, move, check again, note, hold back and release, and the
/// list of open ones with those resolved in a window.
/// </summary>
public sealed class ImpedimentsTests : IDisposable
{
    private readonly InMemoryImpedimentRepository _repository = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero));
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public ImpedimentsTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton<IImpedimentRepository>(_repository);
        services.AddProgressModule();

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        _scope = _provider.CreateScope();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private IImpediments Impediments => _scope.ServiceProvider.GetRequiredService<IImpediments>();

    private void On(int day) => _time.SetUtcNow(new DateTimeOffset(2026, 10, day, 9, 0, 0, TimeSpan.Zero));

    private async Task<ImpedimentDto> RaiseAsync(string title = "Bank sample files missing", params HeldBackItemDto[] holds) =>
        Assert.IsType<Ok<ImpedimentDto>>((await Impediments.RaiseAsync(new RaiseImpedimentRequest(title, holds), Cancellation)).Value).Value;

    [Fact]
    public async Task Raising_stores_a_New_impediment_dated_today_with_its_items_and_note()
    {
        var task = Guid.NewGuid();

        var raised = Assert.IsType<Ok<ImpedimentDto>>((await Impediments.RaiseAsync(
            new RaiseImpedimentRequest(
                "Bank sample files missing",
                [HeldBackItemDto.ForTask(task), HeldBackItemDto.ForPullRequest(" acme/payments ", 412), HeldBackItemDto.ForPlan("+goals-standup")],
                "Asked twice"),
            Cancellation)).Value).Value;

        Assert.Equal(ImpedimentState.New, raised.State);
        Assert.Equal(new DateOnly(2026, 10, 14), raised.RaisedOn);
        Assert.Equal(
            [HeldBackItemDto.ForTask(task), HeldBackItemDto.ForPullRequest("acme/payments", 412), HeldBackItemDto.ForPlan("goals-standup")],
            raised.HoldsBack);
        Assert.Equal([task], raised.HeldBackTasks);
        Assert.Equal("Asked twice", raised.Note);
        Assert.Equivalent(raised, Assert.IsType<Ok<ImpedimentDto>>((await Impediments.GetAsync(raised.Id, Cancellation)).Value).Value, strict: true);
    }

    [Fact]
    public async Task An_item_without_its_identity_refuses_the_raise_and_stores_nothing()
    {
        var raised = await Impediments.RaiseAsync(
            new RaiseImpedimentRequest("Bank sample files missing", [HeldBackItemDto.ForTask(Guid.NewGuid()), new HeldBackItemDto(HeldBackKind.PullRequest, Number: 412)]),
            Cancellation);

        Assert.Equal("progress.held_back_item_without_identity", Assert.IsType<Error>(raised.Value).Code);
        Assert.Equal(0, _repository.Saves);
    }

    /// <summary>Requirements scenarios "A pull request no task covers", "A plan", "Linking
    /// twice" and "Unlinking", through the port.</summary>
    [Fact]
    public async Task Pull_requests_and_plans_are_linked_once_and_unlinked_leaving_the_history_alone()
    {
        var bank = await RaiseAsync();
        On(15);
        await Impediments.MoveAsync(bank.Id, ImpedimentState.Reported, "bank support", Cancellation);

        await Impediments.HoldBackAsync(bank.Id, HeldBackItemDto.ForPullRequest("acme/payments", 412), Cancellation);
        await Impediments.HoldBackAsync(bank.Id, HeldBackItemDto.ForPlan("goals-standup"), Cancellation);
        var twice = Assert.IsType<Ok<ImpedimentDto>>((await Impediments.HoldBackAsync(bank.Id, HeldBackItemDto.ForPullRequest("ACME/Payments", 412), Cancellation)).Value).Value;

        Assert.Equal([HeldBackItemDto.ForPullRequest("acme/payments", 412), HeldBackItemDto.ForPlan("goals-standup")], twice.HoldsBack);
        Assert.Equal([ImpedimentState.New, ImpedimentState.Reported], twice.History.Select(step => step.State));

        var released = Assert.IsType<Ok<ImpedimentDto>>((await Impediments.ReleaseAsync(bank.Id, HeldBackItemDto.ForPullRequest("acme/payments", 412), Cancellation)).Value).Value;
        Assert.Equal([HeldBackItemDto.ForPlan("goals-standup")], released.HoldsBack);
        Assert.Equal(ImpedimentState.Reported, released.State);
    }

    [Fact]
    public async Task Linking_or_unlinking_an_item_without_its_identity_is_refused_and_changes_nothing()
    {
        var bank = await RaiseAsync();
        var saves = _repository.Saves;

        Assert.Equal(
            "progress.held_back_item_without_identity",
            Assert.IsType<Error>((await Impediments.HoldBackAsync(bank.Id, new HeldBackItemDto(HeldBackKind.PullRequest, Number: 412), Cancellation)).Value).Code);
        Assert.Equal(
            "progress.held_back_item_without_identity",
            Assert.IsType<Error>((await Impediments.ReleaseAsync(bank.Id, HeldBackItemDto.ForPlan("+"), Cancellation)).Value).Code);
        Assert.Equal(saves, _repository.Saves);
    }

    [Fact]
    public async Task Raising_without_a_title_is_refused_and_stores_nothing()
    {
        Assert.Equal("progress.impediment_without_title", Assert.IsType<Error>((await Impediments.RaiseAsync(new RaiseImpedimentRequest(" "), Cancellation)).Value).Code);
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
        var escalated = Assert.IsType<Ok<ImpedimentDto>>((await Impediments.MoveAsync(bank.Id, ImpedimentState.Escalated, "account manager", Cancellation)).Value).Value;

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

        var handled = Assert.IsType<Ok<ImpedimentDto>>((await Impediments.MoveAsync(runner.Id, ImpedimentState.BeingHandled, "platform team", Cancellation)).Value).Value;

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

        Assert.Equal("progress.impediment_move_not_allowed", Assert.IsType<Error>(back.Value).Code);
        Assert.Equal(saves, _repository.Saves);
        Assert.Equal(ImpedimentState.Escalated, Assert.IsType<Ok<ImpedimentDto>>((await Impediments.GetAsync(bank.Id, Cancellation)).Value).Value.State);
    }

    /// <summary>Requirements scenarios "Resolved while New" and "A resolved impediment stays resolved".</summary>
    [Fact]
    public async Task A_New_impediment_resolves_and_then_stays_resolved()
    {
        var credit = await RaiseAsync("Credit note numbering unclear");

        var resolved = Assert.IsType<Ok<ImpedimentDto>>((await Impediments.MoveAsync(credit.Id, ImpedimentState.Resolved, cancellationToken: Cancellation)).Value).Value;
        Assert.Equal([ImpedimentState.New, ImpedimentState.Resolved], resolved.History.Select(step => step.State));

        Assert.Equal(
            "progress.impediment_resolved",
            Assert.IsType<Error>((await Impediments.MoveAsync(credit.Id, ImpedimentState.Reported, cancellationToken: Cancellation)).Value).Code);
    }

    [Fact]
    public async Task Check_again_note_and_held_back_tasks_change_and_are_kept()
    {
        var bank = await RaiseAsync();
        var task = Guid.NewGuid();

        await Impediments.SetCheckAgainAsync(bank.Id, new DateOnly(2026, 10, 20), Cancellation);
        await Impediments.EditNoteAsync(bank.Id, "Chased by phone", Cancellation);
        await Impediments.HoldBackAsync(bank.Id, HeldBackItemDto.ForTask(task), Cancellation);
        var held = Assert.IsType<Ok<ImpedimentDto>>((await Impediments.HoldBackAsync(bank.Id, HeldBackItemDto.ForTask(task), Cancellation)).Value).Value;

        Assert.Equal(new DateOnly(2026, 10, 20), held.CheckAgain);
        Assert.Equal("Chased by phone", held.Note);
        Assert.Equal([HeldBackItemDto.ForTask(task)], held.HoldsBack);

        var released = Assert.IsType<Ok<ImpedimentDto>>((await Impediments.ReleaseAsync(bank.Id, HeldBackItemDto.ForTask(task), Cancellation)).Value).Value;
        Assert.Empty(released.HoldsBack);
        Assert.Equivalent(released, Assert.IsType<Ok<ImpedimentDto>>((await Impediments.GetAsync(bank.Id, Cancellation)).Value).Value, strict: true);
    }

    [Fact]
    public async Task A_change_to_an_unknown_impediment_is_refused()
    {
        var id = Guid.NewGuid();

        Assert.Equal("progress.impediment_not_found", Assert.IsType<Error>((await Impediments.GetAsync(id, Cancellation)).Value).Code);
        Assert.Equal("progress.impediment_not_found", Assert.IsType<Error>((await Impediments.MoveAsync(id, ImpedimentState.Reported, cancellationToken: Cancellation)).Value).Code);
        Assert.Equal("progress.impediment_not_found", Assert.IsType<Error>((await Impediments.EditNoteAsync(id, "x", Cancellation)).Value).Code);
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

        var openOnly = Assert.IsType<Ok<IReadOnlyList<ImpedimentDto>>>((await Impediments.ListAsync(cancellationToken: Cancellation)).Value).Value;
        Assert.Equal([open.Id], openOnly.Select(impediment => impediment.Id));

        var window = Assert.IsType<Ok<IReadOnlyList<ImpedimentDto>>>((await Impediments.ListAsync(new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 16), Cancellation)).Value).Value;
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
