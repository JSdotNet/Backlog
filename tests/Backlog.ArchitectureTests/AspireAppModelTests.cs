using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Backlog.ArchitectureTests;

/// <summary>
/// What the Aspire app model registers, read as text.
///
/// <para>No test project references <c>src/Aspire/Backlog.Aspire.AppHost</c> and
/// none can: the AppHost is an executable whose whole behavior is a side effect of
/// <c>builder.Build().Run()</c>, and referencing it would pull the Aspire hosting
/// SDK into a plain test assembly. So the rules that matter about the app model —
/// which resources exist, which of them may start by themselves, and what the
/// documentation claims about the host — are asserted the way
/// <see cref="ProcessLaunchTests"/> asserts its rule: by reading the source.</para>
///
/// <para>A source scan cannot prove the app model <em>runs</em>; that still takes
/// starting the AppHost. What it can do is stop the two failures a green build and
/// a green suite both miss: a resource quietly gaining an automatic start it must
/// not have, and prose that goes on describing the host the way it used to be.</para>
/// </summary>
public class AspireAppModelTests
{
    private const string AppHost = "Backlog.Aspire.AppHost";

    private static readonly string[] AppHostProject = ["src", "Aspire", AppHost, AppHost + ".csproj"];

    private static readonly string[] AppModel = ["src", "Aspire", AppHost, "Program.cs"];

    private static readonly string[] LocalDataResetSource = ["src", "Aspire", AppHost, "LocalDataReset.cs"];

    /// <summary>The Aspire hosting packages this AppHost adds on top of
    /// <c>Aspire.Hosting.AppHost</c>. Each has to move with it: the two preview
    /// ones ship one build per Aspire release, and mixing releases fails when the
    /// host starts rather than when it restores.</summary>
    private static readonly string[] AspireHostingPackages =
        ["Aspire.Hosting.Foundry", "Aspire.Hosting.Maui", "Aspire.Hosting.DevTunnels"];

    /// <summary>The MAUI heads. A <c>ProjectReference</c> to one of them makes the
    /// AppHost build resolve <c>net10.0-android</c>, which it cannot, so every
    /// registration that names a head names it by path.</summary>
    private static readonly string[] MauiHeads = ["Backlog.Mobile", "Backlog.Desktop"];

    /// <summary>The two copies of the orchestration runtime context. CLAUDE.md
    /// requires them to carry the same facts, and nothing but a rule makes that
    /// true — the Claude copy is the one that gets edited.</summary>
    private static readonly string[][] OrchestrationContexts =
        [[".claude", "orch-context.md"], [".github", "copilot-orch-context.md"]];

    [Fact]
    public void The_apphost_opts_in_to_the_aspire_cli_bundle()
    {
        var value = Csproj().Descendants("AspireUseCliBundle").Select(element => element.Value).SingleOrDefault();

        Assert.Equal("true", value, ignoreCase: true);
    }

