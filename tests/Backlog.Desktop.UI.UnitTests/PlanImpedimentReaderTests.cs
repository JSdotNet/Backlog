using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.UI.Goals;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The host's side of the impediment a Blocked plan names (local ADR 0035 §5; local ADR
/// 0062 §2–§3): the open impediment holding back the plan by its tag or one of its
/// entries by id, read again when an impediment is stored, with nothing named when none
/// is linked or the Progress port is not composed.
/// </summary>
public sealed class PlanImpedimentReaderTests
{
    private static readonly Guid Roof = Guid.NewGuid();
    private static readonly Guid Paint = Guid.NewGuid();

    private static ImpedimentDto Impediment(string title, ImpedimentState state, params HeldBackItemDto[] holds) =>
        new(Guid.NewGuid(), title, state, null, null, null, null,
            [new ImpedimentStateChangeDto(ImpedimentState.New, new DateOnly(2026, 11, 20), null)], holds);

    private static async Task<PlanImpedimentReader> ReaderAsync(FakeImpediments impediments)
    {
        var reader = new PlanImpedimentReader(impediments);
        await reader.RefreshAsync();
        return reader;
    }

    [Fact]
    public async Task The_plan_names_the_first_open_impediment_holding_it_or_one_of_its_entries()
    {
        var impediments = new FakeImpediments
        {
            Open =
            [
                Impediment("Unrelated", ImpedimentState.New, HeldBackItemDto.ForTask(Guid.NewGuid())),
                Impediment("Roofer not booked", ImpedimentState.Reported, HeldBackItemDto.ForTask(Roof)),
                Impediment("Budget frozen", ImpedimentState.New, HeldBackItemDto.ForPlan("delta")),
            ],
        };
        using var reader = await ReaderAsync(impediments);

        Assert.Equal("Roofer not booked", reader.Source.TitleOf("+delta", [Roof, Paint]));
        Assert.Equal("Budget frozen", reader.Source.TitleOf("+delta", [Paint]));
        Assert.Equal("Budget frozen", reader.Source.TitleOf("Delta", []));
        Assert.Null(reader.Source.TitleOf("+gamma", [Paint]));
    }

    [Fact]
    public async Task A_resolved_impediment_names_nothing()
    {
        var impediments = new FakeImpediments
        {
            Open = [Impediment("Roofer not booked", ImpedimentState.Resolved, HeldBackItemDto.ForTask(Roof))],
        };
        using var reader = await ReaderAsync(impediments);

        Assert.Null(reader.Source.TitleOf("+delta", [Roof]));
    }

    [Fact]
    public async Task A_stored_impediment_is_read_again_and_the_watchers_told()
    {
        var impediments = new FakeImpediments();
        using var reader = await ReaderAsync(impediments);
        var told = 0;
        using var watch = reader.Source.Watch(() => told++);
        Assert.Null(reader.Source.TitleOf("+delta", [Roof]));

        impediments.Open = [Impediment("Roofer not booked", ImpedimentState.New, HeldBackItemDto.ForTask(Roof))];
        impediments.RaiseChanged();

        await WaitUntilAsync(() => told > 0);
        Assert.Equal("Roofer not booked", reader.Source.TitleOf("+delta", [Roof]));

        reader.Dispose();
        Assert.Equal(0, impediments.Listeners);
    }

    [Fact]
    public async Task A_failed_reading_keeps_the_last_answer()
    {
        var impediments = new FakeImpediments
        {
            Open = [Impediment("Roofer not booked", ImpedimentState.New, HeldBackItemDto.ForTask(Roof))],
        };
        using var reader = await ReaderAsync(impediments);

        impediments.Failure = new Error("progress.unavailable", "down");
        await reader.RefreshAsync();

        Assert.Equal("Roofer not booked", reader.Source.TitleOf("+delta", [Roof]));
    }

    [Fact]
    public void Without_the_Progress_port_no_plan_names_one()
    {
        using var reader = new PlanImpedimentReader(null);

        Assert.Null(reader.Source.TitleOf("+delta", [Roof]));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    private sealed class FakeImpediments : IImpediments
    {
        private Action? _changed;

        public event Action? Changed
        {
            add { _changed += value; Listeners++; }
            remove { _changed -= value; Listeners--; }
        }

        public int Listeners { get; private set; }

        public IReadOnlyList<ImpedimentDto> Open { get; set; } = [];

        public Error? Failure { get; set; }

        public void RaiseChanged() => _changed?.Invoke();

        public Task<Result<IReadOnlyList<ImpedimentDto>>> ListAsync(DateOnly? resolvedFrom = null, DateOnly? resolvedTo = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Failure is { } error ? Result.Failure<IReadOnlyList<ImpedimentDto>>(error) : Result.Success(Open));

        public Task<Result<ImpedimentDto>> GetAsync(Guid impedimentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ImpedimentDto>> RaiseAsync(RaiseImpedimentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ImpedimentDto>> MoveAsync(Guid impedimentId, ImpedimentState state, string? with = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ImpedimentDto>> SetCheckAgainAsync(Guid impedimentId, DateOnly? checkAgain, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ImpedimentDto>> EditNoteAsync(Guid impedimentId, string? note, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ImpedimentDto>> HoldBackAsync(Guid impedimentId, HeldBackItemDto item, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ImpedimentDto>> ReleaseAsync(Guid impedimentId, HeldBackItemDto item, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
