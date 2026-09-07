using System.Reflection;

using Backlog.Modules.Tasks.Abstractions.Services;

using Xunit;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// What <see cref="ITaskStore"/> is allowed to answer.
/// <para>
/// The port says where the <em>task</em> store lives. The Inbox context's folder
/// used to hang off it as well, which made one context's abstractions the owner
/// of another context's folder. It was taken off deliberately, and the folder is
/// reached through the workspace settings adapter instead, so this guards the
/// removal rather than restating it: the member is easy to add back by reflex
/// when the port is already at hand.
/// </para>
/// </summary>
public sealed class TaskStoreSurfaceTests
{
    [Fact]
    public void ITaskStore_exposes_no_inbox_member()
    {
        const BindingFlags Surface = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

        var inboxMembers = typeof(ITaskStore)
            .GetInterfaces()
            .Append(typeof(ITaskStore))
            .SelectMany(contract => contract.GetMembers(Surface))
            .Select(member => member.Name)
            .Where(name => name.Contains("Inbox", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            inboxMembers.Count == 0,
            $"ITaskStore gained {string.Join(", ", inboxMembers)}. The Inbox context's folder is not this "
            + "port's to hand out — take it from WorkspaceSettingsStore, the way the settings screen does.");
    }
}
