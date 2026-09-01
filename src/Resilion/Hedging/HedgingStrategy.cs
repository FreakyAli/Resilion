namespace Resilion;

/// <summary>
/// Hedging strategy — races concurrent attempts and returns the first successful result.
/// All losing attempts are cancelled and awaited for proper resource cleanup.
/// </summary>
internal sealed class HedgingStrategy<TResult> : Strategy<TResult>
{
    private readonly HedgingStrategyOptions<TResult> _options;
    private readonly TimeProvider _timeProvider;

    internal HedgingStrategy(HedgingStrategyOptions<TResult> options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
    }

    protected internal override async ValueTask<Outcome<TResult>> ExecuteAsync(
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context)
    {
        if (_options.MaxHedgedAttempts == 1)
        {
            // No hedging — just execute the primary.
            return await callback(context).ConfigureAwait(context.ContinueOnCapturedContext);
        }

        var userToken = context.CancellationToken;
        var attempts = new List<Task<Outcome<TResult>>>(_options.MaxHedgedAttempts);
        var perAttemptCts = new List<CancellationTokenSource>(_options.MaxHedgedAttempts);

        try
        {
            // Launch primary attempt (attempt 0).
            LaunchAttempt(0, callback, context, userToken, attempts, perAttemptCts);

            for (var attemptIndex = 1; attemptIndex < _options.MaxHedgedAttempts; attemptIndex++)
            {
                // Wait for the hedging delay, or for the primary/earlier attempt to complete.
                if (_options.HedgingDelay == System.Threading.Timeout.InfiniteTimeSpan)
                {
                    // Sequential mode: wait for the current attempt to complete before launching next.
                    var completed = await attempts[^1].ConfigureAwait(false);
                    if (!_options.ShouldHandleOutcome(completed))
                    {
                        // Success — no need for more attempts.
                        return completed;
                    }
                }
                else if (_options.HedgingDelay > TimeSpan.Zero)
                {
                    // Latency mode: wait for the delay, but also check if any attempt completes early.
                    var delayTask = Task.Delay(_options.HedgingDelay, _timeProvider, userToken);
                    var winner = await Task.WhenAny(Task.WhenAny(attempts), delayTask).ConfigureAwait(false);

                    if (winner != delayTask)
                    {
                        // An attempt completed before the delay. Check if it succeeded.
                        var completedTask = await Task.WhenAny(attempts).ConfigureAwait(false);
                        var completed = await completedTask.ConfigureAwait(false);
                        if (!_options.ShouldHandleOutcome(completed))
                        {
                            return completed;
                        }
                    }
                }
                // Parallel mode (delay == 0): launch immediately, no waiting.

                ResilionTelemetry.HedgingAttempts.Add(1);

                // Fire OnHedging event.
                if (_options.OnHedging is { } handler && handler.HasHandler)
                {
                    await handler.InvokeAsync(new OnHedgingEvent<TResult>(attemptIndex, context))
                        .ConfigureAwait(false);
                }

                // Launch next hedged attempt.
                LaunchAttempt(attemptIndex, callback, context, userToken, attempts, perAttemptCts);
            }

            // All attempts launched. Wait for the first success or all to fail.
            return await WaitForBestOutcome(attempts, userToken).ConfigureAwait(false);
        }
        finally
        {
            // Cancel all remaining in-flight attempts.
            foreach (var cts in perAttemptCts)
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed — fine.
                }
            }

            // Await all tasks to ensure resource cleanup (connections, streams, etc.).
            // Use a bounded timeout to prevent indefinite hangs on non-cooperative tasks.
            var cleanupTimeout = TimeSpan.FromSeconds(5);
            foreach (var task in attempts)
            {
                try
                {
                    await Task.WhenAny(task, Task.Delay(cleanupTimeout)).ConfigureAwait(false);
                }
                catch
                {
                    // Swallow — we've already got our result or failure.
                }
            }

