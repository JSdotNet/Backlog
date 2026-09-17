using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backlog.Infrastructure.FileSystem.Logging;

/// <summary>
/// Where the log files are, for a screen that wants to say so. Registered by
/// <see cref="FileLoggingRegistration.AddFileLogging"/> and absent on a host
/// that keeps no file, so a screen resolves it as optional.
/// </summary>
public sealed record AppLogLocation(string Directory);

public static class FileLoggingRegistration
{
    /// <summary>
    /// The daily file, with the two filters that decide what reaches it.
    /// <para>
    /// The app's own categories from <see cref="LogLevel.Information"/>: the
    /// sync workers log a cycle that did not complete there, on purpose, and
    /// that line — with the service's code and sentence — is the one a person
    /// opens the file for. Everything else from <see cref="LogLevel.Warning"/>:
    /// the framework narrates every HTTP request and every component render at
    /// Information, and a file that carried all of it would bury the failure
    /// it exists to keep.
    /// </para>
    /// </summary>
    public static ILoggingBuilder AddFileLogging(this ILoggingBuilder logging, string directory)
    {
        ArgumentNullException.ThrowIfNull(logging);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        logging.Services.AddSingleton(new AppLogLocation(directory));
        logging.Services.AddSingleton<ILoggerProvider>(services =>
            new FileLoggerProvider(directory, services.GetService<TimeProvider>() ?? TimeProvider.System));

        logging.AddFilter<FileLoggerProvider>(category: null, LogLevel.Warning);
        logging.AddFilter<FileLoggerProvider>("Backlog", LogLevel.Information);

        return logging;
    }
}
