namespace NetIndex.Providers.TextEmbeddingsInference;

/// <summary>
/// Hand-written circuit breaker: closed, open, half-open. After <c>failureThreshold</c> consecutive failures the
/// circuit opens for <c>breakDuration</c>; then exactly one probe is admitted (half-open). A successful probe
/// closes the circuit and a failed one re-opens it.
/// </summary>
internal sealed class TeiCircuitBreaker
{
    private readonly object _gate = new();
    private readonly int _failureThreshold;
    private readonly TimeSpan _breakDuration;
    private readonly TimeProvider _clock;
    private int _consecutiveFailures;
    private DateTimeOffset? _openUntil;
    private bool _probeInFlight;

    internal TeiCircuitBreaker(int failureThreshold, TimeSpan breakDuration, TimeProvider clock)
    {
        _failureThreshold = failureThreshold;
        _breakDuration = breakDuration;
        _clock = clock;
    }

    /// <summary>Gets the current state, derived from the clock.</summary>
    internal CircuitState State
    {
        get
        {
            lock (_gate)
            {
                if (_openUntil is null)
                {
                    return CircuitState.Closed;
                }
                return _clock.GetUtcNow() < _openUntil ? CircuitState.Open : CircuitState.HalfOpen;
            }
        }
    }

    /// <summary>Tries to admit a call. Returns false when the call must be rejected without any network attempt.</summary>
    internal bool TryAcquire()
    {
        lock (_gate)
        {
            if (_openUntil is null)
            {
                return true;
            }
            if (_clock.GetUtcNow() < _openUntil || _probeInFlight)
            {
                return false;
            }
            _probeInFlight = true;
            return true;
        }
    }

    /// <summary>Records that the service answered (the circuit closes).</summary>
    internal void RecordSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            _openUntil = null;
            _probeInFlight = false;
        }
    }

    /// <summary>Records an outage-type failure.</summary>
    internal void RecordFailure()
    {
        lock (_gate)
        {
            _consecutiveFailures++;
            if (_probeInFlight || _consecutiveFailures >= _failureThreshold)
            {
                _openUntil = _clock.GetUtcNow() + _breakDuration;
                _probeInFlight = false;
            }
        }
    }

    /// <summary>Releases an admitted call that ended without information (caller cancellation).</summary>
    internal void Release()
    {
        lock (_gate)
        {
            _probeInFlight = false;
        }
    }
}

/// <summary>Circuit breaker states.</summary>
internal enum CircuitState
{
    Closed,
    Open,
    HalfOpen,
}