            // Dispose all CTS instances.
            foreach (var cts in perAttemptCts)
            {
                cts.Dispose();
            }
        }
    }

    protected internal override Outcome<TResult> Execute(
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context)
    {
        // Hedging is inherently async (parallel execution).
        // For the sync path, execute sequentially (equivalent to InfiniteTimeSpan mode).
        Outcome<TResult> lastOutcome = default;

        for (var attemptIndex = 0; attemptIndex < _options.MaxHedgedAttempts; attemptIndex++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var action = ResolveAction(attemptIndex, callback, context);
            lastOutcome = action(context);

            if (!_options.ShouldHandleOutcome(lastOutcome))
            {
                return lastOutcome;
            }

            if (attemptIndex < _options.MaxHedgedAttempts - 1)
            {
                if (_options.OnHedging is { } handler && handler.HasHandler)
                {
                    handler.Invoke(new OnHedgingEvent<TResult>(attemptIndex + 1, context));
                }
            }
        }

        return lastOutcome;
    }

    private void LaunchAttempt(
        int attemptIndex,
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context,
        CancellationToken userToken,
        List<Task<Outcome<TResult>>> attempts,
        List<CancellationTokenSource> perAttemptCts)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(userToken);
        perAttemptCts.Add(cts);

        var action = ResolveAsyncAction(attemptIndex, callback, context);
        var token = cts.Token;

        var task = Task.Run(async () =>
        {
            try
            {
                return await action(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested && !userToken.IsCancellationRequested)
            {
                // Cancelled by hedging strategy (not by user) — treat as a handled failure.
                return Outcome<TResult>.FromException(new OperationCanceledException("Hedging attempt was cancelled."));
            }
        }, userToken);

        attempts.Add(task);
    }

    private Func<CancellationToken, ValueTask<Outcome<TResult>>> ResolveAsyncAction(
        int attemptIndex,
        Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context)
    {
        if (_options.ActionGenerator is not null)
        {
            var customAction = _options.ActionGenerator(new HedgingActionContext(attemptIndex));
            if (customAction is not null)
            {
                return async ct =>
                {
                    try
                    {
                        var result = await customAction(ct).ConfigureAwait(false);
                        return Outcome<TResult>.FromResult(result);
                    }
                    catch (Exception ex)
                    {
                        return Outcome<TResult>.FromException(ex);
                    }
                };
            }
        }

        // Default: re-execute the original callback.
        return async ct =>
        {
            // Create a lightweight context copy with the per-attempt token.
            var attemptContext = ResilienceContextPool.Shared.Rent(ct);
            attemptContext.OperationKey = context.OperationKey;
            attemptContext.ContinueOnCapturedContext = context.ContinueOnCapturedContext;
            attemptContext.Properties.CopyFrom(context.Properties);
            try
            {
                return await callback(attemptContext).ConfigureAwait(false);
            }
            finally
            {
                ResilienceContextPool.Shared.Return(attemptContext);
            }
        };
    }

    private Func<ResilienceContext, Outcome<TResult>> ResolveAction(
        int attemptIndex,
        Func<ResilienceContext, Outcome<TResult>> callback,
        ResilienceContext context)
    {
        if (_options.ActionGenerator is not null)
        {
            var customAction = _options.ActionGenerator(new HedgingActionContext(attemptIndex));
            if (customAction is not null)
            {
                return ctx =>
                {
                    try
                    {
                        var result = customAction(ctx.CancellationToken).GetAwaiter().GetResult();
                        return Outcome<TResult>.FromResult(result);
                    }
                    catch (Exception ex)
                    {
                        return Outcome<TResult>.FromException(ex);
                    }
                };
            }
        }

        return callback;
    }

    private async Task<Outcome<TResult>> WaitForBestOutcome(
        List<Task<Outcome<TResult>>> attempts,
        CancellationToken userToken)
    {
        var remaining = new List<Task<Outcome<TResult>>>(attempts);
        Outcome<TResult> lastFailure = default;

        while (remaining.Count > 0)
        {
            userToken.ThrowIfCancellationRequested();

            var completed = await Task.WhenAny(remaining).ConfigureAwait(false);
            remaining.Remove(completed);

            var outcome = await completed.ConfigureAwait(false);

            if (!_options.ShouldHandleOutcome(outcome))
            {
                return outcome;
            }

            // Track the last failure in case all attempts fail.
            lastFailure = outcome;
        }

        // All attempts failed — return the last failure outcome.
        return lastFailure;
    }
}
