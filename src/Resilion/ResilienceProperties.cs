namespace Resilion;

/// <summary>
/// A type-safe property bag for passing arbitrary data through the resilience pipeline.
/// Strategies and user callbacks can read and write values using <see cref="ResiliencePropertyKey{TValue}"/>.
/// </summary>
/// <remarks>
/// This is not thread-safe. It is expected to be used within a single pipeline execution
/// (one <see cref="ResilienceContext"/> per execution), which is inherently single-threaded
/// through the strategy chain.
/// </remarks>
public sealed class ResilienceProperties
{
    private Dictionary<string, object?>? _properties;

    /// <summary>
    /// Sets a property value for the specified key.
    /// </summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="key">The property key.</param>
    /// <param name="value">The value to set.</param>
    public void Set<TValue>(ResiliencePropertyKey<TValue> key, TValue value)
    {
        _properties ??= new Dictionary<string, object?>(StringComparer.Ordinal);
        _properties[key.Key] = value;
    }

    /// <summary>
    /// Gets the property value for the specified key.
    /// </summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="key">The property key.</param>
    /// <param name="value">When this method returns <c>true</c>, contains the value.</param>
    /// <returns><c>true</c> if the property was found; <c>false</c> otherwise.</returns>
    public bool TryGetValue<TValue>(ResiliencePropertyKey<TValue> key, out TValue? value)
    {
        if (_properties is not null && _properties.TryGetValue(key.Key, out var raw))
        {
            value = (TValue?)raw;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Gets the property value for the specified key, or the default value if not found.
    /// </summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="key">The property key.</param>
    /// <param name="defaultValue">The value to return if the key is not found.</param>
    /// <returns>The property value, or <paramref name="defaultValue"/> if not found.</returns>
    public TValue GetValue<TValue>(ResiliencePropertyKey<TValue> key, TValue defaultValue = default!)
        => TryGetValue(key, out var value) ? value! : defaultValue;

    /// <summary>
    /// Removes the property with the specified key.
    /// </summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="key">The property key.</param>
    /// <returns><c>true</c> if the property was found and removed; <c>false</c> otherwise.</returns>
    public bool Remove<TValue>(ResiliencePropertyKey<TValue> key)
        => _properties?.Remove(key.Key) ?? false;

    /// <summary>
    /// Gets the number of properties stored.
    /// </summary>
    public int Count => _properties?.Count ?? 0;

    /// <summary>
    /// Removes all properties.
    /// </summary>
    /// <summary>
    /// Removes all properties.
    /// </summary>
    internal void Clear() => _properties?.Clear();

    /// <summary>
    /// Copies all properties from another <see cref="ResilienceProperties"/> instance.
    /// Existing properties with the same key are overwritten.
    /// </summary>
    internal void CopyFrom(ResilienceProperties source)
    {
        if (source._properties is null || source._properties.Count == 0)
        {
            return;
        }

        _properties ??= new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var kvp in source._properties)
        {
            _properties[kvp.Key] = kvp.Value;
        }
    }
}
