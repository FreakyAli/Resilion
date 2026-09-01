namespace Resilion;

/// <summary>
/// Base class for all exceptions originated by Resilion strategies.
/// Catching this type catches any Resilion-specific failure (timeout, circuit broken, rate limited, etc.)
/// while letting <see cref="OperationCanceledException"/> and user exceptions pass through.
/// </summary>
public abstract class ResilionException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResilionException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    protected ResilionException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResilionException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    protected ResilionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
