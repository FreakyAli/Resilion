namespace Resilion;

/// <summary>
/// A strongly-typed key for storing and retrieving values from <see cref="ResilienceProperties"/>.
/// Using typed keys prevents accidental type mismatches when passing data through the pipeline.
/// </summary>
/// <typeparam name="TValue">The type of the value associated with this key.</typeparam>
public readonly struct ResiliencePropertyKey<TValue> : IEquatable<ResiliencePropertyKey<TValue>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResiliencePropertyKey{TValue}"/> struct.
    /// </summary>
    /// <param name="key">The unique string identifier for this property key.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null or empty.</exception>
    public ResiliencePropertyKey(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        Key = key;
    }

    /// <summary>
    /// Gets the unique string identifier for this property key.
    /// </summary>
    public string Key { get; }

    /// <inheritdoc />
    public bool Equals(ResiliencePropertyKey<TValue> other)
        => string.Equals(Key, other.Key, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is ResiliencePropertyKey<TValue> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Key);

    /// <summary>Determines whether two property keys are equal.</summary>
    public static bool operator ==(ResiliencePropertyKey<TValue> left, ResiliencePropertyKey<TValue> right)
        => left.Equals(right);

    /// <summary>Determines whether two property keys are not equal.</summary>
    public static bool operator !=(ResiliencePropertyKey<TValue> left, ResiliencePropertyKey<TValue> right)
        => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => $"ResiliencePropertyKey<{typeof(TValue).Name}>({Key})";
}
