using System.Net.Http;

namespace Resilion.Http.Internal;

/// <summary>
/// Executes each outbound request through a <see cref="Pipeline{TResult}"/> of
/// <see cref="HttpResponseMessage"/>.
/// </summary>
internal sealed class ResilienceHandler : DelegatingHandler
{
    private readonly Pipeline<HttpResponseMessage> _pipeline;
    private readonly HttpRequestReplay _replay;
    private readonly bool _sequentialAttempts;

    /// <param name="pipeline">The pipeline to execute each request through.</param>
    /// <param name="replay">Whether each attempt sends the original request or a clone.</param>
    /// <param name="sequentialAttempts">
    /// <see langword="true"/> when attempts cannot overlap, which lets a discarded response be
    /// disposed as soon as the next attempt begins rather than at the end of the pipeline.
    /// </param>
    internal ResilienceHandler(
        Pipeline<HttpResponseMessage> pipeline,
        HttpRequestReplay replay,
        bool sequentialAttempts)
    {
        _pipeline = pipeline;
        _replay = replay;
        _sequentialAttempts = sequentialAttempts;
    }

    /// <summary>
    /// The state handed to the pipeline callback.
    /// </summary>
    /// <remarks>
    /// A struct passed as <c>TState</c> with a <c>static</c> lambda, so executing a request adds no
    /// closure allocation of its own on top of the pipeline's.
    /// </remarks>
    private readonly struct SendState
    {
        internal SendState(
            ResilienceHandler handler,
            HttpRequestMessage request,
            byte[]? bufferedContent,
            HttpAttemptTracker tracker)
        {
            Handler = handler;
            Request = request;
            BufferedContent = bufferedContent;
            Tracker = tracker;
        }

        internal ResilienceHandler Handler { get; }

        internal HttpRequestMessage Request { get; }

        internal byte[]? BufferedContent { get; }

        internal HttpAttemptTracker Tracker { get; }
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendCoreAsync(request, cancellationToken);
    }

    /// <summary>
    /// Synchronous sending is not supported.
    /// </summary>
    /// <remarks>
    /// This override is mandatory, not defensive. <see cref="DelegatingHandler.Send"/> forwards
    /// straight to the inner handler, so without it a caller using <c>HttpClient.Send</c> would
    /// silently bypass the entire resilience pipeline and believe it was protected.
    /// <para>
    /// Throwing rather than implementing it: <see cref="Pipeline{TResult}"/> has no synchronous
    /// <c>ExecuteOutcome</c>, and the outcome is what identifies the winning response so the losers
    /// can be disposed. A sync path would therefore either leak responses or behave differently
    /// from the async one.
    /// </para>
    /// </remarks>
    protected override HttpResponseMessage Send(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        => throw new NotSupportedException(
            "Resilion's HTTP resilience handler does not support synchronous HttpClient.Send. " +
            "Use SendAsync / the async HttpClient methods.");

    private async Task<HttpResponseMessage> SendCoreAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        byte[]? buffered = null;
        if (_replay == HttpRequestReplay.CloneRequest && request.Content is not null)
        {
            // Buffered outside the pipeline deliberately: reading the caller's body is not an
            // attempt, so it should not consume the total timeout or be retried.
            buffered = await request.Content
                .ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var tracker = new HttpAttemptTracker(disposePreviousEagerly: _sequentialAttempts);

        // Rent rather than let the pipeline own the context: ExecuteOutcomeAsync is caller-owned,
        // and Rent(token) is the only way to set the execution's cancellation token.
        var context = ResilienceContextPool.Shared.Rent(cancellationToken);

        // The HTTP method, not the URI: bounded cardinality for the operation.key telemetry tag,
        // and interned so it costs nothing.
        context.OperationKey = request.Method.Method;
        context.Properties.Set(HttpResilienceKeys.RequestMessage, request);

        HttpResponseMessage? winner = null;
        try
        {
            var outcome = await _pipeline.ExecuteOutcomeAsync(
                static (state, ctx) => state.Handler.SendAttemptAsync(state, ctx),
                new SendState(this, request, buffered, tracker),
                context).ConfigureAwait(false);

            winner = outcome.IsSuccess ? outcome.GetResultOrDefault() : null;
            return outcome.ThrowIfFailed();
        }
        finally
        {
            // Covers strategies that throw rather than returning an outcome, too —
            // ExecuteOutcomeAsync does not wrap those.
            tracker.CloseAndDisposeAllExcept(winner);

            // Reset() clears Properties, so the request does not outlive the call via the pool.
            ResilienceContextPool.Shared.Return(context);
        }
    }

    private async ValueTask<Outcome<HttpResponseMessage>> SendAttemptAsync(
        SendState state,
        ResilienceContext context)
    {
        var attempt = state.Tracker.BeginAttempt();

        var message = attempt == 0 && _replay == HttpRequestReplay.ReuseRequest
            ? state.Request
            : HttpRequestMessageCloner.Clone(state.Request, state.BufferedContent);

        // Overwritten per attempt so GetRequestMessage() from a callback sees this attempt's
        // message rather than the first one's.
        context.Properties.Set(HttpResilienceKeys.RequestMessage, message);

        try
        {
            // context.CancellationToken, not the caller's: an enclosing Timeout strategy replaces
            // it with its own linked token, which is how per-attempt timeouts actually bite.
            var response = await base
                .SendAsync(message, context.CancellationToken)
                .ConfigureAwait(context.ContinueOnCapturedContext);

            state.Tracker.Track(response);
            return Outcome<HttpResponseMessage>.FromResult(response);
        }
        catch (Exception ex)
        {
            // Returned as an outcome rather than rethrown, including OperationCanceledException:
            // the Timeout strategy inspects the outcome to decide whether a cancellation was its
            // own timeout or the caller's, and it cannot do that if the exception unwinds past it.
            return Outcome<HttpResponseMessage>.FromException(ex);
        }
        finally
        {
            if (!ReferenceEquals(message, state.Request))
            {
                // Disposes the clone and its ByteArrayContent; the shared body array is untouched.
                message.Dispose();
            }
        }
    }
}
