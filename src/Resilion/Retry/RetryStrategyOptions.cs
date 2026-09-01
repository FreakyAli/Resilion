namespace Resilion;

/// <summary>
/// Options for the Retry resilience strategy (exception-only predicates).
/// </summary>
public sealed record RetryStrategyOptions
{
    /// <summary>
    /// Gets the maximum number of retry attempts (not counting the initial execution).
    /// Defaults to 3. Set to 0 for no retries.
    /// </summary>
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>
    /// Gets the delay strategy between retries. Defaults to exponential backoff starting at 1 second
    /// with a 30-second cap.
    /// </summary>
    public RetryDelay Delay { get; init; } = RetryDelay.Exponential(TimeSpan.FromSeconds(1));

    /// <summary>
    /// Gets a value indicating whether jitter should be applied to retry delays.
    /// Defaults to <c>true</c> (decorrelated jitter).
    /// </summary>
    public bool UseJitter { get; init; } = true;

    /// <summary>
    /// Gets the predicate that determines which exceptions trigger a retry.
    /// Defaults to all exceptions except <see cref="OperationCanceledException"/>.
    /// </summary>
    public Func<Exception, bool>? ShouldHandle { get; init; }

    /// <summary>
    /// Gets an optional event handler fired before each retry wait.
    /// </summary>
    public ResilienceEventHandler<RetryAttemptEvent>? OnRetry { get; init; }

    internal void Validate()
    {
        if (MaxRetryAttempts < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRetryAttempts), MaxRetryAttempts,
                "MaxRetryAttempts must be >= 0.");
        }
    }

    internal bool ShouldHandleException(Exception exception)
    {
        // OperationCanceledException is never retried by default.
        if (exception is OperationCanceledException)
        {
            return ShouldHandle?.Invoke(exception) ?? false;
        }

        return ShouldHandle?.Invoke(exception) ?? true;
    }
}

/// <summary>
/// Options for the Retry resilience strategy with result-based predicates.
/// </summary>
/// <typeparam name="TResult">The result type to inspect.</typeparam>
public sealed record RetryStrategyOptions<TResult>
{
    /// <summary>
    /// Gets the maximum number of retry attempts. Defaults to 3.
    /// </summary>
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>
    /// Gets the delay strategy between retries.
    /// </summary>
    public RetryDelay Delay { get; init; } = RetryDelay.Exponential(TimeSpan.FromSeconds(1));

    /// <summary>
    /// Gets a value indicating whether jitter should be applied.
    /// </summary>
    public bool UseJitter { get; init; } = true;

    /// <summary>
    /// Gets the predicate that determines which outcomes (exceptions or results) trigger a retry.
    /// Receives the <see cref="Outcome{TResult}"/> to enable both exception and result-based decisions.
    /// </summary>
    public Func<Outcome<TResult>, bool>? ShouldHandle { get; init; }

    /// <summary>
    /// Gets an optional event handler fired before each retry wait.
    /// </summary>
    public ResilienceEventHandler<RetryAttemptEvent<TResult>>? OnRetry { get; init; }

    internal void Validate()
    {
        if (MaxRetryAttempts < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRetryAttempts), MaxRetryAttempts,
                "MaxRetryAttempts must be >= 0.");
        }
    }

    internal bool ShouldHandleOutcome(Outcome<TResult> outcome)
    {
        // If we have a custom predicate, use it.
        if (ShouldHandle is not null)
        {
            return ShouldHandle(outcome);
        }

        // Default: handle any exception except OperationCanceledException.
        return outcome.Exception is not null and not OperationCanceledException;
    }
}

/// <summary>
/// Event arguments for the <see cref="RetryStrategyOptions.OnRetry"/> callback (exception-only).
/// </summary>
/// <param name="AttemptNumber">The 1-based retry attempt number.</param>
/// <param name="RetryDelay">The delay that will be waited before the next attempt.</param>
/// <param name="Exception">The exception that triggered this retry.</param>
/// <param name="Context">The execution context.</param>
public readonly record struct RetryAttemptEvent(
    int AttemptNumber,
    TimeSpan RetryDelay,
    Exception Exception,
    ResilienceContext Context);

/// <summary>
/// Event arguments for the <see cref="RetryStrategyOptions{TResult}.OnRetry"/> callback (outcome-based).
/// </summary>
/// <typeparam name="TResult">The result type.</typeparam>
/// <param name="AttemptNumber">The 1-based retry attempt number.</param>
/// <param name="RetryDelay">The delay that will be waited before the next attempt.</param>
/// <param name="Outcome">The outcome that triggered this retry.</param>
/// <param name="Context">The execution context.</param>
public readonly record struct RetryAttemptEvent<TResult>(
    int AttemptNumber,
    TimeSpan RetryDelay,
    Outcome<TResult> Outcome,
    ResilienceContext Context);
