using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Tasks.Abstractions;

/// <summary>
/// One day's planned hours merged into an entry's blocks — the rule the Tasks detail panel
/// and the Calendar (local ADR 0056 §2) both write by, so the two never disagree on what a
/// day edit saves.
/// </summary>
public static class PlannedHoursMerge
{
    /// <summary>
    /// The blocks with <paramref name="on"/> set to <paramref name="hours"/> — replaced when
    /// held, removed for <see langword="null"/>, the other days kept, in day order — or
    /// <see langword="null"/> when the day already reads so and there is nothing to write.
    /// </summary>
    public static IReadOnlyList<PlannedHoursDto>? WithDay(IReadOnlyList<PlannedHoursDto> blocks, DateOnly on, decimal? hours)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var held = blocks.FirstOrDefault(block => block.On == on);
        if (hours is null && held is null) return null;
        if (hours is { } same && held is not null && held.Hours == same) return null;

        List<PlannedHoursDto> next = [.. blocks.Where(block => block.On != on)];
        if (hours is { } figure) next.Add(new PlannedHoursDto(on, figure));

        return [.. next.OrderBy(block => block.On)];
    }
}
