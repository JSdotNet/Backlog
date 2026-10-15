using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.UI;

/// <summary>
/// The seven insights the dashboard's tabs draw from, handed to
/// <see cref="DashboardAiContentSource"/> as one thing.
/// </summary>
/// <remarks>
/// One parameter rather than seven because the source reads them as a set — every
/// tab's figures, in the tabs' order — and a constructor eleven parameters long
/// would make that set look like eleven unrelated collaborators. They are the same
/// scoped instances the parts inject, so a figure the assistant is sent comes out of
/// the cache the part on screen already filled.
/// </remarks>
internal sealed record DashboardInsightPorts(
    IProductivityInsights Productivity,
    ITaskInsights Tasks,
    ISessionInsights Sessions,
    IHoursWorkedInsights HoursWorked,
    IDriftInsights Drift,
    ICostInsights Cost,
    IAttentionInsights Attention);
