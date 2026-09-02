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
        var timeout = ResolveTimeout(context);

        if (timeout == System.Threading.Timeout.InfiniteTimeSpan)
        {
            return await callback(context).ConfigureAwait(context.ContinueOnCapturedContext);
        }

        var previousToken = context.CancellationToken;
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(previousToken);
        var startTimestamp = _timeProvider.GetTimestamp();
        ITimer? timer = null;

        try
        {
            timer = _timeProvider.CreateTimer(
                static state =>
                {
                    try
                    {
                        ((CancellationTokenSource)state!).Cancel();
                    }
                    catch
                    {
                        // Suppress exceptions from timer callback to prevent process termination.
                        // CancellationTokenSource.Cancel() can throw if user cancellation callbacks throw.
                        // We can't log here safely on a thread pool timer thread, but the timeout has
                        // already been triggered via cancellation request, so suppressing is acceptable.
                    }
                },
                linkedCts,
                timeout,
                System.Threading.Timeout.InfiniteTimeSpan);

            context.CancellationToken = linkedCts.Token;

            try
            {
                var outcome = await callback(context).ConfigureAwait(context.ContinueOnCapturedContext);

                if (outcome.Exception is OperationCanceledException oce
                    && WasCancelledByTimeout(linkedCts, previousToken))
                {
                    var elapsed = _timeProvider.GetElapsedTime(startTimestamp);
                    return await HandleTimeout<TResult>(context, timeout, elapsed, oce).ConfigureAwait(false);
                }

                return outcome;
            }
            catch (OperationCanceledException oce) when (WasCancelledByTimeout(linkedCts, previousToken))
            {
                var elapsed = _timeProvider.GetElapsedTime(startTimestamp);
                return await HandleTimeout<TResult>(context, timeout, elapsed, oce).ConfigureAwait(false);
            }
        }
        finally
        {
            context.CancellationToken = previousToken;

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
        var timeout = ResolveTimeout(context);

        if (timeout == System.Threading.Timeout.InfiniteTimeSpan)
        {
            return callback(context);
        }

        var previousToken = context.CancellationToken;
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(previousToken);
        var startTimestamp = _timeProvider.GetTimestamp();
        ITimer? timer = null;

        try
        {
            timer = _timeProvider.CreateTimer(
                static state =>
                {
                    try
                    {
                        ((CancellationTokenSource)state!).Cancel();
                    }
                    catch
                    {
                        // Suppress exceptions from timer callback to prevent process termination.
                        // CancellationTokenSource.Cancel() can throw if user cancellation callbacks throw.
                        // We can't log here safely on a thread pool timer thread, but the timeout has
                        // already been triggered via cancellation request, so suppressing is acceptable.
                    }
                },
                linkedCts,
                timeout,
                System.Threading.Timeout.InfiniteTimeSpan);

            context.CancellationToken = linkedCts.Token;
            var outcome = callback(context);

            if (outcome.Exception is OperationCanceledException oce
                && WasCancelledByTimeout(linkedCts, previousToken))
            {
                var elapsed = _timeProvider.GetElapsedTime(startTimestamp);
                HandleTimeoutSync(context, timeout, elapsed);
                return Outcome<TResult>.FromException(
                    new TimeoutRejectedException(timeout, elapsed, oce));
            }

            return outcome;
        }
        catch (OperationCanceledException oce) when (WasCancelledByTimeout(linkedCts, previousToken))
        {
            var elapsed = _timeProvider.GetElapsedTime(startTimestamp);
            HandleTimeoutSync(context, timeout, elapsed);
            return Outcome<TResult>.FromException(
                new TimeoutRejectedException(timeout, elapsed, oce));
        }
        finally
        {
            context.CancellationToken = previousToken;
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
    /// In rare cases where user cancellation races with timeout, this may misclassify.
    /// </summary>
    private static bool WasCancelledByTimeout(
        CancellationTokenSource linkedCts,
        CancellationToken userToken)
    {
        // Check both atomically as possible: if linked CTS fired AND user token didn't.
        return linkedCts.IsCancellationRequested && !userToken.IsCancellationRequested;
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
        ResilionTelemetry.TimeoutExpirations.Add(1);

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
        ResilionTelemetry.TimeoutExpirations.Add(1);

        if (_options.OnTimeout is { } handler && handler.HasHandler)
        {
            handler.Invoke(new OnTimeoutArgs(context, timeout, elapsed));
        }
    }
}
