namespace Backlog.ArchitectureTests;

/// <summary>
/// <c>AddSessionSyncClient</c> (see <c>SyncClientRegistration</c> in
/// <c>Backlog.Infrastructure.Sync</c>) has two preconditions a host can satisfy
/// only by writing another line, and getting either wrong is invisible until the
/// app is running.
///
/// <para>
/// The same shape as <see cref="TaskSyncClientRegistrationTests"/> and for the same
/// reason: <c>Backlog.HostComposition.UnitTests</c> starts the web harnesses for
/// real and would fail the way a user's launch does, but it cannot do that for
/// <c>src/App/Backlog.Desktop/MauiProgram.cs</c> — a MAUI head cannot be composed
/// in a test process at all. These are text scans over source, which is the only
/// guard available for that head.
/// </para>
///
/// <para>
/// Deliberately narrow. They check that a composition root naming the call also
/// names the thing it needs, not that the registration it builds is reachable,
/// well-formed, or ever resolved.
/// </para>
/// </summary>
public class SessionSyncClientRegistrationTests
{
    private static readonly string[] Roots = ["src/App", "src/Harness"];

    /// <summary>
    /// The exchange reads this machine's sessions through
    /// <c>IAgentSessionSource</c>, which <c>AddAgentSessionSource()</c> registers. A
    /// head that opted into session replication without composing the readers builds
    /// cleanly and dies inside provider validation the moment something asks for the
    /// exchange — or, worse on a head that does not validate, pushes nothing for
    /// ever.
    /// </summary>
    [Fact]
    public void Every_composition_root_that_adds_session_sync_also_composes_the_session_readers()
    {
        var files = CompositionRoots().ToList();

        Assert.NotEmpty(files);

        var offenders = files
            // The open paren, not a bare name: a head that skips the call explains
            // why in a comment naming the method, and a bare substring match would
            // read that explanation as the call it is explaining the absence of.
            .Where(file => file.Text.Contains("AddSessionSyncClient(", StringComparison.Ordinal))
            .Where(file => !file.Text.Contains("AddAgentSessionSource(", StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These composition roots add session replication without composing the session readers it pushes "
            + "from, so the host builds and only dies inside provider validation the moment something asks for "
            + "SessionSyncSession. Call AddAgentSessionSource(), or drop the session sync call if this head has "
            + "no sessions to replicate:\n" + string.Join('\n', offenders));
    }

    /// <summary>
    /// The exchange also needs somewhere to keep its progress and the records it
    /// pulls, which is <c>AddSessionSyncStores</c>. Without it the exchange and the
    /// loop are registered and unconstructable, exactly as task sync is without its
    /// own store — the case a settings screen has to guard.
    /// </summary>
    [Fact]
    public void Every_composition_root_that_adds_session_sync_also_chooses_its_stores()
    {
        var files = CompositionRoots().ToList();

        Assert.NotEmpty(files);

        var offenders = files
            .Where(file => file.Text.Contains("AddSessionSyncClient(", StringComparison.Ordinal))
            .Where(file => !file.Text.Contains("AddSessionSyncStores(", StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These composition roots add session replication and choose no stores for it, so the exchange and "
            + "the loop are registered and unconstructable. Call AddSessionSyncStores with a per-user folder - "
            + "never the workspace root, which a file-sync product may carry to the other machine:\n"
            + string.Join('\n', offenders));
    }

    /// <summary>
    /// <c>SessionSyncWorker</c> is a singleton with a timer inside it rather than an
    /// <c>IHostedService</c> — there is no hosted service anywhere in this
    /// repository, because the MAUI head has no generic host to start one — so
    /// nothing starts it except the first resolve.
    ///
    /// <para>
    /// That makes the failure completely silent: a head that registers the worker and
    /// never asks for it builds, starts, opens every screen and replicates nothing at
    /// all. No test that composes a service collection can see it, because the
    /// registration is there and correct; what is missing is the ask.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_composition_root_that_adds_session_sync_also_resolves_the_worker()
    {
        var files = CompositionRoots().ToList();

        Assert.NotEmpty(files);

        var offenders = files
            .Where(file => file.Text.Contains("AddSessionSyncClient(", StringComparison.Ordinal))
            .Where(file => !file.Text.Contains("GetRequiredService<SessionSyncWorker>()", StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These composition roots add session replication and never resolve SessionSyncWorker, so the "
            + "background loop is registered and never constructed - the head replicates nothing, and there is "
            + "nothing about that to notice. Ask for it once after Build():\n" + string.Join('\n', offenders));
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
