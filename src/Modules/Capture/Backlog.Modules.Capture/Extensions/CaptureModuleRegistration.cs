using Backlog.Modules.Capture.Abstractions.DataTransferObjects;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Capture.Features.RunCapture;
using Backlog.Modules.Capture.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Modules.Capture.Extensions;

/// <summary>
/// The module's composition root. A host calls this once and gets the run;
/// it never registers the handler itself.
/// <para>
/// Two things are deliberately not registered here. <see cref="ICaptureSourceSettings"/>
/// is a port, and which adapter answers it — today the JSON file beside the
/// app's other per-user choices — is the host's decision, the same as
/// <c>IRoadmapPlanRepository</c>. And no <c>ICaptureSourceAdapter</c>: none
/// ships yet, and the run says so per source rather than needing one to exist.
/// </para>
/// </summary>
public static class CaptureModuleRegistration
{
    public static IServiceCollection AddCaptureModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICommandHandler<RunCaptureCommand, Result<CaptureRunResultDto>>, RunCaptureCommandHandler>();
        services.AddScoped<ICaptureRunner, CaptureRunner>();

        // The run stamps when it happened. TryAdd, so a host that already fixed
        // the clock — a test, or the dashboard's fake — keeps its own.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
