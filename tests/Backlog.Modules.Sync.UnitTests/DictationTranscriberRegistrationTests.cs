using Backlog.Modules.Sync.Adapters;
using Backlog.Modules.Sync.Extensions;
using Backlog.Modules.Sync.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// The transcriber the module composes when the host configured none (local ADR 0045 §3):
/// a stand-in that says "unavailable", so the route answers 503 and the phone falls back —
/// and that a host's own transcriber, registered first, replaces.
/// </summary>
public sealed class DictationTranscriberRegistrationTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task With_no_transcriber_registered_the_module_answers_unavailable()
    {
        using var provider = new ServiceCollection().AddSyncModule().BuildServiceProvider();

        var transcriber = provider.GetRequiredService<IDictationTranscriber>();
        var answer = await transcriber.TranscribeAsync(new DictationAudio(new byte[] { 1, 2, 3 }, "audio/mp4", null), Cancellation);

        Assert.Equal(DictationTranscription.Unavailable, answer);
    }

    [Fact]
    public void A_transcriber_the_host_registered_first_wins()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDictationTranscriber, HostTranscriber>();
        services.AddSyncModule();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<HostTranscriber>(provider.GetRequiredService<IDictationTranscriber>());
        Assert.IsNotType<NoDictationTranscriber>(provider.GetRequiredService<IDictationTranscriber>());
    }

    private sealed class HostTranscriber : IDictationTranscriber
    {
        public Task<DictationTranscription> TranscribeAsync(DictationAudio audio, CancellationToken cancellationToken = default) =>
            Task.FromResult(DictationTranscription.Transcribed("host"));
    }
}
