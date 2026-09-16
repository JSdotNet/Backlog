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
/// Four things are deliberately not registered here, because all four are
/// ports and which adapter answers each is the host's decision, the same as
/// <c>IRoadmapPlanRepository</c>. <see cref="ICaptureSourceSettings"/> and
/// <see cref="ICaptureRunLog"/> — today two JSON files beside the app's other
/// per-user choices. The <c>ICaptureSourceAdapter</c>s — the feed readers
/// under <c>Backlog.Infrastructure.Capture</c>, one per kind, and a kind
/// without one is reported per source rather than needed. And
/// <c>ICaptureDelivery</c> — the Inbox's intake, behind an adapter in the same
/// project because Capture may not see the Inbox. A host that composes an
/// adapter without a delivery fails provider validation rather than running
/// captures into nothing.
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
