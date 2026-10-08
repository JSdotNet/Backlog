namespace Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

/// <summary>
/// Hours set aside for an entry on one day: the date and how many hours, never a
/// start or an end time. The Calendar draws each as a chip on its day and adds the
/// day's blocks up against the day's working hours.
/// <para>
/// The person's own plan, kept on the entry beside its text rather than in it. The
/// roadmap never reads it — its windows and pace come from points and measured pace.
/// </para>
/// </summary>
/// <param name="On">The day.</param>
/// <param name="Hours">How many hours: more than zero, at most a whole day, in quarters
/// of an hour.</param>
public sealed record PlannedHoursDto(DateOnly On, decimal Hours);
