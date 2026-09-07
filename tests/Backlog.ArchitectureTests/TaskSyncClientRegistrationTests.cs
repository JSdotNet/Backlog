namespace Backlog.ArchitectureTests;

/// <summary>
/// <c>AddTaskSyncClient</c> (see <c>SyncClientRegistration</c> in
/// <c>Backlog.Infrastructure.Sync</c>) registers <c>TaskReplicaMerge</c> and
/// <c>TaskSyncSession</c>, and both of those need an <c>ITaskRepository</c> — a host
/// that calls it without registering one builds cleanly and only dies later, inside
/// <c>IServiceProvider</c> validation, the moment something asks for the session
/// rather than at the call itself.
///
/// <para>
/// That already happened once: the missing registration shipped past a 5075-test
/// suite and was only caught by starting the app. The two web harnesses are now
/// covered for real — <c>Backlog.HostComposition.UnitTests</c> starts each one and
/// would fail the same way a user's launch would. It cannot do that for
/// <c>src/App/Backlog.Desktop/MauiProgram.cs</c> or
/// <c>src/App/Backlog.Mobile/MauiProgram.cs</c>: a MAUI head cannot be composed in a
/// test process at all.
/// </para>
///
/// <para>
/// This rule is a text scan over source rather than a real host start, which is the
/// only guard available for those two heads. It is deliberately narrow: it checks
/// that a composition root naming <c>AddTaskSyncClient</c> also names
/// <c>ITaskRepository</c> somewhere in the same file, not that the registration it
/// builds is reachable, well-formed, or ever resolved.
/// </para>
/// </summary>
public class TaskSyncClientRegistrationTests
{
    private static readonly string[] Roots = ["src/App", "src/Harness"];

    [Fact]
    public void Every_composition_root_that_adds_task_sync_also_registers_a_task_repository()
    {
        var files = CompositionRoots().ToList();

        Assert.NotEmpty(files);

        var offenders = files
            // The open paren, not a bare "AddTaskSyncClient": every head that skips the
            // call explains why in a comment naming the method, and a bare substring
            // match would read that explanation as the call it is explaining the
            // absence of.
            .Where(file => file.Text.Contains("AddTaskSyncClient(", StringComparison.Ordinal))
            // The same reasoning for the interface: the angle brackets rather than a
            // bare "ITaskRepository", because the comment on the other side names the
            // interface too.
            .Where(file => !file.Text.Contains("<ITaskRepository>", StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These composition roots call AddTaskSyncClient without registering an ITaskRepository, so "
            + "the host builds and only dies inside provider validation the moment something asks for "
            + "TaskReplicaMerge or TaskSyncSession. Register an ITaskRepository before the call, or drop "
            + "the call if this head has none to give it:\n" + string.Join('\n', offenders));
    }

    /// <summary>Every <c>MauiProgram.cs</c> or <c>Program.cs</c> under the app heads
    /// and the harnesses — the files that assemble a service collection for a
    /// runnable host, as opposed to a library or a module.</summary>
    private static IEnumerable<(string RelativePath, string Text)> CompositionRoots()
    {
        foreach (var root in Roots)
        {
            var folder = new DirectoryInfo(Path.Combine([Repository.Root.FullName, .. root.Split('/')]));
            if (!folder.Exists) continue;

            foreach (var file in folder.EnumerateFiles("*.cs", SearchOption.AllDirectories))
            {
                if (file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    || file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                    continue;

                if (file.Name is not ("MauiProgram.cs" or "Program.cs")) continue;

                yield return (
                    Path.GetRelativePath(Repository.Root.FullName, file.FullName).Replace('\\', '/'),
                    File.ReadAllText(file.FullName));
            }
        }
    }
}