    /// <summary>
    /// ASPIRE010 is the advisory for staying opted out. Silencing it while opted in
    /// silences nothing today — but it reads as a standing decision to stay out,
    /// and the next advisory to arrive under that code would be swallowed with it.
    /// </summary>
    [Fact]
    public void Nothing_in_the_apphost_still_silences_the_cli_bundle_advisory()
    {
        var suppressed = string.Join(";", Csproj().Descendants("NoWarn").Select(element => element.Value));

        Assert.DoesNotContain("ASPIRE010", suppressed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The technology graph states the opt-out in prose, where no build
    /// step can notice it going stale.</summary>
    [Fact]
    public void The_technology_chapter_no_longer_claims_the_apphost_is_opted_out()
    {
        var chapter = File.ReadAllText(RepositoryRoot.File(".tech", "shared.md"));

        Assert.DoesNotContain("AspireUseCliBundle=false", chapter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AspireUseCliBundle=true", chapter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Org ADR 0002: a version is declared in
    /// <c>Directory.Packages.props</c> once, and a project file names the package
    /// only.</summary>
    [Fact]
    public void Every_apphost_package_takes_its_version_from_central_package_management()
    {
        var declared = PackageVersions();
        var references = Csproj().Descendants("PackageReference").ToList();

        Assert.NotEmpty(references);

        foreach (var reference in references)
        {
            var name = (string?)reference.Attribute("Include") ?? "";

            Assert.True(
                reference.Attribute("Version") is null,
                $"{name} pins its own version in {AppHost}.csproj. Versions are declared centrally in "
                + "Directory.Packages.props (org ADR 0002); a project-local Version is an exception that "
                + "has to be justified with a comment.");

            Assert.True(
                declared.ContainsKey(name),
                $"{name} is referenced by {AppHost}.csproj but has no PackageVersion entry in "
                + "Directory.Packages.props, so the restore has no version to resolve.");
        }
    }

    /// <summary>
    /// The Foundry and MAUI hosting packages ship as previews stamped with the
    /// Aspire release they were built for, and DevTunnels ships as a stable build
    /// of the same release. Pinning the exact build here would make the next Aspire
    /// bump fail on a version string rather than on a mismatch; what has to hold is
    /// that all of them move together.
    /// </summary>
    [Fact]
    public void Every_aspire_hosting_package_tracks_the_apphost_release()
    {
        var declared = PackageVersions();
        var aspire = declared["Aspire.Hosting.AppHost"];

        foreach (var package in AspireHostingPackages)
        {
            Assert.True(
                declared.TryGetValue(package, out var version),
                $"{package} has no PackageVersion entry in Directory.Packages.props.");

            Assert.True(
                Release(version!) == Release(aspire),
                $"{package} is declared as {version} while Aspire.Hosting.AppHost is {aspire}. An Aspire "
                + "hosting package built for another release binds against a different Aspire.Hosting "
                + "than the AppHost resolves, which fails when the host starts rather than when it "
                + "restores.");
        }
    }

    [Fact]
    public void The_apphost_takes_no_project_reference_to_a_maui_head()
    {
        var referenced = Repository
            .ReferencedProjectNames(new FileInfo(RepositoryRoot.File(AppHostProject)))
            .ToList();

        foreach (var head in MauiHeads)
        {
            Assert.True(
                !referenced.Contains(head, StringComparer.OrdinalIgnoreCase),
                $"{AppHost}.csproj references {head}. The MAUI heads target frameworks the AppHost cannot "
                + "build, so a head is named by path and never by project reference.");
        }
    }

    /// <summary>
    /// <c>WithExplicitStart()</c> does not hold Foundry Local back, and the app
    /// model does not pretend it does. Registering it unconditionally was tried on
    /// a machine without the CLI: <c>RunAsFoundryLocal()</c> launched
    /// <c>foundry</c> while the app model came up, and the resource sat at
    /// <c>FailedToStart</c> for the whole run with no start command to recover it.
    ///
    /// <para>So the guard is the thing that keeps a run clean, and the guard is
    /// what this asserts. Reading only that the call is present would pass on the
    /// version that was measured failing.</para>
    /// </summary>
    [Fact]
    public void Foundry_local_is_registered_only_where_its_cli_can_be_found()
    {
        var model = AppModelSource();
        var registration = Registration("AddFoundry(");

        Assert.Contains("RunAsFoundryLocal()", registration, StringComparison.Ordinal);
        Assert.Contains("WithExplicitStart()", registration, StringComparison.Ordinal);
        Assert.Contains("FoundryModel.Local.Phi4", model, StringComparison.Ordinal);

        Assert.Contains("if (IsOnPath(\"foundry\"))", model, StringComparison.Ordinal);

        // Guarded, the registration is indented inside its block; unguarded, it
        // starts a line of its own.
        Assert.DoesNotContain("\nbuilder.AddFoundry(", model, StringComparison.Ordinal);
    }

    /// <summary>
    /// The guard answers "can this machine run it", and the only honest answer
    /// comes from the environment: the CLI is looked for on <c>PATH</c>, with the
    /// <c>PATHEXT</c> suffixes Windows needs to find it at all. A guard that read
    /// an opt-in flag or an OS check instead would go stale the day someone
    /// installs the CLI.
    /// </summary>
    [Fact]
    public void The_foundry_guard_answers_from_the_environment()
    {
        var model = AppModelSource();
        var guard = model[model.IndexOf("static bool IsOnPath", StringComparison.Ordinal)..];

        Assert.Contains("GetEnvironmentVariable(\"PATH\")", guard, StringComparison.Ordinal);
        Assert.Contains("GetEnvironmentVariable(\"PATHEXT\")", guard, StringComparison.Ordinal);
        Assert.Contains("File.Exists", guard, StringComparison.Ordinal);
    }

    /// <summary>The local stand-in is what <c>desktop-web-harness</c> actually
    /// talks to. Foundry Local arriving beside it is an addition, not a
    /// replacement — swapping them would leave the harness waiting on a CLI that is
    /// not installed.</summary>
    [Fact]
    public void The_azure_foundry_harness_stays_registered_beside_foundry_local()
    {
        Assert.Contains("AddProject(\"azure-foundry-test\"", AppModelSource(), StringComparison.Ordinal);

        var harness = Registration("AddProject(\"desktop-web-harness\"");

        Assert.Contains("WithReference(azureFoundryTest)", harness, StringComparison.Ordinal);
        Assert.Contains("BACKLOG_AZURE_FOUNDRY_LOCAL_ENDPOINT", harness, StringComparison.Ordinal);
        Assert.Contains("WaitFor(azureFoundryTest)", harness, StringComparison.Ordinal);
    }

    /// <summary>
    /// An emulator reaches the host over its own loopback, not the developer's, so
    /// <c>sync</c> has to be published through a tunnel before the Android head can
    /// see it at all.
    ///
    /// <para>That takes both halves of the reference, which is why both are read
    /// here. The tunnel's own <c>WithReference(sync)</c> is what opens a port for
    /// <c>sync</c>'s endpoint; the head's <c>WithReference(sync, mobileTunnel)</c>
    /// is what makes the head resolve <c>sync</c> through that port. With only the
    /// second the tunnel forwards nothing, and the app model still builds, still
    /// starts, and still passes every other rule in this file — the head simply
    /// never reaches sync.</para>
    /// </summary>
    [Fact]
    public void The_maui_android_head_reaches_sync_through_an_anonymous_dev_tunnel()
    {
        var tunnel = Registration("AddDevTunnel(");

        Assert.Contains("WithReference(sync)", tunnel, StringComparison.Ordinal);
        Assert.Contains("WithAnonymousAccess()", tunnel, StringComparison.Ordinal);

        var registration = Registration("AddMauiProject(");

        Assert.Contains("AddAndroidEmulator()", registration, StringComparison.Ordinal);
        Assert.Contains("WithOtlpDevTunnel(", registration, StringComparison.Ordinal);
        Assert.Contains("WithReference(sync, mobileTunnel)", registration, StringComparison.Ordinal);
        Assert.Contains("WithExplicitStart()", registration, StringComparison.Ordinal);
    }

    /// <summary>The MSBuild <c>Run</c> target is still the only way to deploy the
    /// head to an already-attached device, which is what most local runs have. The
    /// MAUI resource is an addition beside it, not a replacement for it.</summary>
    [Fact]
    public void The_executable_android_launch_stays_registered()
    {
        var model = AppModelSource();

        Assert.Contains("AddExecutable(", model, StringComparison.Ordinal);
        Assert.Contains("\"mobile-android\"", model, StringComparison.Ordinal);
    }

    /// <summary>
    /// The head used to be pinned to <c>net10.0-android</c> in the argument list,
    /// so running it against anything else meant editing the app model. Aspire 13.5
    /// lets a resource command take arguments, which is where that choice belongs.
    /// </summary>
    [Fact]
    public void The_android_target_framework_is_chosen_through_a_command_argument()
    {
        var model = AppModelSource();

        Assert.Contains("new InteractionInput", model, StringComparison.Ordinal);
        Assert.Contains("context.Arguments.GetString(", model, StringComparison.Ordinal);

        var registration = Registration("\"mobile-android\"");

        Assert.Contains("WithCommand(", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("\"-f\", \"net10.0-android\"", registration, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every worktree of this repository shares one <c>Backlog.Debug</c> workspace,
    /// so a reset here is felt by every session running beside it. The rule is that
    /// it removes named files rather than a folder: a command that deletes a
    /// directory tree deletes whatever else happened to be in it.
    /// </summary>
    [Fact]
    public void Resetting_local_data_removes_named_files_rather_than_a_folder()
    {
        var reset = File.ReadAllText(RepositoryRoot.File(LocalDataResetSource));

        Assert.Contains("backlog.db", reset, StringComparison.Ordinal);
        Assert.Contains("-wal", reset, StringComparison.Ordinal);
        Assert.Contains("-shm", reset, StringComparison.Ordinal);
        Assert.Contains("settings.json", reset, StringComparison.Ordinal);

        Assert.DoesNotContain("Directory.Delete", reset, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reset command copies the workspace folder name out of
    /// <c>WorkspaceSettingsStore</c> rather than referencing it, so that an AppHost
    /// <c>ProjectReference</c> does not drag infrastructure into the app model. A
    /// copy silently drifts, and this one drifts destructively: if the AppHost
    /// resolves <c>Backlog.Debug</c> while the harness beside it reads
    /// <c>Backlog</c>, the reset reports success against a store nothing is using
    /// and leaves the live one untouched. Both halves of the <c>#if DEBUG</c> have
    /// to match, which is the part a single-configuration build never notices.
    /// </summary>
    [Fact]
    public void Resetting_local_data_resolves_the_same_workspace_folder_the_app_does()
    {
        var reset = File.ReadAllText(RepositoryRoot.File(LocalDataResetSource));

        var store = File.ReadAllText(RepositoryRoot.File(
            "src", "Infrastructure", "Backlog.Infrastructure.FileSystem", "Workspace",
            "WorkspaceSettingsStore.cs"));

        foreach (var folder in FolderNamesByConfiguration(store))
        {
            Assert.True(
                FolderNamesByConfiguration(reset).Contains(folder),
                $"WorkspaceSettingsStore resolves \"{folder}\" in one configuration and LocalDataReset does not. "
                + "The reset would delete from a folder the running harness is not reading.");
        }
    }

    /// <summary>The workspace folder names a file declares, in both halves of its
    /// <c>#if DEBUG</c>. Matched on the const declaration rather than the whole file
    /// so a folder name that only appears in prose does not count as agreement.</summary>
    private static HashSet<string> FolderNamesByConfiguration(string source) =>
        [.. Regex
            .Matches(source, """(?:AppDataFolderName|WorkspaceFolderName)\s*=\s*"([^"]+)";""")
            .Select(match => match.Groups[1].Value)];

    /// <summary>A reset is destructive and it is shared, so it is reachable only by
    /// someone choosing it in the dashboard — never from a startup path, a
    /// <c>WaitFor</c>, or a lifecycle hook.</summary>
    [Fact]
    public void Resetting_local_data_is_only_ever_a_command()
    {
        var model = AppModelSource();

        // The destructive entry point specifically. ResolveRoot is also called from
        // the app model, from just outside the registration, so the confirmation can
        // name the folder it would delete from — reading that path is not what this
        // test exists to keep off a startup path.
        var reached = Occurrences(model, "LocalDataReset.Run(");

        Assert.True(reached.Count > 0, "The app model never reaches LocalDataReset, so the command does nothing.");

        foreach (var index in reached)
        {
            var registration = RegistrationAround(model, index);

            Assert.True(
                registration.Contains("WithCommand(", StringComparison.Ordinal)
                    && registration.Contains("reset-local-data", StringComparison.Ordinal),
                "LocalDataReset is reached from something other than the reset-local-data command. Wiping "
                + "the workspace every other worktree shares has to stay something a person chooses, not "
                + "something a run does on the way past.");
        }
    }

    /// <summary>The two orchestration context files are the runtime brief every
    /// orchestration reads. A fact added to one and not the other is worse than a
    /// fact in neither, because the toolchain reading the stale copy cannot
    /// tell.</summary>
    [Theory]
    [InlineData("foundry-local")]
    [InlineData("telemetry filtering")]
    public void Both_orchestration_contexts_carry_the_same_runtime_facts(string fact)
    {
        foreach (var context in OrchestrationContexts)
        {
            var text = File.ReadAllText(RepositoryRoot.File(context));

            Assert.True(
                text.Contains(fact, StringComparison.OrdinalIgnoreCase),
                $"{Path.Combine(context)} does not mention '{fact}'. Both copies carry the same runtime "
                + "facts (CLAUDE.md); updating one and not the other leaves whichever toolchain reads the "
                + "other working from a stale brief.");
        }
    }

    /// <summary>The README resource table is the first thing anyone reads before
    /// starting the app, so a resource missing from it is a resource nobody knows
    /// to start.</summary>
    [Theory]
    [InlineData("mobile-maui")]
    [InlineData("mobile-tunnel")]
    public void The_readme_resource_table_names_every_registered_resource(string resource)
    {
        Assert.Contains(resource, Readme(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Foundry Local is the resource that table cannot describe, because whether it
    /// exists at all depends on the machine. Listing it there beside resources that
    /// are always registered would send a reader looking for something most runs
    /// never show, so the README says what the condition is instead.
    /// </summary>
    [Fact]
    public void The_readme_says_foundry_local_is_registered_only_where_its_cli_is()
    {
        var readme = Readme();

        Assert.Contains("foundry-local", readme, StringComparison.Ordinal);
        Assert.Contains("only when `foundry` is on PATH", readme, StringComparison.Ordinal);
    }

    private static string Readme() => File.ReadAllText(RepositoryRoot.File("README.md"));

    /// <summary>The declared package versions, by package name.</summary>
    private static Dictionary<string, string> PackageVersions() =>
        XDocument.Load(RepositoryRoot.File("Directory.Packages.props"))
            .Descendants("PackageVersion")
            .ToDictionary(
                element => (string?)element.Attribute("Include") ?? "",
                element => (string?)element.Attribute("Version") ?? "",
                StringComparer.OrdinalIgnoreCase);

    /// <summary>The release a package version belongs to, with any preview suffix
    /// dropped: <c>13.5.3-preview.1.26425.3</c> and <c>13.5.3</c> are the same
    /// release.</summary>
    private static string Release(string version) => version.Split("-")[0];

    private static XDocument Csproj() => XDocument.Load(RepositoryRoot.File(AppHostProject));

    private static string AppModelSource() =>
        File.ReadAllText(RepositoryRoot.File(AppModel)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static List<int> Occurrences(string source, string value)
    {
        var found = new List<int>();

        for (var index = source.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = source.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            found.Add(index);
        }

        return found;
    }

    /// <summary>
    /// One resource registration, read from <paramref name="anchor"/> up to the
    /// next one.
    ///
    /// <para>The registration rather than the statement is the unit a rule is
    /// about, because asking whether the file contains <c>WithExplicitStart()</c>
    /// somewhere would pass on any other resource's opt-out. It cannot be the
    /// statement: a chain that configures arguments or runs a command carries
    /// semicolons of its own inside a lambda, and reading to the first one stops
    /// halfway through the resource.</para>
    /// </summary>
    private static string Registration(string anchor)
    {
        var model = AppModelSource();
        var start = model.IndexOf(anchor, StringComparison.Ordinal);

        Assert.True(start >= 0, $"The app model has no registration containing '{anchor}'.");

        return model[start..NextRegistration(model, start)];
    }

    /// <summary>The registration <paramref name="index"/> falls inside.</summary>
    private static string RegistrationAround(string model, int index)
    {
        var start = model.LastIndexOf(Registers, index, StringComparison.Ordinal);

        return model[Math.Max(start, 0)..NextRegistration(model, index)];
    }

    /// <summary>Where the registration after <paramref name="index"/> begins, or
    /// the end of the app model when it is the last one.</summary>
    private static int NextRegistration(string model, int index)
    {
        var next = model.IndexOf(Registers, index + Registers.Length, StringComparison.Ordinal);

        return next < 0 ? model.Length : next;
    }

    /// <summary>What every registration in the app model starts with.</summary>
    private const string Registers = "builder.Add";
}
