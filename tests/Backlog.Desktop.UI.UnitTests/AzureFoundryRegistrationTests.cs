using Backlog.Infrastructure.AzureFoundry;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What a host gets for calling <c>AddAzureFoundryChatClient()</c>, asserted the
/// way the Capture registration is: the options read back under the name the
/// handler registers them, and the handler chain walked the way a request
/// travels.
/// </summary>
public sealed class AzureFoundryRegistrationTests : IDisposable
{
    private readonly List<string> _paths = [];

    public void Dispose()
    {
        foreach (var path in _paths)
        {
            var directory = Path.GetDirectoryName(path);
            if (directory is null) continue;

            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void The_chat_client_resolves_as_the_foundry_client()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new AzureFoundrySettingsStore(NewSettingsPath()));
        services.AddAzureFoundryChatClient();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        Assert.IsType<AzureFoundryChatClient>(provider.GetRequiredService<IAzureFoundryChatClient>());
    }

    /// <summary>The standard pipeline's defaults — ten seconds an attempt,
    /// thirty in all — are sized for a service answering a service. A chat
    /// completion over a screen of tasks routinely takes longer than the
    /// attempt, so every answer was cut off, re-sent and cut off again until
    /// the total ran out. One answer gets the whole budget.</summary>
    [Fact]
    public void The_chat_client_waits_two_minutes_for_one_answer()
    {
        var services = new ServiceCollection();
        services.AddAzureFoundryChatClient();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get($"{nameof(IAzureFoundryChatClient)}-standard");

        Assert.Equal(AzureFoundryRegistration.AnswerBudget, options.AttemptTimeout.Timeout);
        Assert.Equal(AzureFoundryRegistration.AnswerBudget, options.TotalRequestTimeout.Timeout);
        Assert.Equal(TimeSpan.FromMinutes(2), AzureFoundryRegistration.AnswerBudget);
    }

    /// <summary>Both hosts call <c>AddServiceDefaults()</c>, which puts the
    /// standard pipeline on every client. This one must replace it rather than
    /// sit inside it: the outer pipeline's thirty seconds would still be the
    /// one that fires.</summary>
    [Fact]
    public void The_chat_client_carries_one_resilience_handler_even_under_the_hosts_defaults()
    {
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
        services.AddAzureFoundryChatClient();

        using var provider = services.BuildServiceProvider();
        var chain = HandlerChain(provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(nameof(IAzureFoundryChatClient)));

        Assert.Single(chain.OfType<ResilienceHandler>());
    }

    private string NewSettingsPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "backlog-foundry-registration", Guid.NewGuid().ToString("n"), "azure-foundry.json");
        _paths.Add(path);
        return path;
    }

    /// <summary>The factory's handler, the additional handlers in order, and
    /// the primary handler last — walked the way a request travels.</summary>
    private static List<HttpMessageHandler> HandlerChain(HttpMessageHandler handler)
    {
        var chain = new List<HttpMessageHandler>();

        for (var current = handler; current is not null; current = (current as DelegatingHandler)?.InnerHandler)
        {
            chain.Add(current);
        }

        return chain;
    }
}
