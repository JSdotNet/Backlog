using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// Org ADR 0002 puts the deployed service's settings, secrets and alert rules in the AppHost.
/// <c>SyncDeployment.cs</c> models them; <c>infra/sync/main.bicep</c> still deploys the
/// service until the deployment moves onto <c>aspire publish</c>, so for now the two name the
/// same settings and these rules keep them from drifting apart.
///
/// <para>Read as text, for the reason <see cref="AspireAppModelTests"/> gives: no test project
/// can reference the AppHost. <c>aspire publish</c> is what proves the model generates; these
/// stop the failures a green build misses.</para>
/// </summary>
public partial class AppHostDeploymentModelTests
{
    private static readonly string[] SyncDeploymentSource = ["src", "Aspire", "Backlog.Aspire.AppHost", "SyncDeployment.cs"];

    private static readonly string[] AlertTemplate = ["src", "Aspire", "Backlog.Aspire.AppHost", "Alerts", "sync-exception-alert.bicep"];

    private static readonly string[] SyncBicep = ["infra", "sync", "main.bicep"];

    private static readonly string[] ExceptionsWorkflow = [".github", "workflows", "app-insights-exceptions.yml"];

    /// <summary>The settings the deployed container carries that come from neither a resource
    /// the template declares nor the service's own appsettings: each is an AppHost parameter.</summary>
    private static readonly string[] ParameterisedSettings =
    [
        "Owner__GitHubUserId",
        "Owner__OwnerId",
        "GitHub__ClientId",
        "Modules__Sync__Tokens__KeyVaultUri",
        "Modules__Sync__Connector__Issuer",
        "Modules__Sync__Connector__Resource",
        "Sync__NotificationHubs__Hub",
        "Sync__NotificationHubs__SendConnectionString",
        "Sync__NotificationHubs__ManageConnectionString",
        "APPLICATIONINSIGHTS_CONNECTION_STRING",
    ];

    /// <summary>The parameters a publish cannot do without. Each is declared with no value,
    /// so an environment that has not set it fails at publish instead of deploying a service
    /// that refuses to start.</summary>
    private static readonly string[] RequiredParameters =
    [
        "sync-owner-github-user-id",
        "sync-github-oauth-client-id",
        "sync-key-vault-uri",
        "sync-public-origin",
        "sync-notification-hubs-send-connection-string",
        "sync-notification-hubs-manage-connection-string",
        "sync-alert-email",
    ];

    private static readonly string[] SecretParameters =
    [
        "sync-notification-hubs-send-connection-string",
        "sync-notification-hubs-manage-connection-string",
    ];

    [Fact]
    public void Every_deployed_sync_setting_is_an_apphost_parameter_the_bicep_also_sets()
    {
        var model = Read(SyncDeploymentSource);
        var bicep = Read(SyncBicep);

        foreach (var setting in ParameterisedSettings)
        {
            Assert.True(bicep.Contains($"'{setting}'", StringComparison.Ordinal),
                $"infra/sync/main.bicep no longer sets {setting}. Drop it from SyncDeployment.cs too, or put it back.");

            if (setting == "APPLICATIONINSIGHTS_CONNECTION_STRING")
            {
                // Set by the reference to the component, not by name.
                Assert.Contains("sync.WithReference(appInsights)", model, StringComparison.Ordinal);
                continue;
            }

            Assert.True(model.Contains($"\"{setting}\"", StringComparison.Ordinal),
                $"SyncDeployment.cs does not model {setting}, which the deployed sync service is given.");
        }
    }

    [Fact]
    public void Required_parameters_have_no_default_and_secrets_are_marked_secret()
    {
        var declarations = ParameterDeclaration().Matches(Read(SyncDeploymentSource))
            .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["rest"].Value.Trim());

        foreach (var name in RequiredParameters)
        {
            Assert.True(declarations.TryGetValue(name, out var rest), $"SyncDeployment.cs does not declare the parameter {name}.");

            var withoutSecretFlag = rest!.Replace("secret: true", "", StringComparison.Ordinal).Trim(' ', ',');
            Assert.True(withoutSecretFlag.Length == 0,
                $"{name} is required and must have no default, but is declared with ({rest}).");
        }

        foreach (var name in SecretParameters)
        {
            Assert.Contains("secret: true", declarations[name], StringComparison.Ordinal);
        }
    }

    /// <summary>A run-mode parameter with no value holds its resource until someone answers
    /// the dashboard, which would stop every unattended <c>aspire start</c>.</summary>
    [Fact]
    public void The_deployment_model_is_added_in_publish_mode_only()
    {
        var model = Read(SyncDeploymentSource);

        var guard = model.IndexOf("if (!builder.ExecutionContext.IsPublishMode) return;", StringComparison.Ordinal);
        var firstParameter = model.IndexOf("builder.AddParameter(", StringComparison.Ordinal);

        Assert.True(guard >= 0 && guard < firstParameter,
            "SyncDeployment.AddPublishedSyncSettings must return in run mode before it declares a parameter.");
    }

    /// <summary>The alert and the workflow read the same signal: the component's
    /// <c>exceptions</c>, one per <c>problemId</c>.</summary>
    [Fact]
    public void The_exception_alert_watches_what_the_exceptions_workflow_files_issues_from()
    {
        var template = Read(AlertTemplate);
        var workflow = Read(ExceptionsWorkflow);

        Assert.Contains("Microsoft.Insights/scheduledQueryRules", template, StringComparison.Ordinal);
        Assert.Contains("query: 'exceptions | summarize occurrences = count() by problemId'", template, StringComparison.Ordinal);
        Assert.Contains("name: 'problemId'", template, StringComparison.Ordinal);

        Assert.Contains("exceptions", workflow, StringComparison.Ordinal);
        Assert.Contains("by problemId", workflow, StringComparison.Ordinal);

        Assert.Contains("builder.AddBicepTemplate(\"sync-alerts\", ExceptionAlertTemplate)", Read(SyncDeploymentSource), StringComparison.Ordinal);
        Assert.Contains("\"Alerts/sync-exception-alert.bicep\"", Read(SyncDeploymentSource), StringComparison.Ordinal);
    }

    private static string Read(string[] path) => File.ReadAllText(RepositoryRoot.File(path));

    [GeneratedRegex(@"AddParameter\(""(?<name>[\w-]+)""(?<rest>[^)]*)\)")]
    private static partial Regex ParameterDeclaration();
}
