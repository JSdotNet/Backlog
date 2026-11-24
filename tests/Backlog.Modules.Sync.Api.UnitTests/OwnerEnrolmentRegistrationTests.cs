using Backlog.Modules.Sync.Api.Security;
using Backlog.Modules.Sync.Features.Enrolment;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// What the host makes of an unset <c>Owner:OwnerId</c> (local ADR 0058 §1): a deployed
/// service guards enrolment against orphaning the owner it already holds and says so at
/// startup; a Development run does neither. Built on the registration alone, because a
/// Production host of the whole service needs a vault.
/// </summary>
public class OwnerEnrolmentRegistrationTests
{
    private static readonly Guid PinnedOwner = Guid.Parse("01a0a754-2903-7ecb-8f5b-dcb92b9a3010");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    [InlineData("Development", false)]
    public void Only_a_deployed_service_guards_an_unpinned_owner(string environment, bool guards)
    {
        using var host = Host(environment, SyncServiceFactory.ConfiguredOwner, new CapturingLoggerProvider());

        Assert.Equal(guards, host.Services.GetRequiredService<OwnerEnrolment>().GuardsUnpinnedOwner);
    }

    [Fact]
    public async Task A_deployed_service_without_a_pinned_owner_warns_at_startup()
    {
        var logs = new CapturingLoggerProvider();
        using var host = Host("Production", SyncServiceFactory.ConfiguredOwner, logs);

        await host.StartAsync(Cancellation);
        await host.StopAsync(Cancellation);

        Assert.Contains(logs.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Owner__OwnerId", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    public async Task No_startup_warning_where_the_owner_is_pinned_or_the_run_is_local(string environment, bool pinned)
    {
        var logs = new CapturingLoggerProvider();
        using var host = Host(
            environment,
            pinned ? SyncServiceFactory.PinnedOwner(PinnedOwner) : SyncServiceFactory.ConfiguredOwner,
            logs);

        await host.StartAsync(Cancellation);
        await host.StopAsync(Cancellation);

        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains("Owner__OwnerId", StringComparison.Ordinal));
    }

    private static IHost Host(string environment, IReadOnlyList<(string Key, string? Value)> configuration, CapturingLoggerProvider logs)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment });

        foreach (var (key, value) in configuration)
        {
            builder.Configuration[key] = value;
        }

        builder.Logging.AddProvider(logs);
        builder.AddOwnerEnrolment();
        return builder.Build();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        // Differs from RecordingLogger: a provider hands it out for every category the host logs under, and all of them write to one list.
        private sealed class Logger(CapturingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (provider._entries)
                {
                    provider._entries.Add((logLevel, formatter(state, exception)));
                }
            }
        }
    }
}
