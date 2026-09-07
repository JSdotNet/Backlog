using Backlog.SharedKernel;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// One switch, so a test can turn task sync on and off. The real store reads a
/// catalog and a file, and neither is what anything here is about — nothing in
/// this project asks which key it was handed, because the worker asks about
/// exactly one.
/// </summary>
internal sealed class StubFeatureSettings(bool enabled) : IAppFeatureSettings
{
    private bool _enabled = enabled;

    public event Action? Changed;

    public AppFeatureSettings Current { get; } = new();

    public string SettingsPath => "in memory";

    public bool IsEnabled(string key) => _enabled;

    public string? SetEnabled(string key, bool enabled)
    {
        _enabled = enabled;
        Changed?.Invoke();
        return null;
    }
}
