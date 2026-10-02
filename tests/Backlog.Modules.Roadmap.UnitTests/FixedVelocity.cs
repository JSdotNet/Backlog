using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.SharedKernel;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// A pace the test sets: one global figure, and optionally one per repository alias,
/// resolved by the same <see cref="PacesInUseDto.For"/> placement uses — so a handler
/// under test is asked the question the real service answers, and nothing else.
/// </summary>
internal sealed class FixedVelocity(decimal storyPointsPerWeek) : IPlanningVelocity
{
    /// <summary>The global pace — what an item filed under no configured repository
    /// is placed at.</summary>
    public decimal StoryPointsPerWeek { get; set; } = storyPointsPerWeek;

    /// <summary>Per configured repository alias, its pace in use.</summary>
    public Dictionary<string, decimal> ByRepository { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The working week the paces are counted in (local ADR 0019).</summary>
    public WorkingHours Week { get; set; } = WorkingHours.Default;

    /// <summary>How many times the paces were read — one read per handler call is
    /// the contract, however many items it places.</summary>
    public int Reads { get; private set; }

    public Task<PacesInUseDto> ReadPacesInUseAsync(CancellationToken cancellationToken = default)
    {
        Reads++;
        return Task.FromResult(new PacesInUseDto(
            StoryPointsPerWeek,
            new Dictionary<string, decimal>(ByRepository, StringComparer.OrdinalIgnoreCase))
        {
            Week = Week
        });
    }

    public async Task<decimal> GetStoryPointsPerWeekAsync(
        IReadOnlyCollection<string> repositoryAliases,
        CancellationToken cancellationToken = default) =>
        (await ReadPacesInUseAsync(cancellationToken)).For(repositoryAliases);
}
