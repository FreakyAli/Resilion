using System.Net.Http;

namespace Resilion.Http.Internal;

/// <summary>
/// Tracks the <see cref="HttpResponseMessage"/> produced by each attempt so the ones that lose are
/// disposed rather than leaked.
/// </summary>
/// <remarks>
/// The pipeline discards non-final outcomes without disposing them: a retry overwrites its outcome
/// each iteration, and hedging returns the first success and abandons the rest. An undisposed
/// response holds its connection, so under retry-heavy traffic this exhausts the pool.
/// <para>
/// This cannot be done with <c>OnRetry</c>, because <c>ResilienceEventHandler</c>'s invocation is
/// internal to Resilion — a user-supplied handler can only be replaced, not wrapped — so the
/// handler has to own disposal itself.
/// </para>
/// </remarks>
internal sealed class HttpAttemptTracker
{
    private readonly object _gate = new();
    private readonly bool _disposePreviousEagerly;

    private int _attempts = -1;
    private HttpResponseMessage? _last;
    private List<HttpResponseMessage>? _others;
    private bool _closed;

    /// <param name="disposePreviousEagerly">
    /// When attempts cannot overlap, dispose the previous response as soon as the next attempt
    /// starts rather than waiting for the pipeline to finish. With three retries and exponential
    /// backoff, "at the end" can be tens of seconds of a connection held for nothing.
    /// </param>
    internal HttpAttemptTracker(bool disposePreviousEagerly)
        => _disposePreviousEagerly = disposePreviousEagerly;

    /// <summary>Marks the start of an attempt and returns its 0-based index.</summary>
    internal int BeginAttempt()
    {
        var index = Interlocked.Increment(ref _attempts);

        if (_disposePreviousEagerly && index > 0)
        {
            HttpResponseMessage? previous;
            lock (_gate)
            {
                previous = _last;
                _last = null;
            }

            previous?.Dispose();
        }

        return index;
    }

    /// <summary>Records a response produced by an attempt.</summary>
    internal void Track(HttpResponseMessage response)
    {
        lock (_gate)
        {
            if (!_closed)
            {
                if (_disposePreviousEagerly)
                {
                    _last = response;
                }
                else
                {
                    (_others ??= []).Add(response);
                }

                return;
            }
        }

        // Closed means the handler has already returned, so nobody will ever read this. Hedging
        // cancels losers and waits only for a bounded period before returning, so a straggler can
        // land here — dispose on the spot rather than leaking it.
        response.Dispose();
    }

    /// <summary>
    /// Disposes every tracked response except <paramref name="winner"/> and refuses further
    /// tracking.
    /// </summary>
    internal void CloseAndDisposeAllExcept(HttpResponseMessage? winner)
    {
        HttpResponseMessage? last;
        List<HttpResponseMessage>? others;

        lock (_gate)
        {
            _closed = true;
            last = _last;
            others = _others;
            _last = null;
            _others = null;
        }

        if (last is not null && !ReferenceEquals(last, winner))
        {
            last.Dispose();
        }

        if (others is null)
        {
            return;
        }

        foreach (var response in others)
        {
            if (!ReferenceEquals(response, winner))
            {
                response.Dispose();
            }
        }
    }
}
