using System.Diagnostics;

namespace Resilion;

/// <summary>
/// Resilience strategy that enforces a time limit on operations using cooperative cancellation.
/// </summary>
internal sealed class TimeoutStrategy : Strategy
{
    private readonly TimeoutStrategyOptions _options;
    private readonly TimeProvider _timeProvider;

    internal TimeoutStrategy(TimeoutStrategyOptions options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
    }

    protected internal override async ValueTask<Outcome<TResult>> ExecuteAsync<TResult>(
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context)
    {
        using var activity = StrategyActivity.Start("Timeout", context);

        var timeout = ResolveTimeout(context);

        if (timeout == System.Threading.Timeout.InfiniteTimeSpan)
        {
            StrategyActivity.SetOutcome(activity, "no_timeout");
            return await callback(context).ConfigureAwait(context.ContinueOnCapturedContext);
        }

        var previousToken = context.CancellationToken;
        var cause = new CancellationCause();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(previousToken);
        cause.Cts = linkedCts;

        // Only pay for the registration when the caller's token can actually be cancelled. For a
        // default CancellationToken — the common case — nothing but the timer can cancel the
        // linked source, so the cause is unambiguous without it.
        CancellationTokenRegistration userRegistration = default;
        if (previousToken.CanBeCanceled)
        {
            userRegistration = previousToken.Register(
                static state => ((CancellationCause)state!).SetUser(),
                cause,
                useSynchronizationContext: false);
        }

        var startTimestamp = _timeProvider.GetTimestamp();
        ITimer? timer = null;

        try
        {
            timer = _timeProvider.CreateTimer(
                static state =>
                {
                    var c = (CancellationCause)state!;

                    // Record the cause before propagating it. Any thread that can observe the
                    // cancellation must already be able to see why it happened.
                    c.SetTimeout();

                    try
                    {
                        c.Cts.Cancel();
                    }
                    catch
                    {
                        // Suppress exceptions from timer callback to prevent process termination.
                        // CancellationTokenSource.Cancel() can throw if user cancellation callbacks throw.
                        // We can't log here safely on a thread pool timer thread, but the timeout has
                        // already been triggered via cancellation request, so suppressing is acceptable.
                    }
                },
                cause,
                timeout,
                System.Threading.Timeout.InfiniteTimeSpan);

            context.CancellationToken = linkedCts.Token;

            try
            {
                var outcome = await callback(context).ConfigureAwait(context.ContinueOnCapturedContext);

                if (outcome.Exception is OperationCanceledException oce
                    && WasCancelledByTimeout(cause))
                {
                    var elapsed = _timeProvider.GetElapsedTime(startTimestamp);
                    var result = await HandleTimeout<TResult>(context, timeout, elapsed, oce).ConfigureAwait(false);
                    StrategyActivity.SetOutcome(activity, "timeout");
                    return result;
                }

                StrategyActivity.SetOutcome(activity, outcome.IsSuccess ? "success" : "failure");
                return outcome;
            }
            catch (OperationCanceledException oce) when (WasCancelledByTimeout(cause))
            {
                var elapsed = _timeProvider.GetElapsedTime(startTimestamp);
                var result = await HandleTimeout<TResult>(context, timeout, elapsed, oce).ConfigureAwait(false);
                StrategyActivity.SetOutcome(activity, "timeout");
                return result;
            }
        }
        finally
        {
            context.CancellationToken = previousToken;
            userRegistration.Dispose();

            if (timer is not null)
            {
                await timer.DisposeAsync().ConfigureAwait(false);
            }

            linkedCts.Dispose();
        }
    }

    protected internal override Outcome<TResult> Execute<TResult>(
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context)
    {
        using var activity = StrategyActivity.Start("Timeout", context);

        var timeout = ResolveTimeout(context);

        if (timeout == System.Threading.Timeout.InfiniteTimeSpan)
        {
            StrategyActivity.SetOutcome(activity, "no_timeout");
            return callback(context);
        }

        var previousToken = context.CancellationToken;
        var cause = new CancellationCause();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(previousToken);
        cause.Cts = linkedCts;

        // Only pay for the registration when the caller's token can actually be cancelled. For a
        // default CancellationToken — the common case — nothing but the timer can cancel the
        // linked source, so the cause is unambiguous without it.
        CancellationTokenRegistration userRegistration = default;
        if (previousToken.CanBeCanceled)
        {
            userRegistration = previousToken.Register(
                static state => ((CancellationCause)state!).SetUser(),
                cause,
                useSynchronizationContext: false);
        }

        var startTimestamp = _timeProvider.GetTimestamp();
        ITimer? timer = null;

        try
        {
            timer = _timeProvider.CreateTimer(
                static state =>
                {
                    var c = (CancellationCause)state!;

                    // Record the cause before propagating it. Any thread that can observe the
                    // cancellation must already be able to see why it happened.
                    c.SetTimeout();

                    try
                    {
                        c.Cts.Cancel();
                    }
                    catch
                    {
                        // Suppress exceptions from timer callback to prevent process termination.
                        // CancellationTokenSource.Cancel() can throw if user cancellation callbacks throw.
                        // We can't log here safely on a thread pool timer thread, but the timeout has
                        // already been triggered via cancellation request, so suppressing is acceptable.
                    }
                },
                cause,
                timeout,
                System.Threading.Timeout.InfiniteTimeSpan);

            context.CancellationToken = linkedCts.Token;
            var outcome = callback(context);

            if (outcome.Exception is OperationCanceledException oce
                && WasCancelledByTimeout(cause))
            {
                var elapsed = _timeProvider.GetElapsedTime(startTimestamp);
                HandleTimeoutSync(context, timeout, elapsed);
                StrategyActivity.SetOutcome(activity, "timeout");
                return Outcome<TResult>.FromException(
                    new TimeoutRejectedException(timeout, elapsed, oce));
            }

            StrategyActivity.SetOutcome(activity, outcome.IsSuccess ? "success" : "failure");
            return outcome;
        }
        catch (OperationCanceledException oce) when (WasCancelledByTimeout(cause))
        {
            var elapsed = _timeProvider.GetElapsedTime(startTimestamp);
            HandleTimeoutSync(context, timeout, elapsed);
            StrategyActivity.SetOutcome(activity, "timeout");
            return Outcome<TResult>.FromException(
                new TimeoutRejectedException(timeout, elapsed, oce));
        }
        finally
        {
            context.CancellationToken = previousToken;
            userRegistration.Dispose();
            timer?.Dispose();
            linkedCts.Dispose();
        }
    }

