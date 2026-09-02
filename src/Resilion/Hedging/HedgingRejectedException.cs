namespace Resilion;

/// <summary>
/// Thrown when all hedging attempts fail.
/// </summary>
public sealed class HedgingRejectedException : ResilionException
{
    internal HedgingRejectedException(IReadOnlyList<Exception> attemptExceptions)
        : base("All hedging attempts failed.")
    {
        AttemptExceptions = attemptExceptions;
    }

    /// <summary>
    /// Gets the exceptions from all hedging attempts, in attempt order.
    /// The first element is the primary attempt's exception.
    /// </summary>
    public IReadOnlyList<Exception> AttemptExceptions { get; }
}
