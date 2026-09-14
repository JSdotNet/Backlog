namespace Backlog.Modules.Inbox.DomainModels;

/// <summary>
/// A list an item can be filed in — the organiser's leaf. Its own small root
/// rather than a child of the item: a list exists before anything is in it and
/// after everything has left, and deleting one is a handler write across every
/// item it held rather than a cascade the store could run on its own.
/// <para>
/// Local-only, and hard-deleted. Tombstoning exists for documents that travel
/// (local ADR 0005) and nothing replicates a list, so a deleted one leaves no
/// row behind.
/// </para>
/// </summary>
public sealed class InboxList
{
    public static InboxList Create(string name, Guid? groupId, int order, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), name, groupId, order, createdAt: now);

    /// <summary>Full constructor, also used by storage to rehydrate a persisted
    /// list; storage calls <see cref="LoadStamps"/> last.</summary>
    public InboxList(Guid id, string name, Guid? groupId, int order, DateTimeOffset createdAt)
    {
        Id = id;
        Name = Clean(name);
        GroupId = groupId;
        Order = order;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; }

    public string Name { get; private set; }

    /// <summary>The group this list sits in, or null at the top level.</summary>
    public Guid? GroupId { get; private set; }

    public int Order { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Rename(string name)
    {
        Name = Clean(name);
        Touch();
    }

    public void MoveToGroup(Guid? groupId)
    {
        GroupId = groupId;
        Touch();
    }

    public void SetOrder(int order)
    {
        Order = order;
        Touch();
    }

    public void LoadStamps(DateTimeOffset updatedAt) => UpdatedAt = updatedAt;

    /// <summary>Trimmed and non-empty; the handler has already answered
    /// <c>inbox.list.needs_name</c> for a blank, so reaching here with one is a
    /// bug rather than input.</summary>
    private static string Clean(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A list needs a name.", nameof(name));

        return name.Trim();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
