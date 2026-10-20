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

        [ReleaseWorkflow] = 60,
        ["deploy-foundry.yml"] = 60,
        ["deploy-sync.yml"] = 60,
        ["deploy-all.yml"] = 60,
    };

    /// <summary>The one release of both apps, whose Latest badge is the App Installer update channel.</summary>
    private const string ReleaseWorkflow = "release-all.yml";

    /// <summary>The combined deploy, which ships the release as one of its components.</summary>
    private const string DeployAllWorkflow = "deploy-all.yml";

    /// <summary>The sync tier's workflow, which Release All calls for the service alone.</summary>
    private const string DeploySyncWorkflow = "deploy-sync.yml";

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
    /// The plan job numbers the release once for both channels, through
    /// <c>build/Get-NextReleaseVersion.ps1</c> (whose own cases are
    /// <see cref="NextReleaseVersionScriptTests"/>), from the tags of both namespaces,
    /// and says whether the script chose the number (issue #751). The publish job
    /// keys its Latest decision on that, not on <c>github.ref_type</c>: Deploy All and
    /// a dispatch release from a branch ref.
    /// </summary>
    [Fact]
    public void The_release_is_numbered_once_from_the_tags_of_both_channels()
    {
        var plan = JobText(ReleaseWorkflow, "plan");
        var version = Step(plan, "Derive version numbers");

        Assert.Contains("./build/Get-NextReleaseVersion.ps1", version, StringComparison.Ordinal);
        Assert.Contains("git tag --list 'v*' 'mobile-v*'", version, StringComparison.Ordinal);
        Assert.Contains("nightly=", version, StringComparison.Ordinal);
        Assert.Contains("$next.Derived", version, StringComparison.Ordinal);
        Assert.DoesNotContain("run_number", version, StringComparison.Ordinal);

        Assert.Contains("fetch-tags: true", Step(plan, "Checkout"), StringComparison.Ordinal);
        Assert.Contains("fetch-depth: 0", Step(plan, "Checkout"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Both channels build from the one number, and nothing composes the old
    /// <c>mobile-v</c> tag any more: every release is <c>v</c> plus the version.
    /// </summary>
    [Fact]
    public void Both_channels_build_and_publish_under_the_one_tag()
    {
        var workflow = Workflow(ReleaseWorkflow);

        Assert.DoesNotMatch(new Regex(@"mobile-v\$"), workflow);
        Assert.Contains("-p:ApplicationDisplayVersion=${{ needs.plan.outputs.display }}", JobText(ReleaseWorkflow, "desktop"), StringComparison.Ordinal);
        Assert.Contains("-p:ApplicationDisplayVersion=${{ needs.plan.outputs.display }}", JobText(ReleaseWorkflow, "mobile"), StringComparison.Ordinal);
        Assert.Contains("-p:ApplicationVersion=${{ needs.plan.outputs.code }}", JobText(ReleaseWorkflow, "mobile"), StringComparison.Ordinal);

        var publish = JobText(ReleaseWorkflow, "publish");
        Assert.Contains("needs: [plan, sync-preflight, desktop, mobile, sync]", publish, StringComparison.Ordinal);
        Assert.Contains("$tag = '${{ needs.plan.outputs.tag }}'", Step(publish, "Publish GitHub Release"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Two runs never compute the same number: the version is read from the tags
    /// before the run publishes its own, so the whole workflow is one queue, not one
    /// queue per ref, and a queued run waits rather than cancelling one that is
    /// already publishing.
    /// </summary>
    [Fact]
    public void Release_runs_queue_behind_each_other_workflow_wide()
    {
        var workflow = Workflow(ReleaseWorkflow).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Matches(new Regex(@"^concurrency:\n  group: release-all\n  cancel-in-progress: false\n", RegexOptions.Multiline), workflow);
    }

    /// <summary>
    /// The scheduled gate watches what ships in either package, and diffs against
    /// the newest <c>v*</c> release. <c>gh release list --limit 1</c> answered the
    /// newest release of any tag, so the desktop gate had been diffing against the
    /// mobile nightly's commit.
    /// </summary>
    [Fact]
    public void The_scheduled_gate_watches_both_heads_since_the_newest_v_release()
    {
        var gate = Step(JobText(ReleaseWorkflow, "plan"), "Check scheduled release changes");

        Assert.DoesNotContain("--limit 1 ", gate, StringComparison.Ordinal);
        Assert.Contains("test(\"^v[0-9]+[.][0-9]+[.][0-9]+$\")", gate, StringComparison.Ordinal);
        Assert.Contains("src/App/Backlog.Desktop/Backlog.Desktop.csproj", gate, StringComparison.Ordinal);
        Assert.Contains("src/App/Backlog.Mobile/Backlog.Mobile.csproj", gate, StringComparison.Ordinal);
        Assert.Contains("'.github/workflows/release-all.yml'", gate, StringComparison.Ordinal);
        Assert.Contains("'build/Get-NextReleaseVersion.ps1'", gate, StringComparison.Ordinal);
    }

    /// <summary>
    /// A channel that cannot build skips the whole scheduled release, so the two
    /// never drift apart by a number, and fails any release someone asked for.
    /// </summary>
    [Fact]
    public void Missing_preconditions_skip_a_scheduled_release_and_fail_an_explicit_one()
    {
        var plan = JobText(ReleaseWorkflow, "plan");
        var preconditions = Step(plan, "Check release preconditions");

        foreach (var name in new[]
                 {
                     "WINDOWS_CERTIFICATE_BASE64", "WINDOWS_CERTIFICATE_PASSWORD", "ANDROID_KEYSTORE_BASE64",
                     "ANDROID_KEYSTORE_PASSWORD", "ANDROID_KEY_ALIAS", "ANDROID_KEY_PASSWORD", "BACKLOG_SYNC_SERVICE_URL"
                 })
        {
            Assert.Contains(name, preconditions, StringComparison.Ordinal);
        }

        Assert.Contains("'${{ github.event_name }}' -eq 'schedule'", preconditions, StringComparison.Ordinal);
        Assert.Contains("GITHUB_STEP_SUMMARY", preconditions, StringComparison.Ordinal);
        Assert.Contains("throw", preconditions, StringComparison.Ordinal);

        var jobs = Jobs(File.ReadAllLines(WorkflowPath(ReleaseWorkflow)));
        Assert.Equal("needs.plan.outputs.should_release == 'true'", JobKey(jobs.Single(j => j.Name == "sync-preflight"), "if"));
        foreach (var job in new[] { "desktop", "mobile", "sync", "publish" })
        {
            Assert.Equal("needs.sync-preflight.outputs.configured == 'true'", JobKey(jobs.Single(j => j.Name == job), "if"));
        }
    }

    /// <summary>
    /// The sync service's settings live on the <c>backlog-sync</c> environment, which
    /// only a job that declares it can read, so a small job declares it, after the
    /// change gate and away from the signing secrets, and a scheduled run with it
    /// unconfigured releases nothing, the apps included.
    /// </summary>
    [Fact]
    public void An_unconfigured_sync_environment_skips_a_scheduled_release_and_fails_an_explicit_one()
    {
        var preflight = JobText(ReleaseWorkflow, "sync-preflight");
        Assert.Contains("environment: ${{ inputs.sync_environment || 'backlog-sync' }}", preflight, StringComparison.Ordinal);
        Assert.Contains("needs: plan", preflight, StringComparison.Ordinal);

        var check = Step(preflight, "Check the sync service environment");
        foreach (var name in new[]
                 {
                     "AZURE_CLIENT_ID", "AZURE_TENANT_ID", "AZURE_SUBSCRIPTION_ID", "AZURE_RESOURCE_GROUP",
                     "AZURE_LOCATION", "SYNC_TOKEN_SIGNING_KEY"
                 })
        {
            Assert.Contains(name, check, StringComparison.Ordinal);
        }

        Assert.Contains("'${{ github.event_name }}' -eq 'schedule'", check, StringComparison.Ordinal);
        Assert.Contains("GITHUB_STEP_SUMMARY", check, StringComparison.Ordinal);
        Assert.Contains("throw", check, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each release deploys the sync service under its number, after both packages
    /// built and before anything is published, through Deploy Sync's
    /// <c>service</c> mode: the service and nothing of the infrastructure.
    /// </summary>
    [Fact]
    public void Release_all_deploys_the_sync_service_under_the_release_number()
    {
        var sync = JobText(ReleaseWorkflow, "sync");

        Assert.Contains("uses: ./.github/workflows/deploy-sync.yml", sync, StringComparison.Ordinal);
        Assert.Contains("needs: [plan, sync-preflight, desktop, mobile]", sync, StringComparison.Ordinal);
        Assert.Contains("mode: service", sync, StringComparison.Ordinal);
        Assert.Contains("environment_name: ${{ inputs.sync_environment || 'backlog-sync' }}", sync, StringComparison.Ordinal);
        Assert.Contains("release_tag: ${{ needs.plan.outputs.tag }}", sync, StringComparison.Ordinal);
        Assert.Contains("version: ${{ needs.plan.outputs.display }}", sync, StringComparison.Ordinal);
        Assert.Contains("id-token: write", sync, StringComparison.Ordinal);
        Assert.Contains("secrets: inherit", sync, StringComparison.Ordinal);

        Assert.Contains("sync", JobKey(Jobs(File.ReadAllLines(WorkflowPath(ReleaseWorkflow))).Single(j => j.Name == "report-failure"), "needs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A stale re-run of an older run must not put older service code into
    /// production under a number a newer run already published. "Re-run failed jobs"
    /// re-runs the sync job's steps but reuses every job that passed, so the check
    /// lives inside Deploy Sync, before it signs in to Azure: a release tag that
    /// already exists is deployed only from its own commit.
    /// </summary>
    [Fact]
    public void A_stale_rerun_never_redeploys_the_sync_service()
    {
        var job = JobText(DeploySyncWorkflow, "sync");
        var check = Step(job, "Check the release tag");

        Assert.Contains("if: env.RELEASE_TAG != ''", check, StringComparison.Ordinal);
        Assert.Contains("/commits/refs/tags/$env:RELEASE_TAG", check, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"-ne \$env:GITHUB_SHA"), check);
        Assert.Contains("RELEASE_TAG: ${{ inputs.release_tag }}", job, StringComparison.Ordinal);

        var position = job.IndexOf("      - name: Check the release tag", StringComparison.Ordinal);
        Assert.True(position < job.IndexOf("      - name: Sign in to Azure", StringComparison.Ordinal), "The release tag is checked after signing in to Azure.");
        Assert.True(position < job.IndexOf("      - name: Deploy the sync service", StringComparison.Ordinal), "The release tag is checked after the deploy.");

        Assert.Contains("/commits/refs/tags/$tag", Step(JobText(ReleaseWorkflow, "publish"), "Publish GitHub Release"), StringComparison.Ordinal);
    }

    /// <summary>
    /// One run deploys the sync infrastructure and its service to the same
    /// environment: Deploy All hands its <c>sync_environment</c> to Release All, which
    /// checks and deploys there, and defaults to production on its own triggers.
    /// </summary>
    [Fact]
    public void The_release_deploys_the_sync_service_to_the_environment_deploy_all_provisions()
    {
        var release = Workflow(ReleaseWorkflow).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Matches(new Regex(@"^      sync_environment:\n        required: false\n        default: backlog-sync\n        type: string\n", RegexOptions.Multiline), release);
        Assert.DoesNotMatch(new Regex(@"^\s+environment(_name)?: backlog-sync\s*$", RegexOptions.Multiline), release);

        var deployAll = JobText(DeployAllWorkflow, "release");
        Assert.Contains("sync_environment: ${{ inputs.sync_environment }}", deployAll, StringComparison.Ordinal);
    }

    /// <summary>
    /// A failed sync job may still have deployed: the deploy can succeed and only
    /// the version read-back fail, leaving a container serving a number no release
    /// carries. The failure issue says so rather than "not deployed".
    /// </summary>
    [Fact]
    public void The_failure_issue_does_not_claim_a_failed_sync_job_deployed_nothing()
    {
        var report = Step(JobText(ReleaseWorkflow, "report-failure"), "Create issue for release failure");

        Assert.Contains("deploy failed or unverified", report, StringComparison.Ordinal);
        Assert.DoesNotContain("not deployed", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// The service ships when the service changed: the gate watches the Sync.Api
    /// graph, <c>azure.yaml</c> and the deploy workflow, and not <c>infra/sync/</c>,
    /// which Release All never provisions.
    /// </summary>
    [Fact]
    public void The_scheduled_gate_watches_the_sync_service_but_not_its_infrastructure()
    {
        var gate = Step(JobText(ReleaseWorkflow, "plan"), "Check scheduled release changes");

        Assert.Contains("src/Modules/Sync/Backlog.Modules.Sync.Api/Backlog.Modules.Sync.Api.csproj", gate, StringComparison.Ordinal);
        Assert.Contains("'azure.yaml'", gate, StringComparison.Ordinal);
        Assert.Contains("'.github/workflows/deploy-sync.yml'", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("infra/sync", gate, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>service</c> deploys the container and provisions nothing, the preview
    /// included; the existing modes keep their steps.
    /// </summary>
    [Fact]
    public void The_service_mode_of_deploy_sync_provisions_nothing()
    {
        var workflow = Workflow(DeploySyncWorkflow);
        Assert.Matches(new Regex(@"^          - service\s*$", RegexOptions.Multiline), workflow);

        var job = JobText(DeploySyncWorkflow, "sync");
        Assert.Contains("if: env.DEPLOY_MODE != 'service'", Step(job, "Preview infrastructure changes"), StringComparison.Ordinal);
        Assert.Contains("if: env.DEPLOY_MODE == 'provision' || env.DEPLOY_MODE == 'deploy'", Step(job, "Provision infrastructure"), StringComparison.Ordinal);
        Assert.Contains("if: env.DEPLOY_MODE == 'deploy' || env.DEPLOY_MODE == 'service'", Step(job, "Deploy the sync service"), StringComparison.Ordinal);

        var load = Step(job, "Load the provisioned environment");
        Assert.Contains("if: env.DEPLOY_MODE == 'service'", load, StringComparison.Ordinal);
        Assert.Contains("azd env refresh", load, StringComparison.Ordinal);
        Assert.DoesNotContain("azd provision", load, StringComparison.Ordinal);
    }

    /// <summary>
    /// The template falls back to a public placeholder image when
    /// <c>SERVICE_SYNC_IMAGE_NAME</c> is empty, and on a fresh runner it always is, so
    /// an infrastructure run would replace the running service with the placeholder.
    /// Before anything provisions — the preview included — the workflow reads the
    /// running app's image and hands it back to the template; only a first provision,
    /// with no app yet, keeps the placeholder. The image kept lands on the run summary.
    /// </summary>
    [Fact]
    public void Provisioning_keeps_the_running_service_image()
    {
        var job = JobText(DeploySyncWorkflow, "sync");
        var keep = Step(job, "Keep the running service image");

        Assert.Contains("if: env.DEPLOY_MODE != 'service'", keep, StringComparison.Ordinal);
        Assert.Contains("--federated-token", keep, StringComparison.Ordinal);
        Assert.Contains("Microsoft.App/containerApps", keep, StringComparison.Ordinal);
        Assert.Contains("'azd-service-name'", keep, StringComparison.Ordinal);
        Assert.Contains("'azd-env-name'", keep, StringComparison.Ordinal);
        Assert.Contains("azd env set SERVICE_SYNC_IMAGE_NAME", keep, StringComparison.Ordinal);
        Assert.Contains("GITHUB_STEP_SUMMARY", keep, StringComparison.Ordinal);

        var position = job.IndexOf("      - name: Keep the running service image", StringComparison.Ordinal);
        Assert.True(position < job.IndexOf("      - name: Preview infrastructure changes", StringComparison.Ordinal), "The image is looked up after the preview.");
        Assert.True(position < job.IndexOf("      - name: Provision infrastructure", StringComparison.Ordinal), "The image is looked up after the provision.");
        Assert.True(job.IndexOf("      - name: Select azd environment", StringComparison.Ordinal) < position, "The image is set before the azd environment exists.");
    }

    /// <summary>
    /// The release number reaches the service's assembly as the MSBuild
    /// <c>Version</c> property, through the environment of the one step that
    /// publishes the container (azd runs <c>dotnet publish</c> in its own process
    /// environment), and the deployed <c>GET /</c> is read back before the release
    /// publishes: its version is the number and its commit the run's.
    /// </summary>
    [Fact]
    public void The_sync_service_is_stamped_with_the_release_version_and_checked()
    {
        var job = JobText(DeploySyncWorkflow, "sync");
        var deploy = Step(job, "Deploy the sync service");

        Assert.Contains("$env:Version = $env:RELEASE_VERSION", deploy, StringComparison.Ordinal);
        Assert.Contains("RELEASE_VERSION: ${{ inputs.version }}", job, StringComparison.Ordinal);
        Assert.Contains(@"'^\d+\.\d+\.\d+$'", Step(job, "Verify deployment target"), StringComparison.Ordinal);

        var others = Regex.Matches(job, @"^      - name: .*?(?=^      - name: |\z)", RegexOptions.Multiline | RegexOptions.Singleline)
            .Select(match => match.Value)
            .Where(step => !step.StartsWith("      - name: Deploy the sync service", StringComparison.Ordinal));
        Assert.All(others, step => Assert.DoesNotContain("$env:Version", step, StringComparison.Ordinal));

        var check = Step(job, "Check the deployed version");
        Assert.Contains("if: env.RELEASE_VERSION != ''", check, StringComparison.Ordinal);
        Assert.Contains("$env:GITHUB_SHA", check, StringComparison.Ordinal);
        Assert.Contains(".version", check, StringComparison.Ordinal);
        Assert.Contains(".commit", check, StringComparison.Ordinal);
        Assert.Contains("throw", check, StringComparison.Ordinal);
    }

    /// <summary>
    /// The service belongs to the release, so Deploy All's sync component is the
    /// infrastructure alone: its <c>deploy</c> mode maps to Deploy Sync's
    /// <c>provision</c>, never to <c>deploy</c>, or one combined run would deploy the
    /// service twice under two versions. Its release job grants the OIDC permission
    /// the nested sync deploy needs.
    /// </summary>
    [Fact]
    public void Deploy_all_provisions_sync_and_leaves_the_service_to_the_release()
    {
        var plan = JobText(DeployAllWorkflow, "plan");
        var syncSwitch = Regex.Match(plan, @"\$syncMode = switch \(\$env:MODE\) \{(?<body>.*?)\n\s*\}", RegexOptions.Singleline);

        Assert.True(syncSwitch.Success, "No $syncMode switch in Deploy All's plan step.");
        Assert.Contains("'deploy'    { 'provision' }", syncSwitch.Groups["body"].Value, StringComparison.Ordinal);
        Assert.DoesNotContain("{ 'deploy' }", syncSwitch.Groups["body"].Value, StringComparison.Ordinal);

        Assert.Contains("id-token: write", JobText(DeployAllWorkflow, "release"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The keystore is written to the runner only inside the mobile job, and is
    /// removed there whatever happened.
    /// </summary>
    [Fact]
    public void The_mobile_job_removes_its_keystore_whatever_happened()
    {
        Assert.Contains("if: always()", Step(JobText(ReleaseWorkflow, "mobile"), "Remove signing keystore"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Deploy All ships both apps by calling Release All, and lets it number the
    /// release: the counter no longer belongs to whichever workflow runs, so a blank
    /// version is the next number rather than Deploy All's own run number.
    /// </summary>
    [Fact]
    public void Deploy_all_calls_release_all_and_lets_it_number_the_release()
    {
        var deploy = Workflow(DeployAllWorkflow);

        Assert.Contains("uses: ./.github/workflows/release-all.yml", JobText(DeployAllWorkflow, "release"), StringComparison.Ordinal);
        Assert.DoesNotContain("release-desktop.yml", deploy, StringComparison.Ordinal);
        Assert.DoesNotContain("release-mobile.yml", deploy, StringComparison.Ordinal);
        Assert.DoesNotContain("is blank", deploy, StringComparison.Ordinal);
    }

    /// <summary>
    /// A nightly release never demotes a higher-versioned release from
    /// the Latest badge (issue #751). Latest is the App Installer update channel, so
    /// a nightly still takes it when it is the highest version; an explicit
    /// release always takes it. Both <c>gh release create</c> and
    /// <c>gh release edit</c> pass one computed flag, and the decision lands on
    /// the run summary.
    /// </summary>
    [Fact]
    public void A_nightly_release_takes_latest_only_from_a_lower_version()
    {
        var publish = Step(JobText(ReleaseWorkflow, "publish"), "Publish GitHub Release");

        Assert.DoesNotMatch(new Regex(@"--latest\s+\$msix"), publish);
        Assert.DoesNotMatch(new Regex(@"gh release edit .*--latest\s*$", RegexOptions.Multiline), publish);
        Assert.Contains("needs.plan.outputs.nightly", publish, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"gh release create .*\$latest \$msix"), publish);
        Assert.Matches(new Regex(@"gh release edit .*\$latest\s*$", RegexOptions.Multiline), publish);
        Assert.Contains("gh release view --json tagName", publish, StringComparison.Ordinal);
        Assert.Contains("[version]", publish, StringComparison.Ordinal);
        Assert.Contains("'--latest=false'", publish, StringComparison.Ordinal);
        Assert.Contains("GITHUB_STEP_SUMMARY", publish, StringComparison.Ordinal);
    }

    /// <summary>
    /// The release that holds Latest is the one App Installer reads
    /// <c>releases/latest/download/Backlog.Desktop.appinstaller</c> from, so every
    /// release this workflow publishes carries it, on create and on a rerun alike,
    /// beside the APK.
    /// </summary>
    [Fact]
    public void Every_release_carries_the_app_installer_and_the_apk()
    {
        var publish = Step(JobText(ReleaseWorkflow, "publish"), "Publish GitHub Release");

        Assert.Contains("Backlog.Desktop.appinstaller", publish, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"gh release create [^\n]*\$appInstaller"), publish);
        Assert.Matches(new Regex(@"gh release upload [^\n]*\$appInstaller"), publish);
        Assert.Matches(new Regex(@"gh release create [^\n]*\$apk"), publish);
        Assert.Matches(new Regex(@"gh release upload [^\n]*\$apk"), publish);
    }

    /// <summary>
    /// Numbers come from the tags, so two runs can plan the same one: a failed run
    /// re-run after a newer run already published that number would otherwise clobber
    /// the newer build under the same MSIX version and versionCode, which no installed
    /// device would ever pick up. An existing tag is republished only from its own
    /// commit, checked in the publish job (a re-run of failed jobs reuses the plan's
    /// outputs) and before anything builds.
    /// </summary>
    [Fact]
    public void An_existing_tag_is_republished_only_from_its_own_commit()
    {
        var publish = Step(JobText(ReleaseWorkflow, "publish"), "Publish GitHub Release");

        var guard = publish.IndexOf("/commits/refs/tags/$tag", StringComparison.Ordinal);
        Assert.True(guard >= 0, "The publish step does not resolve the existing tag's commit.");
        Assert.True(
            guard < publish.IndexOf("--clobber", StringComparison.Ordinal),
            "The publish step checks the tag's commit only after clobbering its assets.");
        Assert.Matches(new Regex(@"-ne \$env:GITHUB_SHA"), publish);

        var version = Step(JobText(ReleaseWorkflow, "plan"), "Derive version numbers");
        Assert.Contains("git rev-list -n 1", version, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"-ne \$env:GITHUB_SHA"), version);
    }

    /// <summary>
    /// A ref name is typed by a person and git allows a quote in it, so the version
    /// step reads it from the environment rather than having it spliced into the script.
    /// </summary>
    [Fact]
    public void The_version_step_reads_the_ref_from_the_environment()
    {
        var version = Step(JobText(ReleaseWorkflow, "plan"), "Derive version numbers");
        var script = version[version.IndexOf("run: |", StringComparison.Ordinal)..];

        Assert.DoesNotContain("${{", script, StringComparison.Ordinal);
        Assert.Contains("RELEASE_REF_NAME: ${{ github.ref_name }}", version, StringComparison.Ordinal);
        Assert.Contains("RELEASE_REF_TYPE: ${{ github.ref_type }}", version, StringComparison.Ordinal);
    }

    /// <summary>
    /// Inside Deploy All, <c>github.run_number</c> is Deploy All's counter, so a title
    /// keyed on it could land a failure on another workflow's open issue. The run id
    /// is unique across workflows.
    /// </summary>
    [Fact]
    public void The_failure_issue_is_keyed_on_the_run_id()
    {
        var report = Step(JobText(ReleaseWorkflow, "report-failure"), "Create issue for release failure");

        Assert.Contains("RELEASE_ISSUE_TITLE: 'Release All failed (${{ github.workflow }}): run ${{ github.run_id }}'", report, StringComparison.Ordinal);
        Assert.DoesNotContain("github.run_number", report, StringComparison.Ordinal);
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
    /// would merge untested code green.
    /// </summary>
    [Fact]
    public void The_pull_request_build_jobs_skip_only_on_a_devbook_only_change()
    {
        const string Gate = "${{ !cancelled() && needs.changes.outputs.code != 'false' }}";
        var jobs = Jobs(File.ReadAllLines(WorkflowPath(PullRequestWorkflow)));

        var detect = Assert.Single(jobs, job => job.Name == "changes");
        var builds = jobs.Where(job => job.Name != "changes").ToArray();

        Assert.NotEmpty(builds);
        Assert.All(builds, job =>
        {
            Assert.Equal("changes", JobKey(job, "needs"));
            Assert.Equal(Gate, JobKey(job, "if"));
        });

        var detection = string.Join('\n', detect.Lines);
        Assert.Contains("grep -v '^\\.devbook/'", detection, StringComparison.Ordinal);
        Assert.Contains("code=false", detection, StringComparison.Ordinal);
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

        foreach (var workflow in new[] { ReleaseWorkflow, "deploy-sync.yml" })
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