    private TimeSpan ResolveTimeout(ResilienceContext context)
    {
        if (_options.TimeoutGenerator is not null)
        {
            return _options.TimeoutGenerator(new TimeoutGeneratorArgs(context));
        }

        return _options.Timeout;
    }

    /// <summary>
    /// Determines whether the cancellation was caused by our timeout rather than the user's token.
    /// This is a single volatile read of a cause recorded at the moment cancellation happened, so
    /// there is no time-of-check/time-of-use window: a later cancellation from the other source
    /// cannot retroactively change the answer.
    /// </summary>
    private static bool WasCancelledByTimeout(CancellationCause cause) => cause.WasTimeout;

    /// <summary>
    /// Records which cause cancelled the linked token <em>first</em>. The first writer wins via
    /// compare-and-swap, which is what makes classification race-free.
    /// </summary>
    /// <remarks>
    /// Reading <c>linkedCts.IsCancellationRequested &amp;&amp; !userToken.IsCancellationRequested</c>
    /// instead is two separate reads, and the user token can change between them — misclassifying
    /// user cancellation as a timeout. Pre-capturing the user token's state before the callback does
    /// not fix it either; it only reverses the direction of the error, turning a real timeout into a
    /// reported user cancellation when the user cancels after the timer fires. The question that
    /// actually needs answering is "which happened first?", so that is what this records.
    /// <para>
    /// Ordering is guaranteed: <see cref="CancellationTokenSource"/> invokes callbacks in reverse
    /// registration order, and the linked source's internal propagation registration is created by
    /// <c>CreateLinkedTokenSource</c> — before ours. So on user cancellation <c>SetUser</c> runs
    /// before <c>linkedCts</c> reports cancellation. The timer calls <c>SetTimeout</c> before
    /// <c>Cancel</c>. Either way the cause is recorded before any consumer can observe the effect.
    /// </para>
    /// </remarks>
    private sealed class CancellationCause
    {
        private const int None = 0;
        private const int Timeout = 1;
        private const int User = 2;

        private int _cause;

        internal CancellationTokenSource Cts = null!;

        internal void SetTimeout() => Interlocked.CompareExchange(ref _cause, Timeout, None);

        internal void SetUser() => Interlocked.CompareExchange(ref _cause, User, None);

        internal bool WasTimeout => Volatile.Read(ref _cause) == Timeout;
    }

    /// <summary>
    /// Handles timeout on the async path. Preserves the original OCE as inner exception.
    /// </summary>
    private async ValueTask<Outcome<TResult>> HandleTimeout<TResult>(
        ResilienceContext context,
        TimeSpan timeout,
        TimeSpan elapsed,
        Exception originalException)
    {
        ResilionTelemetry.TimeoutExpirations.Add(1, new(ResilionTelemetry.PipelineNameTag, context.PipelineName), new(ResilionTelemetry.OperationKeyTag, context.OperationKey));

        if (_options.OnTimeout is { } handler && handler.HasHandler)
        {
            await handler.InvokeAsync(new OnTimeoutArgs(context, timeout, elapsed)).ConfigureAwait(false);
        }

        return Outcome<TResult>.FromException(
            new TimeoutRejectedException(timeout, elapsed, originalException));
    }

    private void HandleTimeoutSync(
        ResilienceContext context,
        TimeSpan timeout,
        TimeSpan elapsed)
    {
        ResilionTelemetry.TimeoutExpirations.Add(1, new(ResilionTelemetry.PipelineNameTag, context.PipelineName), new(ResilionTelemetry.OperationKeyTag, context.OperationKey));

        if (_options.OnTimeout is { } handler && handler.HasHandler)
        {
            handler.Invoke(new OnTimeoutArgs(context, timeout, elapsed));
        }
    }
}
