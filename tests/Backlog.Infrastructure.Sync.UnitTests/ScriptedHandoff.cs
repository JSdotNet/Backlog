namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The pairing-code handoff between the two harnesses, in memory, with the
/// desktop's side scripted for the phone's tests: asked whenever the phone looks
/// while its request is open and no code waits, it answers the code to hand over,
/// or null for "not yet". The desktop's own tests drive
/// <see cref="HandOver"/> through the real minting loop instead.
/// </summary>
internal sealed class ScriptedHandoff : IPairingCodeHandoff
{
    private readonly Lock _lock = new();
    private string? _code;
    private bool _requested;
    private int _handOverFailures;

    /// <summary>The desktop as the test scripts it, given the number of the
    /// request it answers, counted from one.</summary>
    public Func<int, string?> Desktop { get; init; } = _ => null;

    /// <summary>Whether the code handed over for a request, counted from one,
    /// cannot be read: it is dropped when the phone takes it, as an envelope that
    /// will not open is.</summary>
    public Func<int, bool> Unreadable { get; init; } = _ => false;

    /// <summary>How many hand-overs fail with an <see cref="IOException"/> before
    /// one gets through, as a file a scanner holds does.</summary>
    public int FailingHandOvers
    {
        init => _handOverFailures = value;
    }

    /// <summary>How many times the phone asked for a code.</summary>
    public int Requests { get; private set; }

    /// <summary>How many times the phone looked for one.</summary>
    public int Takes { get; private set; }

    /// <summary>How many times the desktop withdrew a request it gave up on.</summary>
    public int Withdrawn { get; private set; }

    public bool CodeRequested
    {
        get
        {
            lock (_lock) return _requested;
        }
    }

    public bool CodeHandedOver
    {
        get
        {
            AnswerAsTheDesktop();
            lock (_lock) return _code is not null;
        }
    }

    public void RequestCode()
    {
        lock (_lock)
        {
            Requests++;
            _code = null;
            _requested = true;
        }
    }

    public void HandOver(string code)
    {
        lock (_lock)
        {
            if (_handOverFailures > 0)
            {
                _handOverFailures--;
                throw new IOException("The process cannot access the file because it is being used by another process.");
            }

            _code = code;
            _requested = false;
        }
    }

    public void WithdrawRequest()
    {
        lock (_lock)
        {
            Withdrawn++;
            _requested = false;
        }
    }

    public string? TakeCode()
    {
        int request;
        lock (_lock)
        {
            Takes++;
            request = Requests;
        }

        AnswerAsTheDesktop();

        lock (_lock)
        {
            var code = _code;
            _code = null;
            return code is not null && Unreadable(request) ? null : code;
        }
    }

    private void AnswerAsTheDesktop()
    {
        int request;
        lock (_lock)
        {
            if (!_requested || _code is not null) return;
            request = Requests;
        }

        if (Desktop(request) is { } answered) HandOver(answered);
    }
}
