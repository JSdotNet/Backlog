namespace Backlog.Modules.Backlog.DomainModels;

/// <summary>
/// An ordered breakdown step owned by a <see cref="BacklogEntry"/>. It has
/// identity within the aggregate only. All mutations go through the aggregate
/// root — setters are internal so callers cannot bypass the root's invariants.
/// </summary>
public sealed class SubItem
{
    internal SubItem(Guid id, string title, int order, string? notes = null, SubItemStatus status = SubItemStatus.Pending)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Sub-item title is required.", nameof(title));

        Id = id;
        Title = title;
        Order = order;
        Notes = notes;
        Status = status;
    }

    public Guid Id { get; }

    public string Title { get; internal set; }

    public SubItemStatus Status { get; internal set; }

    public string? Notes { get; internal set; }

    /// <summary>Zero-based ordinal position within the parent entry.</summary>
    public int Order { get; internal set; }
}
