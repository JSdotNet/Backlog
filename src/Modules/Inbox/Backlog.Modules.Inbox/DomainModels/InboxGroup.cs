namespace Backlog.Modules.Inbox.DomainModels;

/// <summary>
/// A fold in the side menu that holds lists. Local-only and hard-deleted, for
/// the reasons <see cref="InboxList"/> gives; ungrouping is a handler that
/// moves the lists out and then removes the group, because no FK enforces the
/// relationship and none should — SQLite leaves FK enforcement off by default.
/// </summary>
public sealed class InboxGroup
{
    public static InboxGroup Create(string name, int order, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), name, order, createdAt: now);

    /// <summary>Full constructor, also used by storage to rehydrate a persisted
    /// group; storage calls <see cref="LoadStamps"/> last.</summary>
    public InboxGroup(Guid id, string name, int order, DateTimeOffset createdAt)
    {
        Id = id;
        Name = Clean(name);
        Order = order;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; }

    public string Name { get; private set; }

    public int Order { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Rename(string name)
    {
        Name = Clean(name);
        Touch();
    }

    public void SetOrder(int order)
    {
        Order = order;
        Touch();
    }

    public void LoadStamps(DateTimeOffset updatedAt) => UpdatedAt = updatedAt;

    private static string Clean(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A group needs a name.", nameof(name));

        return name.Trim();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
