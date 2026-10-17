namespace Backlog.Modules.Progress.DomainModels;

/// <summary>Plan tags as Progress compares them: the slug without the <c>+</c> sigil, so
/// <c>+goals-standup</c> and <c>goals-standup</c> name the same plan.</summary>
public static class PlanTags
{
    public static string Bare(string? tag)
    {
        var trimmed = tag?.Trim() ?? string.Empty;
        return trimmed.StartsWith('+') ? trimmed[1..].Trim() : trimmed;
    }

    public static bool Same(string? left, string? right) =>
        string.Equals(Bare(left), Bare(right), StringComparison.OrdinalIgnoreCase);
}
