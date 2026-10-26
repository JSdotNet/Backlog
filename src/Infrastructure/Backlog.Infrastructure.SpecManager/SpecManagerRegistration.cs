using Backlog.Infrastructure.SpecManager.Api;
using Backlog.Infrastructure.SpecManager.OAuth;
using Backlog.Infrastructure.SpecManager.Questions;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.Modules.Tasks.Extensions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backlog.Infrastructure.SpecManager;

/// <summary>
/// The one call a host makes to ship the spec-manager connector: its options, its
/// token store, its HTTP clients and pipeline, the sign-in, and the connector —
/// registered as an <see cref="ITaskConnector"/> for the sync and, the same
/// instance, as an <see cref="ITaskConnectorSignIn"/> for the settings screen.
/// <para>
/// And the product's questions as notifications: the <see cref="SpecManagerQuestionSource"/>
/// a head resolves once to start it, the answer handler for the <c>spec-manager</c> source
/// and the probe for its subjects. They use the Notifications context's ports when the host
/// composes them, and stay idle when it does not.
/// </para>
/// </summary>
public static class SpecManagerRegistration
{
    /// <param name="services">The host's services. The sync's
    /// <see cref="IConnectedTargets"/> is read from here when registered.</param>
    /// <param name="tokenStore">Where the sign-in is kept;
    /// <see cref="SpecManagerTokenStoreFactory.Create"/> at its default path when
    /// null.</param>
    /// <param name="configure">Changes to <see cref="SpecManagerOptions"/>, such as
    /// another installation's base URL.</param>
    /// <param name="configuration">The <see cref="SpecManagerOptions.SectionName"/>
    /// section to bind the options from (ADR 0018 in
    /// <c>.devbook/arc42/adr/guidelines/</c>): <c>SpecManager:BaseUrl</c>, so a harness
    /// can point the app at a local spec-manager. Bound before
    /// <paramref name="configure"/>, and validated when the app starts.</param>
    public static IServiceCollection AddSpecManager(
        this IServiceCollection services,
        Func<IServiceProvider, ISpecManagerTokenStore>? tokenStore = null,
        Action<SpecManagerOptions>? configure = null,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<SpecManagerOptions>();
        if (configuration is not null) options.Bind(configuration);
        if (configure is not null) options.Configure(configure);
        options.Validate(value => value.IsValid(), "SpecManager:BaseUrl must be an absolute http or https URL.");
        if (configuration is not null) options.ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(tokenStore ?? (_ => SpecManagerTokenStoreFactory.Create()));
        services.AddSpecManagerHttpClient();

        services.TryAddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new SpecManagerSignIn(
                () => factory.CreateClient(SpecManagerClient.HttpClientName),
                sp.GetRequiredService<ISpecManagerTokenStore>(),
                sp.GetRequiredService<IOptions<SpecManagerOptions>>().Value,
                sp.GetRequiredService<TimeProvider>(),
                SystemBrowser.OpenAsync,
                sp.GetService<ILogger<SpecManagerSignIn>>());
        });
        services.TryAddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new SpecManagerClient(
                () => factory.CreateClient(SpecManagerClient.HttpClientName),
                sp.GetRequiredService<SpecManagerSignIn>(),
                sp.GetRequiredService<IOptions<SpecManagerOptions>>().Value,
                () => factory.CreateClient(SpecManagerClient.StreamHttpClientName));
        });

        services.AddTaskConnector<SpecManagerConnector>();

        // The connector the sync holds, not a second one: AddTaskConnector registers
        // it by type, so the container builds it there and this resolves that one.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITaskConnectorSignIn, SpecManagerConnector>(
            sp => sp.GetServices<ITaskConnector>().OfType<SpecManagerConnector>().Single()));

        AddQuestions(services);

        return services;
    }

    /// <summary>The questions: the source, the answer handler and the subject probe.</summary>
    private static void AddQuestions(IServiceCollection services)
    {
        services.TryAddSingleton<ISpecManagerLinkedWorkLookup>(sp => new SpecManagerLinkedWorkLookup(sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<TimeProvider>()));

        services.TryAddSingleton(sp => new SpecManagerQuestionSync(
            sp.GetRequiredService<SpecManagerClient>(),
            sp.GetRequiredService<SpecManagerSignIn>(),
            sp.GetService<IConnectedTargets>(),
            sp.GetRequiredService<INotifications>(),
            sp.GetService<INotificationReplication>(),
            sp.GetRequiredService<ISpecManagerLinkedWorkLookup>(),
            sp.GetRequiredService<TimeProvider>(),
            IsDefaultInstallation(sp),
            sp.GetService<ILogger<SpecManagerQuestionSync>>()));

        services.TryAddSingleton(sp => new SpecManagerQuestionSource(
            sp.GetRequiredService<SpecManagerQuestionSync>(),
            new SpecManagerEventStreamConnector(sp.GetRequiredService<SpecManagerClient>()),
            sp.GetRequiredService<SpecManagerSignIn>(),
            sp.GetService<IConnectedTargets>(),
            sp.GetService<INotificationAnswerReplay>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ILogger<SpecManagerQuestionSource>>()));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<INotificationAnswerHandler, SpecManagerAnswers>(sp => new SpecManagerAnswers(
            sp.GetRequiredService<SpecManagerClient>(),
            sp.GetRequiredService<SpecManagerSignIn>(),
            sp.GetService<INotifications>,
            sp.GetService<INotificationReplication>,
            IsDefaultInstallation(sp),
            sp.GetService<ILogger<SpecManagerAnswers>>())));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<INotificationSubjectProbe, SpecManagerQuestionProbe>(sp => new SpecManagerQuestionProbe(
            sp.GetRequiredService<SpecManagerClient>(),
            sp.GetRequiredService<SpecManagerSignIn>(),
            sp.GetService<IConnectedTargets>(),
            sp.GetRequiredService<TimeProvider>())));
    }

    /// <summary>Whether the options name the installation that ships, which a question's
    /// origin then does not spell out.</summary>
    private static bool IsDefaultInstallation(IServiceProvider services) =>
        string.Equals(
            services.GetRequiredService<IOptions<SpecManagerOptions>>().Value.Root,
            new SpecManagerOptions().Root,
            StringComparison.OrdinalIgnoreCase);
}
