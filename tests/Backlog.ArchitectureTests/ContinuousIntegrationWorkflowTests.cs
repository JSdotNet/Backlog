using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// What the workflows under <c>.github/workflows/</c> promise about themselves
/// (issue #748): no job holds a runner for GitHub's six-hour default, a nightly
/// run exercises the suite at the parallelism developer machines use, every
/// Node step runs one version, and the pull request run reports its coverage.
///
/// <para>Nothing else can prove these. A workflow runs only on GitHub, and each of
/// them fails quietly: a missing timeout is noticed when a hung workload install has
/// already blocked the self-hosted runner, and a nightly run that passes
/// <c>--parallel none</c> is green while testing nothing it was added for. The
/// files are read as text, line by line, because the repository takes no YAML
/// dependency for one test class; the shapes it reads are the two-space jobs and
/// four-space job keys every workflow here already uses.</para>
/// </summary>
public class ContinuousIntegrationWorkflowTests
{
    private static readonly string[] WorkflowFolder = [".github", "workflows"];

    /// <summary>The nightly run at default parallelism.</summary>
    private const string NightlyWorkflow = "nightly-tests.yml";

    /// <summary>The pull request run, the one that reports coverage.</summary>
    private const string PullRequestWorkflow = "pull-request.yml";

    /// <summary>
    /// The timeout each workflow's jobs carry: 60 for release and deploy jobs,
    /// whose workload installs and provisioning are slow when healthy; 30 for the
    /// rest, the nightly test run and auto-merge included. A workflow added without a row
    /// here fails the test, so the next one gets a decision rather than the
    /// platform default.
    /// </summary>
    private static readonly Dictionary<string, int> ExpectedTimeoutMinutes = new(StringComparer.Ordinal)
    {
        ["pull-request.yml"] = 30,
        ["devbook-metadata.yml"] = 30,
        ["devbook-meta.yml"] = 30,
        ["codeql.yml"] = 30,
        ["app-insights-exceptions.yml"] = 30,
        [NightlyWorkflow] = 30,
        ["auto-merge.yml"] = 30,

        ["release-desktop.yml"] = 60,
        ["release-mobile.yml"] = 60,
        ["deploy-foundry.yml"] = 60,
        ["deploy-sync.yml"] = 60,
        ["deploy-all.yml"] = 60,
    };

    /// <summary>The desktop release, whose Latest badge is the App Installer update channel.</summary>
    private const string DesktopReleaseWorkflow = "release-desktop.yml";

    /// <summary>The workflows whose <c>setup-node</c> steps must agree on one version.</summary>
    private static readonly string[] NodeWorkflows =
        ["pull-request.yml", "devbook-metadata.yml", NightlyWorkflow];

    [Fact]
    public void Every_workflow_has_a_timeout_decision()
    {
        var unlisted = WorkflowFiles()
            .Select(file => Path.GetFileName(file))
            .Where(name => !ExpectedTimeoutMinutes.ContainsKey(name))
            .ToArray();

        Assert.True(
            unlisted.Length == 0,
            $"{string.Join(", ", unlisted)} has no row in ExpectedTimeoutMinutes. Decide the job timeout "
            + "for the new workflow (30, or 60 for release and deploy) and add it there.");
    }

