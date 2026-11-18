namespace Backlog.Mobile.UI.Recordings;

/// <summary>
/// Notices a recording that hears nothing at all: a level of exactly 0 held for
/// <see cref="Hold"/> while recording. A room is never that quiet — the microphone's own noise
/// moves the level — so a level that stays at 0 means another app has the microphone, on a
/// recorder that cannot say so itself (Android before 10, and the browser).
/// </summary>
/// <remarks>Fed from the recorder's own threads, so every reading is taken under a lock.</remarks>
public sealed class FlatLevelDetector
{
    /// <summary>Ten seconds: long enough that a pause in speech never trips it.</summary>
    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(10);

    private readonly Lock _gate = new();
    private DateTimeOffset? _silentSince;

    /// <summary>Takes one reading; answers whether the level has been 0 for <see cref="Hold"/>.</summary>
    public bool Observe(double level, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (level > 0)
            {
                _silentSince = null;
                return false;
            }

            _silentSince ??= now;
            return now - _silentSince.Value >= Hold;
        }
    }

    /// <summary>Forgets the silence so far: a pause or a resume starts the count again.</summary>
    public void Reset()
    {
        lock (_gate) _silentSince = null;
    }
}
