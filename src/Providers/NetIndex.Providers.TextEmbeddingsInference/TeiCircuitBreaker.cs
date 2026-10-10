namespace NetIndex.Providers.TextEmbeddingsInference;

/// <summary>
/// Hand-written circuit breaker: closed, open, half-open. After <c>failureThreshold</c> consecutive failures the
/// circuit opens for <c>breakDuration</c>; then exactly one probe is admitted (half-open). A successful probe
/// closes the circuit and a failed one re-opens it.
/// </summary>
/// <remarks>
/// Every admitted call gets an <see cref="Admission"/> stamped with the breaker's generation, which advances on every
/// state change. An outcome reported with a stale admission (the call began before the circuit opened, closed or
/// admitted a probe) is ignored, so a slow call can never close an open circuit early or free a probe slot it does
/// not own.
/// </remarks>
internal sealed class TeiCircuitBreaker
{
    private readonly object _gate = new();
    private readonly int _failureThreshold;
    private readonly TimeSpan _breakDuration;
    private readonly TimeProvider _clock;
    private int _consecutiveFailures;
    private DateTimeOffset? _openUntil;
    private bool _probeInFlight;
    private long _generation;

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
    internal bool TryAcquire(out Admission admission)
    {
        lock (_gate)
        {
            if (_openUntil is null)
            {
                admission = new Admission(_generation);
                return true;
            }
            if (_clock.GetUtcNow() < _openUntil || _probeInFlight)
            {
                admission = default;
                return false;
            }
            _probeInFlight = true;
            _generation++;
            admission = new Admission(_generation);
            return true;
        }
    }

    /// <summary>Records that the service answered (the circuit closes).</summary>
    internal void RecordSuccess(Admission admission)
    {
        lock (_gate)
        {
            if (admission.Generation != _generation)
            {
                return;
            }
            _consecutiveFailures = 0;
            if (_openUntil is not null)
            {
                _openUntil = null;
                _probeInFlight = false;
                _generation++;
            }
        }
    }

    /// <summary>Records an outage-type failure.</summary>
    internal void RecordFailure(Admission admission)
    {
        lock (_gate)
        {
            if (admission.Generation != _generation)
            {
                return;
            }
            _consecutiveFailures++;
            if (_probeInFlight || _consecutiveFailures >= _failureThreshold)
            {
                _openUntil = _clock.GetUtcNow() + _breakDuration;
                _probeInFlight = false;
                _generation++;
            }
        }
    }

    /// <summary>Releases an admitted call that ended without information (caller cancellation).</summary>
    internal void Release(Admission admission)
    {
        lock (_gate)
        {
            if (admission.Generation != _generation)
            {
                return;
            }
            _probeInFlight = false;
        }
    }
}

/// <summary>Proof that a call was admitted, stamped with the breaker generation it was admitted under.</summary>
/// <param name="Generation">The breaker generation at admission.</param>
internal readonly record struct Admission(long Generation);

/// <summary>Circuit breaker states.</summary>
internal enum CircuitState
{
    Closed,
    Open,
    HalfOpen,
}
