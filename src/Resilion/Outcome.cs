using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace Resilion;

/// <summary>
/// A discriminated union representing either a successful result of type <typeparamref name="TResult"/>
/// or a captured <see cref="Exception"/>. Used throughout the pipeline to avoid re-throwing exceptions
/// on the hot path — strategies pass outcomes as data rather than catching and rethrowing.
/// </summary>
/// <typeparam name="TResult">The type of the successful result.</typeparam>
[StructLayout(LayoutKind.Auto)]
public readonly struct Outcome<TResult> : IEquatable<Outcome<TResult>>
{
    private readonly TResult? _result;
    private readonly ExceptionDispatchInfo? _exceptionDispatchInfo;

    private Outcome(TResult? result, ExceptionDispatchInfo? exceptionDispatchInfo)
    {
        _result = result;
        _exceptionDispatchInfo = exceptionDispatchInfo;
    }

    /// <summary>
    /// Gets a value indicating whether this outcome represents a successful result.
    /// </summary>
    public bool IsSuccess => _exceptionDispatchInfo is null;

    /// <summary>
    /// Gets a value indicating whether this outcome represents a failure (captured exception).
    /// </summary>
    public bool IsFailure => _exceptionDispatchInfo is not null;

    /// <summary>
    /// Gets the exception that caused the failure, or <c>null</c> if this outcome is a success.
    /// </summary>
    public Exception? Exception => _exceptionDispatchInfo?.SourceException;

    /// <summary>
    /// Gets the successful result value.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the outcome is a failure.</exception>
    public TResult Result
    {
        get
        {
            if (_exceptionDispatchInfo is not null)
            {
                throw new InvalidOperationException(
                    "Cannot access Result on a failed Outcome. Check IsSuccess before accessing Result, " +
                    "or use TryGetResult/GetResultOrDefault.",
                    _exceptionDispatchInfo.SourceException);
            }

            return _result!;
        }
    }

    /// <summary>
    /// Attempts to get the successful result value.
    /// </summary>
    /// <param name="result">When this method returns <c>true</c>, contains the result value.</param>
    /// <returns><c>true</c> if this outcome is a success; <c>false</c> otherwise.</returns>
    public bool TryGetResult([MaybeNullWhen(false)] out TResult result)
    {
        if (_exceptionDispatchInfo is null)
        {
            result = _result!;
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Returns the successful result value, or the specified default if this outcome is a failure.
    /// </summary>
    /// <param name="defaultValue">The value to return if this outcome is a failure.</param>
    /// <returns>The result value on success, or <paramref name="defaultValue"/> on failure.</returns>
    public TResult GetResultOrDefault(TResult defaultValue = default!)
        => _exceptionDispatchInfo is null ? _result! : defaultValue;

    /// <summary>
    /// Returns the result if successful, or rethrows the captured exception with its original stack trace preserved.
    /// </summary>
    /// <returns>The successful result value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TResult ThrowIfFailed()
    {
        _exceptionDispatchInfo?.Throw();
        return _result!;
    }

    /// <summary>
    /// Applies one of two functions depending on whether this outcome is a success or failure.
    /// </summary>
    /// <typeparam name="T">The return type of both functions.</typeparam>
    /// <param name="onSuccess">Function to apply if the outcome is a success.</param>
    /// <param name="onFailure">Function to apply if the outcome is a failure.</param>
    /// <returns>The result of the applied function.</returns>
    public T Match<T>(Func<TResult, T> onSuccess, Func<Exception, T> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return _exceptionDispatchInfo is null
            ? onSuccess(_result!)
            : onFailure(_exceptionDispatchInfo.SourceException);
    }

    /// <summary>
    /// Creates a successful outcome containing the specified result.
    /// </summary>
    /// <param name="result">The result value.</param>
    /// <returns>A successful <see cref="Outcome{TResult}"/>.</returns>
    public static Outcome<TResult> FromResult(TResult result)
        => new(result, null);

    /// <summary>
    /// Creates a failed outcome containing the specified exception.
    /// </summary>
    /// <param name="exception">The exception representing the failure.</param>
    /// <returns>A failed <see cref="Outcome{TResult}"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="exception"/> is <c>null</c>.</exception>
    public static Outcome<TResult> FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new(default, ExceptionDispatchInfo.Capture(exception));
    }

    /// <summary>
    /// Implicitly converts a result value to a successful <see cref="Outcome{TResult}"/>.
    /// </summary>
    /// <param name="result">The result value.</param>
    public static implicit operator Outcome<TResult>(TResult result)
        => FromResult(result);

    /// <inheritdoc />
    public bool Equals(Outcome<TResult> other)
    {
        if (IsSuccess != other.IsSuccess)
        {
            return false;
        }

        if (IsSuccess)
        {
            return EqualityComparer<TResult>.Default.Equals(_result, other._result);
        }

        return ReferenceEquals(
            _exceptionDispatchInfo!.SourceException,
            other._exceptionDispatchInfo!.SourceException);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is Outcome<TResult> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => IsSuccess
            ? HashCode.Combine(true, _result)
            : HashCode.Combine(false, _exceptionDispatchInfo!.SourceException);

    /// <summary>
    /// Determines whether two outcomes are equal.
    /// </summary>
    public static bool operator ==(Outcome<TResult> left, Outcome<TResult> right)
        => left.Equals(right);

    /// <summary>
    /// Determines whether two outcomes are not equal.
    /// </summary>
    public static bool operator !=(Outcome<TResult> left, Outcome<TResult> right)
        => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString()
        => IsSuccess
            ? $"Outcome<{typeof(TResult).Name}>(Success: {_result})"
            : $"Outcome<{typeof(TResult).Name}>(Failure: {_exceptionDispatchInfo!.SourceException.GetType().Name}: {_exceptionDispatchInfo.SourceException.Message})";
}
