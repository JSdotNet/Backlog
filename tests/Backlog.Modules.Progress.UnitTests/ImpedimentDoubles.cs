using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.DomainModels;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>The impediments kept in memory, saved whole and handed back as stored, in the
/// order they were first saved.</summary>
internal sealed class InMemoryImpedimentRepository : IImpedimentRepository
{
    private readonly List<Impediment> _all = [];

    public int Saves { get; private set; }

    public IReadOnlyList<Impediment> All => _all;

    public Task<IReadOnlyList<Impediment>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Impediment>>([.. _all]);

    public Task<Impediment?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_all.FirstOrDefault(impediment => impediment.Id == id));

    public Task SaveAsync(Impediment impediment, CancellationToken cancellationToken = default)
    {
        Saves++;
        var index = _all.FindIndex(existing => existing.Id == impediment.Id);
        if (index < 0) _all.Add(impediment);
        else _all[index] = impediment;
        return Task.CompletedTask;
    }
}

/// <summary>The tasks marked blocked, as Tasks would answer them.</summary>
internal sealed class FakeBlockedTasks : IBlockedTaskSource
{
    public List<BlockedTaskDto> Blocked { get; } = [];

    public Task<IReadOnlyList<BlockedTaskDto>> ReadBlockedAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BlockedTaskDto>>([.. Blocked]);
}

/// <summary>The impediments the scenarios use, on the requirements' days in October 2026.</summary>
internal static class MakeImpediment
{
    public static readonly DateOnly Wed14 = new(2026, 10, 14);
    public static readonly DateOnly Thu15 = new(2026, 10, 15);
    public static readonly DateOnly Fri16 = new(2026, 10, 16);

    public static Impediment Raised(string title = "Bank sample files missing", DateOnly? on = null) =>
        Impediment.Raise(Guid.NewGuid(), title, on ?? Wed14).Value;

    /// <summary>An impediment moved through <paramref name="path"/>, one step a day from
    /// <see cref="Wed14"/>.</summary>
    public static Impediment At(params Abstractions.ImpedimentState[] path)
    {
        var impediment = Raised();
        var day = Wed14;
        foreach (var state in path)
        {
            day = day.AddDays(1);
            Assert.True(impediment.MoveTo(state, day, "someone").IsSuccess);
        }

        return impediment;
    }
}