    /// <summary>
    /// A job on a runner declares <c>timeout-minutes</c>; a reusable-workflow call
    /// does not, because GitHub rejects the key there and the called workflow's own
    /// job timeout applies under both dispatch and call.
    /// </summary>
    [Fact]
    public void Every_job_on_a_runner_has_its_timeout()
    {
        var problems = new List<string>();

        foreach (var file in WorkflowFiles())
        {
            var name = Path.GetFileName(file);
            if (!ExpectedTimeoutMinutes.TryGetValue(name, out var expected))
            {
                continue;
            }

            foreach (var job in Jobs(File.ReadAllLines(file)))
            {
                var timeout = JobKey(job, "timeout-minutes");

                if (JobKey(job, "uses") is not null)
                {
                    if (timeout is not null)
                    {
                        problems.Add($"{name}: '{job.Name}' calls a reusable workflow and cannot carry timeout-minutes.");
                    }

                    continue;
                }

                if (JobKey(job, "runs-on") is null)
                {
                    continue;
                }

                if (timeout != expected.ToString(System.Globalization.CultureInfo.InvariantCulture))
                {
                    problems.Add($"{name}: '{job.Name}' has timeout-minutes '{timeout ?? "(none)"}', expected {expected}.");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// The pull request run is serial, so the <c>DisableParallelization</c>
    /// collections and the environment-variable test of issue #724 are only ever
    /// parallel-tested here.
    /// </summary>
    [Fact]
    public void A_nightly_run_tests_at_default_parallelism()
    {
        var workflow = Workflow(NightlyWorkflow);

        Assert.Matches(new Regex(@"^  schedule:\s*$", RegexOptions.Multiline), workflow);
        Assert.Matches(new Regex(@"^    - cron: '[^']+'\s*$", RegexOptions.Multiline), workflow);
        Assert.Matches(new Regex(@"^  workflow_dispatch:\s*$", RegexOptions.Multiline), workflow);

        var run = Step(workflow, "Run tests");
        Assert.Contains("--solution Backlog.WithoutAppHeads.slnf", run, StringComparison.Ordinal);
        Assert.Contains("--report-trx", run, StringComparison.Ordinal);
        Assert.DoesNotContain("--parallel", run, StringComparison.Ordinal);
        Assert.DoesNotContain("--max-parallel-test-modules", run, StringComparison.Ordinal);

        Assert.Contains("if: always()", Step(workflow, "Upload test results"), StringComparison.Ordinal);
        Assert.Contains("actions/setup-node@", Step(workflow, "Setup Node.js"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The pull request run collects Cobertura coverage, keeps the report as an
    /// artifact and puts the line figure on the run summary. The serial flags stay:
    /// coverage is added to the run, not traded for its parallelism decision.
    /// </summary>
    [Fact]
    public void The_pull_request_run_collects_and_summarises_coverage()
    {
        var workflow = Workflow(PullRequestWorkflow);

        var run = Step(workflow, "Run tests");
        Assert.Contains("--coverage-output-format cobertura", run, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"^\s+--coverage\s*$", RegexOptions.Multiline), run);
        Assert.Contains("--max-parallel-test-modules 1", run, StringComparison.Ordinal);
        Assert.Contains("--parallel none", run, StringComparison.Ordinal);
        Assert.Contains("--report-trx", run, StringComparison.Ordinal);
        Assert.Contains("--results-directory TestResults", run, StringComparison.Ordinal);

        var upload = Step(workflow, "Upload coverage report");
        Assert.Contains("if: always()", upload, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"uses: actions/upload-artifact@[0-9a-f]{40}\b"), upload);
        Assert.Contains("*.cobertura.xml", upload, StringComparison.Ordinal);

        var summary = Step(workflow, "Summarise coverage");
        Assert.Contains("GITHUB_STEP_SUMMARY", summary, StringComparison.Ordinal);
        Assert.Contains("if: always()", summary, StringComparison.Ordinal);
        Assert.Contains("Line coverage", summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// The desktop version step says whether its version came from the
    /// <c>0.1.&lt;run_number&gt;</c> fallback (issue #751), as mobile's
    /// <c>mobile_only</c> does. The publish step keys its Latest decision on this,
    /// not on <c>github.ref_type</c>: Deploy All and a dispatch release from a
    /// branch ref with an explicit version.
    /// </summary>
    [Fact]
    public void The_desktop_version_step_says_whether_the_release_is_a_nightly()
    {
        var version = Step(Workflow(DesktopReleaseWorkflow), "Derive version numbers");

        Assert.Contains("nightly=", version, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"\$nightly\s*=\s*\$true"), version);
    }

    /// <summary>
    /// A nightly desktop release never demotes a higher-versioned release from
    /// the Latest badge (issue #751). Latest is the App Installer update channel, so
    /// a nightly still takes it when it is the highest version; an explicit
    /// release always takes it. Both <c>gh release create</c> and
    /// <c>gh release edit</c> pass one computed flag, and the decision lands on
    /// the run summary.
    /// </summary>
    [Fact]
    public void A_nightly_desktop_release_takes_latest_only_from_a_lower_version()
    {
        var publish = Step(Workflow(DesktopReleaseWorkflow), "Publish GitHub Release");

        Assert.DoesNotMatch(new Regex(@"--latest\s+\$msix"), publish);
        Assert.DoesNotMatch(new Regex(@"gh release edit .*--latest\s*$", RegexOptions.Multiline), publish);
        Assert.Contains("steps.version.outputs.nightly", publish, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"gh release create .*\$latest \$msix"), publish);
        Assert.Matches(new Regex(@"gh release edit .*\$latest\s*$", RegexOptions.Multiline), publish);
        Assert.Contains("gh release view --json tagName", publish, StringComparison.Ordinal);
        Assert.Contains("[version]", publish, StringComparison.Ordinal);
        Assert.Contains("'--latest=false'", publish, StringComparison.Ordinal);
        Assert.Contains("GITHUB_STEP_SUMMARY", publish, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--coverage</c> exists only in a test module that references the
    /// Microsoft.Testing.Platform coverage extension, and a module without it runs
    /// nothing. coverlet's collector is a VSTest data collector the platform never
    /// loads, so it is not referenced, and Dependabot's test group keeps no pattern
    /// for it.
    /// </summary>
    [Fact]
    public void Every_test_project_references_the_platform_coverage_extension()
    {
        var testProps = File.ReadAllText(RepositoryRoot.File(["tests", "Directory.Build.props"]));
        var packages = File.ReadAllText(RepositoryRoot.File("Directory.Packages.props"));
        var dependabot = File.ReadAllText(RepositoryRoot.File([".github", "dependabot.yml"]));

        Assert.Contains(
            "<PackageReference Include=\"Microsoft.Testing.Extensions.CodeCoverage\" />",
            testProps,
            StringComparison.Ordinal);
        Assert.Single(Regex.Matches(packages, "<PackageVersion Include=\"Microsoft\\.Testing\\.Extensions\\.CodeCoverage\""));

        Assert.DoesNotContain("Include=\"coverlet", testProps, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Include=\"coverlet", packages, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex(@"^\s+- coverlet", RegexOptions.Multiline | RegexOptions.IgnoreCase), dependabot);

        var projects = new[] { "src", "tests" }
            .SelectMany(folder => Directory.GetFiles(RepositoryRoot.Directory([folder]), "*.csproj", SearchOption.AllDirectories))
            .Where(project => File.ReadAllText(project).Contains("Include=\"coverlet", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.True(projects.Length == 0, $"Still referencing coverlet: {string.Join(", ", projects)}");
    }

    /// <summary>
    /// A pull request that changes only <c>.devbook/</c> skips the build jobs. Every
    /// one of them waits on the detection job and is skipped only on its explicit
    /// <c>'false'</c>, so a detection that fails or answers nothing still builds: a
    /// skipped required check reports success, and a gate that skipped on failure
    /// would merge untested code green. A change only to the plugin skips them too,
    /// except the .NET job: it is the plugin's check, running just the tests that
    /// read the plugin, so it runs on <c>scope == 'plugin'</c> as well.
    /// </summary>
    [Fact]
    public void The_pull_request_build_jobs_skip_only_on_a_devbook_or_plugin_only_change()
    {
        const string Gate = "${{ !cancelled() && needs.changes.outputs.code != 'false' }}";
        const string DotnetGate = "${{ !cancelled() && (needs.changes.outputs.code != 'false' || needs.changes.outputs.scope == 'plugin') }}";
        var jobs = Jobs(File.ReadAllLines(WorkflowPath(PullRequestWorkflow)));

        var detect = Assert.Single(jobs, job => job.Name == "changes");
        var builds = jobs.Where(job => job.Name != "changes").ToArray();

        Assert.NotEmpty(builds);
        Assert.All(builds, job =>
        {
            Assert.Equal("changes", JobKey(job, "needs"));
            Assert.Equal(job.Name == "dotnet" ? DotnetGate : Gate, JobKey(job, "if"));
        });

        var detection = string.Join('\n', detect.Lines);
        Assert.Contains("grep -v '^\\.devbook/'", detection, StringComparison.Ordinal);
        Assert.Contains("code=false", detection, StringComparison.Ordinal);
        Assert.Contains("'^(\\.devbook/|plugins/|\\.claude-plugin/|CLAUDE\\.md$)'", detection, StringComparison.Ordinal);
        Assert.Contains("scope=plugin", detection, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_node_step_runs_one_node_version()
    {
        var versions = NodeWorkflows
            .SelectMany(name => Regex.Matches(Workflow(name), @"node-version: '([^']+)'")
                .Select(match => $"{name}: {match.Groups[1].Value}"))
            .ToArray();

        var distinct = versions.Select(entry => entry[(entry.IndexOf(": ", StringComparison.Ordinal) + 2)..])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            distinct.Length == 1,
            $"The setup-node steps disagree: {string.Join("; ", versions)}. Pin one version across them.");
    }

    /// <summary>
    /// <c>build-database.mjs</c> needs <c>node:sqlite</c> (Node 22.5+), so each
    /// devbook job sets Node up before it runs node, rather than taking whatever
    /// the runner image ships.
    /// </summary>
    [Fact]
    public void Every_devbook_metadata_job_sets_up_node_before_running_it()
    {
        foreach (var job in Jobs(File.ReadAllLines(WorkflowPath("devbook-metadata.yml"))))
        {
            var setup = job.Lines.FindIndex(line => line.Contains("uses: actions/setup-node@", StringComparison.Ordinal));
            var firstNode = job.Lines.FindIndex(line => line.TrimStart().StartsWith("run: node ", StringComparison.Ordinal));

            Assert.True(firstNode >= 0, $"devbook-metadata.yml: '{job.Name}' runs no node command.");
            Assert.True(
                setup >= 0 && setup < firstNode,
                $"devbook-metadata.yml: '{job.Name}' runs node without an actions/setup-node step before it.");
        }
    }

    /// <summary>
    /// The two jobs that restore <c>Backlog.WithoutAppHeads.slnf</c> on Windows x64
    /// — the solution minus the heads, which is exactly the set of projects with a
    /// lock file — restore in locked mode, so a package that resolved differently
    /// from the committed lock file fails the run instead of building.
    /// </summary>
    [Theory]
    [InlineData(PullRequestWorkflow, "dotnet")]
    [InlineData(NightlyWorkflow, "dotnet")]
    public void The_solution_filter_restores_in_locked_mode(string workflow, string job)
    {
        var restore = Step(JobText(workflow, job), "Restore dependencies");

        Assert.Matches(new Regex(@"^\s+run: dotnet restore Backlog\.WithoutAppHeads\.slnf [^\r\n]*--locked-mode\b", RegexOptions.Multiline), restore);
    }

    /// <summary>
    /// The app-heads restores pass <c>-r win-x64</c> and
    /// <c>RuntimeIdentifierOverride</c>, global properties that flow into every
    /// project the heads reference. Those projects' lock files were written without
    /// them, so a locked restore there would fail on a graph nobody changed.
    /// </summary>
    [Fact]
    public void The_app_heads_restore_without_locked_mode()
    {
        var heads = string.Join(
            '\n',
            JobText(PullRequestWorkflow, "app-heads").Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

        Assert.Contains("dotnet restore", heads, StringComparison.Ordinal);
        Assert.DoesNotContain("--locked-mode", heads, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pull request and nightly runs cache the NuGet packages folder keyed on
    /// the lock files; the nightly run fills it on main, where pull request runs can
    /// read it. The release and deploy runs stay uncached, so a signed artifact is
    /// always built from packages fetched for it.
    /// </summary>
    [Fact]
    public void The_test_runs_cache_nuget_packages_and_the_release_runs_do_not()
    {
        foreach (var workflow in new[] { PullRequestWorkflow, NightlyWorkflow })
        {
            var steps = SetupDotnetSteps(Workflow(workflow));
            Assert.NotEmpty(steps);
            Assert.All(steps, step =>
            {
                Assert.Matches(new Regex(@"^\s+cache: true\s*$", RegexOptions.Multiline), step);
                Assert.Matches(new Regex(@"^\s+cache-dependency-path: '\*\*/packages\.lock\.json'\s*$", RegexOptions.Multiline), step);
            });
        }

        Assert.Equal(2, SetupDotnetSteps(Workflow(PullRequestWorkflow)).Count);

        foreach (var workflow in new[] { "release-desktop.yml", "release-mobile.yml", "deploy-sync.yml" })
        {
            var steps = SetupDotnetSteps(Workflow(workflow));
            Assert.NotEmpty(steps);
            Assert.All(steps, step => Assert.DoesNotMatch(new Regex(@"^\s+cache(-dependency-path)?:", RegexOptions.Multiline), step));
        }
    }

    private sealed record Job(string Name, List<string> Lines);

    /// <summary>One job's body as text, so <see cref="Step"/> can find a step that
    /// shares its name with a step of another job.</summary>
    private static string JobText(string workflow, string job) =>
        string.Join('\n', Assert.Single(Jobs(File.ReadAllLines(WorkflowPath(workflow))), j => j.Name == job).Lines);

    /// <summary>Every step that uses <c>actions/setup-dotnet</c>, as its text.</summary>
    private static List<string> SetupDotnetSteps(string workflow) =>
        Regex.Matches(
                workflow.Replace("\r\n", "\n", StringComparison.Ordinal),
                @"^      - name: .*?(?=^      - name: |^  \S|\z)",
                RegexOptions.Multiline | RegexOptions.Singleline)
            .Select(match => match.Value)
            .Where(step => step.Contains("uses: actions/setup-dotnet@", StringComparison.Ordinal))
            .ToList();

    private static string[] WorkflowFiles() =>
        Directory.GetFiles(RepositoryRoot.Directory(WorkflowFolder), "*.yml")
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string WorkflowPath(string name) => RepositoryRoot.File([.. WorkflowFolder, name]);

    private static string Workflow(string name) => File.ReadAllText(WorkflowPath(name));

    /// <summary>The jobs under <c>jobs:</c>, each with the lines of its body.</summary>
    private static List<Job> Jobs(string[] lines)
    {
        var jobs = new List<Job>();
        var inJobs = false;
        Job? current = null;

        foreach (var line in lines)
        {
            if (Regex.IsMatch(line, @"^\S"))
            {
                inJobs = line.TrimEnd() == "jobs:";
                current = null;
                continue;
            }

            if (!inJobs)
            {
                continue;
            }

            var header = Regex.Match(line, @"^  ([A-Za-z0-9_-]+):\s*$");
            if (header.Success)
            {
                current = new Job(header.Groups[1].Value, []);
                jobs.Add(current);
                continue;
            }

            current?.Lines.Add(line);
        }

        return jobs;
    }

    /// <summary>The value of a key at the job's own level, or null.</summary>
    private static string? JobKey(Job job, string key)
    {
        foreach (var line in job.Lines)
        {
            var match = Regex.Match(line, $@"^    {Regex.Escape(key)}:\s*(.*?)\s*$");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    /// <summary>A step's text, from its <c>- name:</c> line to the next step or job.</summary>
    private static string Step(string workflow, string name)
    {
        var match = Regex.Match(
            workflow,
            $@"^      - name: {Regex.Escape(name)}\s*$.*?(?=^      - name: |^  \S|\z)",
            RegexOptions.Multiline | RegexOptions.Singleline);

        Assert.True(match.Success, $"No step named '{name}'.");
        return match.Value;
    }
}
