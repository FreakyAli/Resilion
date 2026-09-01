namespace Resilion;

/// <summary>
/// Thrown when an operation exceeds its configured timeout duration.
/// </summary>
/// <remarks>
/// <para>
/// This exception is thrown only when the timeout fires — not when the user's original
/// <see cref="CancellationToken"/> is canceled. If the user's token is canceled, the original
/// <see cref="OperationCanceledException"/> propagates unchanged.
/// </para>
/// <para>
/// The <see cref="Exception.InnerException"/> contains the <see cref="OperationCanceledException"/>
/// that was thrown by the timed-out operation.
/// </para>
/// </remarks>
public sealed class TimeoutRejectedException : ResilionException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TimeoutRejectedException"/> class.
    /// </summary>
    /// <param name="configuredTimeout">The timeout duration that was configured.</param>
    /// <param name="elapsedTime">The actual elapsed time before timeout was triggered.</param>
    /// <param name="innerException">The underlying cancellation exception.</param>
    internal TimeoutRejectedException(
        TimeSpan configuredTimeout,
        TimeSpan elapsedTime,
        Exception innerException)
        : base($"The operation did not complete within the configured timeout of {configuredTimeout.TotalSeconds:F1}s " +
               $"(elapsed: {elapsedTime.TotalSeconds:F1}s).",
               innerException)
    {
        ConfiguredTimeout = configuredTimeout;
        ElapsedTime = elapsedTime;
    }

    /// <summary>
    /// Gets the timeout duration that was configured on the strategy.
    /// </summary>
    public TimeSpan ConfiguredTimeout { get; }

    /// <summary>
    /// Gets the actual time that elapsed before the timeout was triggered.
    /// </summary>
    public TimeSpan ElapsedTime { get; }
}
